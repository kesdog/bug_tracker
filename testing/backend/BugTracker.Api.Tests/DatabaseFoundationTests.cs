using BugTracker.Api.Database;
using BugTracker.Api.Auth;
using BugTracker.Api.Bugs;
using BugTracker.Api.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed class DatabaseFoundationTests
{
    [Fact]
    public async Task CleanInitialization_CreatesFullEmptySchemaAndMigrationJournal()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();

            await using var connection = await factory.OpenConnectionAsync(readOnly: true);
            var tables = await ReadNamesAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';");

            Assert.Contains("users", tables);
            Assert.Contains("projects", tables);
            Assert.Contains("bug_tickets", tables);
            Assert.Contains("schema_migrations", tables);
            Assert.Contains("demo_reset_state", tables);
            Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM users;"));
            Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM projects;"));
            Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM bug_tickets;"));
            Assert.Equal(11L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM schema_migrations;"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM demo_reset_state WHERE singleton_id = 1 AND generation = 0;"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM demo_reset_state WHERE cleanup_pending = 0 AND wal_checkpoint_completed = 1 AND audit_file_cleanup_completed = 1;"));
            Assert.Equal(3L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('demo_reset_state') WHERE name IN ('cleanup_pending', 'wal_checkpoint_completed', 'audit_file_cleanup_completed') AND [notnull] = 1;"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name = 'username' AND [notnull] = 1;"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'owner_user_id';"));
            Assert.Contains("project_access_requests", tables);
        });
    }

    [Fact]
    public async Task RepeatedMigration_IsIdempotentAndDoesNotDuplicateJournalRows()
    {
        await WithDatabaseAsync(async factory =>
        {
            var runner = new SqliteMigrationRunner(factory);
            await runner.MigrateAsync();
            await runner.MigrateAsync();

            await using var connection = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(11L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM schema_migrations;"));

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT version, name, length(checksum) FROM schema_migrations ORDER BY version;";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal("initial_schema", reader.GetString(1));
            Assert.Equal(64, reader.GetInt32(2));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal("usernames", reader.GetString(1));
            Assert.Equal(64, reader.GetInt32(2));
        });
    }

    [Fact]
    public async Task UsernameMigration_BackfillsUniqueNamesDeterministically()
    {
        await WithDatabaseAsync(async factory =>
        {
            await using var connection = await factory.OpenConnectionAsync(readOnly: false);
            await using (var initial = connection.CreateCommand())
            {
                initial.CommandText = ReadMigration("001_initial.sql");
                await initial.ExecuteNonQueryAsync();
            }

            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO users (user_id, email, password_hash, role) VALUES
                        ('usr_alice_one', 'alice@example.com', 'hash', 'dev'),
                        ('usr_alice_two', 'alice@other.example', 'hash', 'dev');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            await using (var usernames = connection.CreateCommand())
            {
                usernames.CommandText = ReadMigration("002_usernames.sql");
                await usernames.ExecuteNonQueryAsync();
            }

            Assert.Equal(2L, await ScalarLongAsync(connection, "SELECT COUNT(DISTINCT username) FROM users;"));
            Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM users WHERE username IS NULL OR username = '';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_index_list('users') WHERE name = 'ux_users_username_nocase' AND [unique] = 1;"));

            await using var missingUsername = connection.CreateCommand();
            missingUsername.CommandText = "INSERT INTO users (user_id, email, password_hash, role) VALUES ('missing_name', 'missing@example.com', 'hash', 'dev');";
            await Assert.ThrowsAsync<SqliteException>(() => missingUsername.ExecuteNonQueryAsync());
        });
    }

    [Fact]
    public async Task OwnershipMigration_BackfillsDeterministicHumanAdminAndAllocation()
    {
        await WithDatabaseAsync(async factory =>
        {
            await using var connection = await factory.OpenConnectionAsync(readOnly: false);
            foreach (var migration in new[] { "001_initial.sql", "002_usernames.sql", "003_ticket_concurrency_outbox.sql", "004_outbox_leases.sql" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = ReadMigration(migration);
                await command.ExecuteNonQueryAsync();
            }

            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO users (user_id,email,username,password_hash,role,user_type,is_active,created_at) VALUES
                      ('senior-first','senior@example.com','senior_first','hash','senior','human',1,'2025-01-01 00:00:00'),
                      ('admin-later','admin@example.com','admin_later','hash','admin','human',1,'2025-02-01 00:00:00'),
                      ('admin-agent','agent@example.com','admin_agent','hash','admin','agent',1,'2024-01-01 00:00:00');
                    INSERT INTO projects (project_id,name) VALUES ('project-a','A'),('project-b','B');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            await using (var migration = connection.CreateCommand())
            {
                migration.CommandText = ReadMigration("005_project_ownership_access.sql");
                await migration.ExecuteNonQueryAsync();
            }

            Assert.Equal(2L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM projects WHERE owner_user_id = 'admin-later';"));
            Assert.Equal(2L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM project_allocations WHERE user_id = 'admin-later';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_index_list('project_access_requests') WHERE name = 'ux_project_access_requests_pending' AND [unique] = 1;"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ticket_activity') WHERE name = 'subject_user_id';"));
        });
    }

    [Fact]
    public async Task ConnectionFactory_EnablesForeignKeysAndPreservesReadWriteSettings()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();

            await using var write = await factory.OpenConnectionAsync(readOnly: false);
            Assert.Equal(1L, await ScalarLongAsync(write, "PRAGMA foreign_keys;"));
            Assert.Equal(0L, await ScalarLongAsync(write, "PRAGMA query_only;"));
            Assert.Equal(10000L, await ScalarLongAsync(write, "PRAGMA busy_timeout;"));

            await using var read = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(1L, await ScalarLongAsync(read, "PRAGMA foreign_keys;"));
            Assert.Equal(1L, await ScalarLongAsync(read, "PRAGMA query_only;"));
            Assert.Equal(5000L, await ScalarLongAsync(read, "PRAGMA busy_timeout;"));
        });
    }

    [Fact]
    public async Task WriteConnection_RejectsOrphanedForeignKey()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            await using var connection = await factory.OpenConnectionAsync(readOnly: false);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO project_allocations (project_id, user_id) VALUES ('missing-project', 'missing-user');";

            var exception = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(19, exception.SqliteErrorCode);
        });
    }

    [Fact]
    public async Task NonemptyUnversionedDatabase_FailsClearly()
    {
        await WithDatabaseAsync(async factory =>
        {
            await using (var connection = await factory.OpenConnectionAsync(readOnly: false))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE legacy_data (id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new SqliteMigrationRunner(factory).MigrateAsync());
            Assert.Contains("nonempty unversioned", exception.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task BootstrapAdmin_CreatesOnlyOneAdministratorAndAuditRecord()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var provisioner = new DatabaseProvisioner(factory, new PasswordHasherService());

            await provisioner.BootstrapAdminAsync("OWNER@EXAMPLE.COM", "OwnerPass123!");

            await using var connection = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM users WHERE role = 'admin' AND email = 'owner@example.com' AND username = 'owner';"));
            Assert.Equal(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM audit_logs WHERE action = 'bootstrap_admin';"));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provisioner.BootstrapAdminAsync("second@example.com", "SecondPass123!"));
        });
    }

    [Fact]
    public async Task SeedDemo_CreatesDeterministicRelationalDataAndRefusesSecondSeed()
    {
        await WithDatabaseAsync(async factory =>
        {
            await new SqliteMigrationRunner(factory).MigrateAsync();
            var provisioner = new DatabaseProvisioner(factory, new PasswordHasherService());

            await provisioner.SeedDemoAsync();

            await using var connection = await factory.OpenConnectionAsync(readOnly: true);
            Assert.Equal(7L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM users;"));
            Assert.Equal(7L, await ScalarLongAsync(connection, "SELECT COUNT(DISTINCT username) FROM users WHERE length(username) > 0;"));
            Assert.Equal(5L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM projects;"));
            Assert.Equal(60L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM bug_tickets;"));
            Assert.Equal(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.SeedDemoAsync());
        });
    }

    [Fact]
    public async Task ConcurrentOutboxDispatchers_ClaimAuditMessageOnlyOnce()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), $"bug-tracker-outbox-{Guid.NewGuid():N}");
        try
        {
            await WithDatabaseAsync(async factory =>
            {
                await new SqliteMigrationRunner(factory).MigrateAsync();
                await using (var connection = await factory.OpenConnectionAsync(readOnly: false))
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        INSERT INTO outbox_messages
                            (outbox_id, event_id, event_type, payload_json, created_at, available_at)
                        VALUES ('outbox-concurrent-1', 'event-stable-1', 'audit.jsonl',
                            '{"actorType":"human","eventId":"event-stable-1","duplicateIdentity":"event-stable-1"}',
                            datetime('now'), datetime('now'));
                        """;
                    await command.ExecuteNonQueryAsync();
                }

                var hub = new AgentNotificationSocketHub();
                var publisher = new AuditFilePublisher(logDirectory);
                var first = new OutboxDispatcher(factory, hub, publisher, NullLogger<OutboxDispatcher>.Instance);
                var second = new OutboxDispatcher(factory, hub, publisher, NullLogger<OutboxDispatcher>.Instance);
                await Task.WhenAll(first.DispatchBatchAsync(CancellationToken.None), second.DispatchBatchAsync(CancellationToken.None));

                await using var verify = await factory.OpenConnectionAsync(readOnly: true);
                Assert.Equal(1L, await ScalarLongAsync(verify, "SELECT attempts FROM outbox_messages WHERE outbox_id = 'outbox-concurrent-1';"));
                Assert.Equal(1L, await ScalarLongAsync(verify, "SELECT COUNT(*) FROM outbox_messages WHERE outbox_id = 'outbox-concurrent-1' AND processed_at IS NOT NULL AND claim_owner IS NULL;"));
                var lines = await File.ReadAllLinesAsync(Path.Combine(logDirectory, "human-activity.jsonl"));
                Assert.Single(lines);
                Assert.Contains("\"duplicateIdentity\":\"event-stable-1\"", lines[0], StringComparison.Ordinal);
            });
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SlowWebSocketDelivery_DoesNotHoldWriterLockDuringConcurrentTicketMutation()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), $"bug-tracker-slow-send-{Guid.NewGuid():N}");
        try
        {
            await WithDatabaseAsync(async factory =>
            {
                await new SqliteMigrationRunner(factory).MigrateAsync();
                await using (var connection = await factory.OpenConnectionAsync(readOnly: false))
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        INSERT INTO users (user_id, email, username, password_hash, role, user_type)
                        VALUES ('slow-agent', 'slow-agent@example.com', 'slow-agent', 'hash', 'dev', 'agent');
                        INSERT INTO projects (project_id, name, visibility) VALUES ('slow-project', 'Slow project', 'normal');
                        INSERT INTO project_allocations (project_id, user_id) VALUES ('slow-project', 'slow-agent');
                        INSERT INTO bug_tickets
                            (id, issue_title, description, bug_type, reporter_user_id, project_id, assignee_user_id,
                             status, severity, priority, tags_json)
                        VALUES ('slow-ticket', 'Slow socket', 'Original', 'api', 'slow-agent', 'slow-project',
                                'slow-agent', 'open', 'mid', 'p2', '[]');
                        INSERT INTO notifications
                            (notification_id, user_id, ticket_id, kind, message, is_read, event_id, ticket_version)
                        VALUES ('slow-notification', 'slow-agent', 'slow-ticket', 'ticket_commented', 'Work item', 0,
                                'slow-event', 1);
                        INSERT INTO outbox_messages
                            (outbox_id, event_id, event_type, aggregate_id, ticket_version, payload_json, created_at, available_at)
                        VALUES ('slow-outbox', 'slow-event', 'notification.websocket', 'slow-ticket', 1,
                            '{"id":"slow-notification","userId":"slow-agent","ticketId":"slow-ticket","kind":"ticket_commented","message":"Work item","isRead":false,"createdAt":"2026-01-01 00:00:00","eventId":"slow-event","ticketVersion":1}',
                            datetime('now'), datetime('now'));
                        """;
                    await command.ExecuteNonQueryAsync();
                }

                var blockedPublisher = new BlockingNotificationPublisher();
                var dispatcher = new OutboxDispatcher(factory, blockedPublisher, new AuditFilePublisher(logDirectory), NullLogger<OutboxDispatcher>.Instance);
                var dispatchTask = dispatcher.DispatchBatchAsync(CancellationToken.None);
                await blockedPublisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

                var repository = new BugRepository(factory, new TicketWriteAuthorizationService());
                var writeTask = repository.UpdateInitialBugReportAsync(
                    "slow-ticket", "Concurrent committed edit", [], "slow-agent", "agent", 1,
                    DateTimeOffset.UtcNow, CancellationToken.None);
                var writeResult = await writeTask.WaitAsync(TimeSpan.FromSeconds(1));

                Assert.NotNull(writeResult.Value);
                Assert.Equal(2, writeResult.Value!.Version);
                Assert.False(dispatchTask.IsCompleted);

                blockedPublisher.Release.TrySetResult();
                await dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));

                await using var verify = await factory.OpenConnectionAsync(readOnly: true);
                Assert.Equal(1L, await ScalarLongAsync(verify, "SELECT COUNT(*) FROM outbox_messages WHERE outbox_id = 'slow-outbox' AND processed_at IS NOT NULL;"));
            });
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }
    }

    private static async Task WithDatabaseAsync(Func<SqliteConnectionFactory, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bug-tracker-foundation-{Guid.NewGuid():N}.db");
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
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }

    private static async Task<HashSet<string>> ReadNamesAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string ReadMigration(string fileName)
    {
        var assembly = typeof(SqliteMigrationRunner).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".Migrations.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class BlockingNotificationPublisher : IAgentNotificationPublisher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SendNotificationAsync(NotificationDto notification, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }
}
