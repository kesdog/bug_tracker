using System.Diagnostics;
using System.Net.WebSockets;
using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Database;
using BugTracker.Api.Health;
using BugTracker.Api.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed class DemoResetTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DueLogic_IsUtcDaily_FirstRunImmediate_AndCatchesUpMissedDays()
    {
        var beforeHour = new DateTimeOffset(2026, 7, 31, 2, 0, 0, TimeSpan.Zero);

        Assert.True(DemoResetCoordinator.IsDue(null, beforeHour, 4));
        Assert.False(DemoResetCoordinator.IsDue(beforeHour.AddHours(-1), beforeHour, 4));
        Assert.False(DemoResetCoordinator.IsDue(beforeHour.AddDays(-1), beforeHour, 4));
        Assert.True(DemoResetCoordinator.IsDue(beforeHour.AddDays(-2), beforeHour, 4));
        Assert.True(DemoResetCoordinator.IsDue(beforeHour.AddDays(-1), beforeHour.AddHours(3), 4));
        Assert.Equal(
            new DateTimeOffset(2026, 7, 31, 4, 0, 0, TimeSpan.Zero),
            DemoResetCoordinator.NextScheduledAt(beforeHour, 4));
    }

    [Fact]
    public async Task Coordinator_FirstStartupResetsImmediately_AndSameDayRestartIsIdempotent()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var time = new FixedTimeProvider(Now);
            var options = Options.Create(new DemoResetOptions
            {
                Enabled = true,
                HourUtc = 23,
                AllowedEnvironments = ["Testing"]
            });
            var service = new DemoResetService(factory, new PasswordHasherService(), time);
            var auditDirectory = Path.Combine(Path.GetTempPath(), $"bug-tracker-reset-audit-{Guid.NewGuid():N}");

            DemoResetCoordinator CreateCoordinator() => new(
                service,
                options,
                new TestEnvironment("Testing"),
                new ResetMaintenanceState(),
                new OutboxDispatchGate(),
                new AgentNotificationSocketHub(),
                new AuditFilePublisher(auditDirectory),
                time,
                NullLogger<DemoResetCoordinator>.Instance);

            Assert.True(await CreateCoordinator().RunIfDueAsync());
            Assert.False(await CreateCoordinator().RunIfDueAsync());

            await using var connection = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(1, await ScalarAsync(connection, "SELECT generation FROM demo_reset_state;"));
        });
    }

    [Fact]
    public async Task MaintenanceAndOutboxGates_BlockNewWork_AndDrainActiveWork()
    {
        var maintenance = new ResetMaintenanceState();
        var request = maintenance.TryBeginApiRequest();
        Assert.NotNull(request);
        var reset = maintenance.BeginResetAndDrainAsync();
        Assert.True(maintenance.IsResetInProgress);
        Assert.False(reset.IsCompleted);
        Assert.Null(maintenance.TryBeginApiRequest());
        request.Dispose();
        using var resetLease = await reset;

        var outbox = new OutboxDispatchGate();
        var dispatch = await outbox.EnterAsync(CancellationToken.None);
        var pause = outbox.PauseAndDrainAsync(CancellationToken.None);
        Assert.False(pause.IsCompleted);
        dispatch.Dispose();
        using var pauseLease = await pause;
        var waitingDispatch = outbox.EnterAsync(CancellationToken.None);
        Assert.False(waitingDispatch.IsCompleted);
        pauseLease.Dispose();
        (await waitingDispatch).Dispose();
    }

    [Fact]
    public async Task WebSocketEstablishmentLease_HandsOffToResetWithoutARequestLeaseRace()
    {
        var maintenance = new ResetMaintenanceState();
        var middleware = new ResetMaintenanceMiddleware(maintenance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/agent/notifications/ws";
        Task<IDisposable>? resetTask = null;

        await middleware.InvokeAsync(context, async currentContext =>
        {
            var establishment = currentContext.Features.Get<IWebSocketEstablishmentLease>();
            Assert.NotNull(establishment);
            resetTask = maintenance.BeginResetAndDrainAsync();
            Assert.False(resetTask.IsCompleted);

            establishment.CompleteEstablishment();
            using var resetLease = await resetTask;
        });

        Assert.NotNull(resetTask);
        Assert.False(maintenance.IsResetInProgress);
    }

    [Fact]
    public async Task ResetWaitsForDisconnectedAudit_ThenDeletesOldIdentityAuditAndOutbox()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var previousDay = new FixedTimeProvider(Now.AddDays(-1));
            var service = new DemoResetService(factory, new PasswordHasherService(), previousDay);
            var options = new DemoResetOptions { Enabled = true, AllowedEnvironments = ["Testing"] };
            await service.ResetAsync(options, "Testing");
            await service.CheckpointWalAsync();
            await service.MarkWalCheckpointCompletedAsync();
            await service.MarkAuditFileCleanupCompletedAsync();
            await service.CompleteCleanupAsync();

            await using (var connection = await factory.OpenConnectionAsync(readOnly: false))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO users (user_id, email, username, password_hash, role, user_type)
                    VALUES ('old-agent', 'old-agent@example.com', 'old-agent', 'hash', 'dev', 'agent');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var auditDirectory = Path.Combine(Path.GetTempPath(), $"bug-tracker-reset-race-{Guid.NewGuid():N}");
            var hub = new AgentNotificationSocketHub();
            using var socket = new ResetRaceWebSocket();
            var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var principal = new AuthenticatedUser(
                "old-agent", "old-agent@example.com", "dev", "agent", "old-hash", DateTimeOffset.UtcNow.AddHours(1));
            var auditLogger = new AuditLogger(new AuditRepository(factory), auditDirectory);
            var handler = hub.HandleConnectionAsync(
                principal,
                socket,
                _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
                _ => Task.FromResult<AuthenticatedUser?>(principal),
                () => Task.CompletedTask,
                async (state, token) =>
                {
                    callbackStarted.TrySetResult();
                    await releaseCallback.Task;
                    await auditLogger.LogAsync(
                        principal, "agent_ws_disconnected", "old identity disconnected", null,
                        new { State = state }, token);
                },
                CancellationToken.None);
            await socket.HelloSent.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var coordinator = new DemoResetCoordinator(
                new DemoResetService(factory, new PasswordHasherService(), new FixedTimeProvider(Now)),
                Options.Create(options),
                new TestEnvironment("Testing"),
                new ResetMaintenanceState(),
                new OutboxDispatchGate(),
                hub,
                new AuditFilePublisher(auditDirectory),
                new FixedTimeProvider(Now),
                NullLogger<DemoResetCoordinator>.Instance);
            var reset = coordinator.RunIfDueAsync();
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(reset.IsCompleted);
            Assert.Equal(1, (await service.GetStateAsync()).Generation);

            releaseCallback.TrySetResult();
            Assert.True(await reset.WaitAsync(TimeSpan.FromSeconds(5)));
            await handler.WaitAsync(TimeSpan.FromSeconds(2));

            await using var verify = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(2, await ScalarAsync(verify, "SELECT generation FROM demo_reset_state;"));
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM users WHERE user_id = 'old-agent';"));
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM audit_logs WHERE actor_user_id = 'old-agent';"));
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM outbox_messages WHERE event_type = 'audit.jsonl';"));

            if (Directory.Exists(auditDirectory)) Directory.Delete(auditDirectory, recursive: true);
        });
    }

    [Fact]
    public async Task ReadinessLease_IsHeldUntilResponsePipelineCompletes()
    {
        var maintenance = new ResetMaintenanceState();
        var middleware = new ResetMaintenanceMiddleware(maintenance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/ready";
        var responseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var request = middleware.InvokeAsync(context, async _ =>
        {
            responseStarted.SetResult();
            await finishResponse.Task;
        });
        await responseStarted.Task;
        var reset = maintenance.BeginResetAndDrainAsync();
        Assert.False(reset.IsCompleted);

        finishResponse.SetResult();
        await request;
        using var resetLease = await reset;
    }

    [Fact]
    public async Task Coordinator_ForceCancelsRequestWhenApiDrainExceedsConfiguredTimeout()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var maintenance = new ResetMaintenanceState();
            using var requestCancellation = new CancellationTokenSource();
            var activeRequest = maintenance.TryBeginApiRequest(requestCancellation);
            Assert.NotNull(activeRequest);
            var releaseRequest = Task.Run(async () =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, requestCancellation.Token).ContinueWith(_ => { });
                activeRequest.Dispose();
            });
            var coordinator = new DemoResetCoordinator(
                new DemoResetService(factory, new PasswordHasherService(), new FixedTimeProvider(Now)),
                Options.Create(new DemoResetOptions
                {
                    Enabled = true,
                    AllowedEnvironments = ["Testing"],
                    DrainTimeoutSeconds = 1
                }),
                new TestEnvironment("Testing"),
                maintenance,
                new OutboxDispatchGate(),
                new AgentNotificationSocketHub(),
                new AuditFilePublisher(Path.GetTempPath()),
                new FixedTimeProvider(Now),
                NullLogger<DemoResetCoordinator>.Instance);

            Assert.True(await coordinator.RunIfDueAsync());
            await releaseRequest;
            Assert.False(maintenance.IsResetInProgress);
            Assert.NotNull(await new DemoResetService(factory, new PasswordHasherService()).GetLastResetAtAsync());
        });
    }

    [Fact]
    public async Task Coordinator_AuditCleanupFailureIsNonFatal_AndRetryDoesNotReseed()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var service = new DemoResetService(factory, new PasswordHasherService(), new FixedTimeProvider(Now));
            var auditDirectory = Path.Combine(Path.GetTempPath(), $"bug-tracker-reset-audit-retry-{Guid.NewGuid():N}");
            Directory.CreateDirectory(auditDirectory);
            var auditPath = Path.Combine(auditDirectory, "human-activity.jsonl");
            await File.WriteAllTextAsync(auditPath, "old audit");
            var publisher = new FailFirstAuditFilePublisher(auditDirectory);
            var coordinator = new DemoResetCoordinator(
                service,
                Options.Create(new DemoResetOptions { Enabled = true, AllowedEnvironments = ["Testing"] }),
                new TestEnvironment("Testing"),
                new ResetMaintenanceState(),
                new OutboxDispatchGate(),
                new AgentNotificationSocketHub(),
                publisher,
                new FixedTimeProvider(Now),
                NullLogger<DemoResetCoordinator>.Instance);

            Assert.True(await coordinator.RunIfDueAsync());
            Assert.Equal(Now, await service.GetLastResetAtAsync());
            var pending = await service.GetStateAsync();
            Assert.Equal(1, pending.Generation);
            Assert.True(pending.CleanupPending);
            Assert.True(pending.WalCheckpointCompleted);
            Assert.False(pending.AuditFileCleanupCompleted);
            Assert.True(File.Exists(auditPath));

            Assert.False(await coordinator.RunIfDueAsync());
            var completed = await service.GetStateAsync();
            Assert.Equal(1, completed.Generation);
            Assert.False(completed.CleanupPending);
            Assert.False(File.Exists(auditPath));
            Directory.Delete(auditDirectory, recursive: true);
        });
    }

    [Fact]
    public async Task SeedDemo_ProducesContractedRelativeFixture()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            await new DatabaseProvisioner(factory, new PasswordHasherService(), new FixedTimeProvider(Now)).SeedDemoAsync();

            await using var connection = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(7, await ScalarAsync(connection, "SELECT COUNT(*) FROM users WHERE is_active = 1 AND user_type = 'human';"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM users WHERE user_type = 'agent';"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM users WHERE role = 'admin';"));
            Assert.Equal(2, await ScalarAsync(connection, "SELECT COUNT(*) FROM users WHERE role = 'senior';"));
            Assert.Equal(4, await ScalarAsync(connection, "SELECT COUNT(*) FROM users WHERE role = 'dev';"));
            Assert.Equal(5, await ScalarAsync(connection, "SELECT COUNT(*) FROM projects WHERE name IN ('bugtracker','currency & metal converter','website (personal)','reservation system','socket manager');"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM projects WHERE name = 'socket manager' AND visibility = 'sensitive' AND owner_user_id = 'usr_admin_001';"));
            Assert.Equal(5, await ScalarAsync(connection, "SELECT COUNT(*) FROM (SELECT project_id FROM bug_tickets GROUP BY project_id HAVING COUNT(*) = 12 AND SUM(status='todo') = 4 AND SUM(status='open') = 4 AND SUM(status='reopened') = 1 AND SUM(status='closed') = 3);"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM bug_tickets WHERE created_at < '2026-07-04 12:00:00' OR created_at > '2026-07-31 12:00:00';"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM bug_tickets WHERE id NOT LIKE 'demo-g000001-%';"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM ticket_activity WHERE activity_id NOT LIKE 'demo-activity-g000001-%';"));
            Assert.Equal(60, await ScalarAsync(connection, "SELECT COUNT(DISTINCT issue_title) FROM bug_tickets;"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM bug_tickets WHERE lower(issue_title) LIKE '%demo issue%' OR lower(description) LIKE '%demo scenario%';"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM bug_tickets WHERE length(trim(description)) < 40 OR length(trim(expected_behavior)) < 30 OR length(trim(actual_behavior)) < 30 OR length(trim(steps_to_reproduce)) < 30 OR json_array_length(tags_json) < 3;"));
            Assert.Equal(3, await ScalarAsync(connection, "SELECT COUNT(DISTINCT reporter_user_id) FROM bug_tickets WHERE project_id = 'project-socket-manager';"));
            Assert.Equal(3, await ScalarAsync(connection, "SELECT COUNT(DISTINCT assignee_user_id) FROM bug_tickets WHERE project_id = 'project-socket-manager';"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM bug_tickets t WHERE NOT EXISTS (SELECT 1 FROM project_allocations a WHERE a.project_id = t.project_id AND a.user_id = t.reporter_user_id) OR (t.assignee_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM project_allocations a WHERE a.project_id = t.project_id AND a.user_id = t.assignee_user_id));"));
            Assert.Equal(7, await ScalarAsync(connection, "SELECT COUNT(DISTINCT user_id) FROM (SELECT reporter_user_id AS user_id FROM bug_tickets UNION SELECT assignee_user_id FROM bug_tickets WHERE assignee_user_id IS NOT NULL);"));
            Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        });
    }

    [Fact]
    public async Task Reset_IsGuarded_AndAtomicallyReplacesAllRuntimeData()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var service = new DemoResetService(factory, new PasswordHasherService(), new FixedTimeProvider(Now));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResetAsync(new DemoResetOptions { Enabled = false, AllowedEnvironments = ["Development"] }, "Development"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResetAsync(new DemoResetOptions { Enabled = true, AllowedEnvironments = ["Development"] }, "Production"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResetAsync(new DemoResetOptions { Enabled = true, AllowedEnvironments = ["Production"] }, "Production"));

            var options = new DemoResetOptions { Enabled = true, AllowedEnvironments = ["Development"] };
            var first = await service.ResetAsync(options, "Development");

            await using (var connection = await factory.OpenConnectionAsync(readOnly: false))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO auth_tokens (token_id, user_id, token_hash, expires_at)
                    VALUES ('runtime-token', 'usr_admin_001', 'runtime-hash', '2099-01-01 00:00:00');
                    INSERT INTO notifications (notification_id, user_id, kind, message)
                    VALUES ('runtime-notification', 'usr_admin_001', 'test', 'remove me');
                    INSERT INTO outbox_messages (outbox_id, event_id, event_type, payload_json)
                    VALUES ('runtime-outbox', 'runtime-event', 'audit.jsonl', '{}');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var second = await service.ResetAsync(options, "development");
            Assert.Equal(1, first.Generation);
            Assert.Equal(2, second.Generation);

            await using var verify = await factory.OpenConnectionAsync(readOnly: false);
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM auth_tokens;"));
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM notifications;"));
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM outbox_messages;"));
            Assert.Equal(60, await ScalarAsync(verify, "SELECT COUNT(*) FROM bug_tickets;"));
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM bug_tickets WHERE id NOT LIKE 'demo-g000002-%';"));
            Assert.Equal(13, await ScalarAsync(verify, "SELECT COUNT(*) FROM schema_migrations;"));
            Assert.Equal(1, await ScalarAsync(verify, "SELECT COUNT(*) FROM demo_reset_state WHERE generation = 2 AND last_environment = 'development';"));
            Assert.Equal(1, await ScalarAsync(verify, "SELECT cleanup_pending FROM demo_reset_state;"));
            Assert.Equal(0, await ScalarAsync(verify, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
            Assert.Equal(1, await ScalarAsync(verify, "PRAGMA secure_delete;"));
        });
    }

    [Fact]
    public async Task Coordinator_CommitsOnce_ClearsAuditDespiteBusyCheckpoint_AndRetriesCleanupWithoutReseed()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var auditDirectory = Path.Combine(Path.GetTempPath(), $"bug-tracker-reset-cleanup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(auditDirectory);
            var auditPath = Path.Combine(auditDirectory, "agent-activity.jsonl");
            await File.WriteAllTextAsync(auditPath, "old agent identity");
            await using var readerConnection = await factory.OpenConnectionAsync(readOnly: false);
            await using var readerTransaction = readerConnection.BeginTransaction(deferred: true);
            await using (var read = readerConnection.CreateCommand())
            {
                read.Transaction = (SqliteTransaction)readerTransaction;
                read.CommandText = "SELECT generation FROM demo_reset_state;";
                _ = await read.ExecuteScalarAsync();
            }

            var service = new DemoResetService(factory, new PasswordHasherService(), new FixedTimeProvider(Now));
            var coordinator = CreateCoordinator(service, new FixedTimeProvider(Now), auditDirectory);
            Assert.True(await coordinator.RunIfDueAsync());
            Assert.False(File.Exists(auditPath));

            var pending = await service.GetStateAsync();
            Assert.Equal(1, pending.Generation);
            Assert.True(pending.CleanupPending);
            Assert.False(pending.WalCheckpointCompleted);
            Assert.True(pending.AuditFileCleanupCompleted);

            await readerTransaction.RollbackAsync();
            Assert.False(await coordinator.RunIfDueAsync());

            var completed = await service.GetStateAsync();
            Assert.Equal(1, completed.Generation);
            Assert.False(completed.CleanupPending);
            Assert.True(completed.WalCheckpointCompleted);
            Assert.True(completed.AuditFileCleanupCompleted);
            await using var verify = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(1, await ScalarAsync(verify, "SELECT generation FROM demo_reset_state;"));
            Assert.Equal(60, await ScalarAsync(verify, "SELECT COUNT(*) FROM bug_tickets WHERE id LIKE 'demo-g000001-%';"));

            Directory.Delete(auditDirectory, recursive: true);
        });
    }

    private static async Task WithDatabaseAsync(Func<SqliteConnectionFactory, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bug-tracker-demo-reset-{Guid.NewGuid():N}.db");
        try
        {
            await test(new SqliteConnectionFactory(path));
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var file = path + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }

    private static DemoResetCoordinator CreateCoordinator(
        DemoResetService service,
        TimeProvider timeProvider,
        string auditDirectory) => new(
            service,
            Options.Create(new DemoResetOptions
            {
                Enabled = true,
                AllowedEnvironments = ["Testing"]
            }),
            new TestEnvironment("Testing"),
            new ResetMaintenanceState(),
            new OutboxDispatchGate(),
            new AgentNotificationSocketHub(),
            new AuditFilePublisher(auditDirectory),
            timeProvider,
            NullLogger<DemoResetCoordinator>.Instance);

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BugTracker.Api.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ResetRaceWebSocket : WebSocket
    {
        private readonly CancellationTokenSource _closed = new();
        private WebSocketState _state = WebSocketState.Open;

        public TaskCompletionSource HelloSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
            _closed.Cancel();
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            _closed.Cancel();
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose()
        {
            _closed.Cancel();
            _closed.Dispose();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            throw new UnreachableException();
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            HelloSent.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FailFirstAuditFilePublisher(string logDirectory) : AuditFilePublisher(logDirectory)
    {
        private int _attempt;

        public override Task ClearAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _attempt) == 1)
            {
                throw new IOException("Injected audit cleanup failure.");
            }

            return base.ClearAsync(ct);
        }
    }
}
