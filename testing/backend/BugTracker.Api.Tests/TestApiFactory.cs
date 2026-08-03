using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using BugTracker.Api;
using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Bugs;
using BugTracker.Api.Database;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed class TestApiFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string DefaultUserId = "usr_test_dev_001";
    public const string DefaultUserEmail = "dev.test@example.com";
    public const string SeniorUserId = "usr_test_senior_001";
    public const string SeniorUserEmail = "senior.test@example.com";
    public const string AdminUserId = "usr_test_admin_001";
    public const string AdminUserEmail = "admin.test@example.com";
    public const string AgentUserId = "usr_test_agent_001";
    public const string AgentUserEmail = "agent.test@example.com";
    public const string DefaultUserPassword = "P@ssword123!";
    public const string TestTokenSecret = "integration-test-secret-key-at-least-32-characters";

    private readonly string _dbPath;
    private readonly string _logDirectory;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly long? _readinessMinimumFreeBytes;
    private readonly int? _agentHeartbeatIntervalSeconds;
    private readonly string _environment;
    private readonly int? _demoMaxTickets;

    public TestApiFactory()
        : this(null, null, true, "Testing", null)
    {
    }

    public static TestApiFactory WithReadinessMinimum(long readinessMinimumFreeBytes) =>
        new(readinessMinimumFreeBytes, null, true, "Testing", null);

    public static TestApiFactory WithAgentHeartbeatInterval(int heartbeatIntervalSeconds) =>
        new(null, heartbeatIntervalSeconds, true, "Testing", null);

    public static TestApiFactory CreateEmpty() => new(null, null, false, "Testing", null);
    public static TestApiFactory CreateDemo(int? maxTickets = null) => new(null, null, true, "Demo", maxTickets);

    private TestApiFactory(long? readinessMinimumFreeBytes, int? agentHeartbeatIntervalSeconds, bool seedBaseline, string environment, int? demoMaxTickets)
    {
        _readinessMinimumFreeBytes = readinessMinimumFreeBytes;
        _agentHeartbeatIntervalSeconds = agentHeartbeatIntervalSeconds;
        _environment = environment;
        _demoMaxTickets = demoMaxTickets;
        _dbPath = Path.Combine(Path.GetTempPath(), $"bug-tracker-tests-{Guid.NewGuid():N}.db");
        _logDirectory = Path.Combine(Path.GetTempPath(), $"bug-tracker-test-logs-{Guid.NewGuid():N}");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        InitializeDatabase(seedBaseline);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Auth:TokenSecret", TestTokenSecret);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var overrideSettings = new Dictionary<string, string?>
            {
                ["Database:Path"] = _dbPath,
                ["Auth:TokenSecret"] = TestTokenSecret,
                ["Audit:LogDirectory"] = _logDirectory
            };

            if (_readinessMinimumFreeBytes is not null)
            {
                overrideSettings["Readiness:MinimumFreeBytes"] = _readinessMinimumFreeBytes.Value.ToString();
            }
            if (_agentHeartbeatIntervalSeconds is not null)
            {
                overrideSettings["AgentWebSocket:HeartbeatIntervalSeconds"] = _agentHeartbeatIntervalSeconds.Value.ToString();
                overrideSettings["AgentWebSocket:HeartbeatRetryIntervalSeconds"] = "1";
            }
            if (_demoMaxTickets is not null)
            {
                overrideSettings["DemoAbuse:MaxTickets"] = _demoMaxTickets.Value.ToString();
            }

            configBuilder.AddInMemoryCollection(overrideSettings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AuthRepository>();
            services.RemoveAll<BugRepository>();
            services.RemoveAll<TicketWriteAuthorizationService>();
            services.RemoveAll<ProjectRepository>();
            services.RemoveAll<AuditRepository>();
            services.RemoveAll<AuditLogger>();
            services.RemoveAll<NotificationRepository>();
            services.RemoveAll<SqliteConnectionFactory>();
            services.RemoveAll<TokenService>();

            services.AddSingleton(new TokenService(TestTokenSecret));
            services.AddSingleton(_connectionFactory);
            services.AddSingleton(new AuthRepository(_connectionFactory));
            services.AddSingleton<TicketWriteAuthorizationService>();
            services.AddSingleton(sp => new BugRepository(_connectionFactory, sp.GetRequiredService<TicketWriteAuthorizationService>()));
            services.AddSingleton(new ProjectRepository(_connectionFactory));
            services.AddSingleton(new AuditRepository(_connectionFactory));
            services.AddSingleton(new NotificationRepository(_connectionFactory));
            services.AddSingleton(sp => new AuditLogger(sp.GetRequiredService<AuditRepository>(), _logDirectory));
        });
    }

    public async Task<string> CreateAccessTokenAsync()
    {
        return await CreateAccessTokenAsync(DefaultUserId);
    }

    public async Task<string> CreateAccessTokenAsync(string userId)
    {
        return await CreateAccessTokenAsync(userId, DateTimeOffset.UtcNow.AddHours(24));
    }

    public async Task<string> CreateAccessTokenAsync(string userId, DateTimeOffset expiresAt)
    {
        var tokenService = new TokenService(TestTokenSecret);
        var rawToken = tokenService.GenerateRawToken();
        var tokenHash = tokenService.HashToken(rawToken);
        var now = DateTimeOffset.UtcNow;

        await using var connection = await _connectionFactory.OpenConnectionAsync(readOnly: false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO auth_tokens (token_id, user_id, token_hash, issued_at, expires_at)
            VALUES ($token_id, $user_id, $token_hash, $issued_at, $expires_at);

            UPDATE users
            SET last_seen_at = $issued_at
            WHERE user_id = $user_id;
            """;
        command.Parameters.AddWithValue("$token_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$user_id", userId);
        command.Parameters.AddWithValue("$token_hash", tokenHash);
        command.Parameters.AddWithValue("$issued_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$expires_at", expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        await command.ExecuteNonQueryAsync();

        return rawToken;
    }

    public async Task SeedBugAsync(SeedBugRequest bug)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(readOnly: false);

        const string sql = """
            INSERT OR REPLACE INTO bug_tickets (
                id,
                issue_title,
                description,
                bug_type,
                project_id,
                reporter_user_id,
                assignee_user_id,
                created_at,
                updated_at,
                status,
                severity,
                priority,
                tags_json,
                close_date,
                resolved_by_user_id,
                assigned_at,
                resolution_notes,
                post_resolution_report,
                resolution_report_images_json
            )
            VALUES (
                $id,
                $issue_title,
                $description,
                $bug_type,
                $project_id,
                $reporter_user_id,
                $assignee_user_id,
                $created_at,
                $updated_at,
                $status,
                $severity,
                $priority,
                $tags_json,
                $close_date,
                $resolved_by_user_id,
                $assigned_at,
                NULL,
                NULL,
                NULL
            );
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", bug.Id);
        command.Parameters.AddWithValue("$issue_title", bug.IssueTitle);
        command.Parameters.AddWithValue("$description", bug.Description);
        command.Parameters.AddWithValue("$bug_type", bug.BugType);
        command.Parameters.AddWithValue("$project_id", (object?)bug.ProjectId ?? "project-general");
        command.Parameters.AddWithValue("$reporter_user_id", bug.ReporterUserId ?? DefaultUserId);
        command.Parameters.AddWithValue("$assignee_user_id", (object?)bug.AssigneeUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", bug.CreatedAt);
        command.Parameters.AddWithValue("$updated_at", bug.UpdatedAt);
        command.Parameters.AddWithValue("$status", bug.Status);
        command.Parameters.AddWithValue("$severity", bug.Severity);
        command.Parameters.AddWithValue("$priority", (object?)bug.Priority ?? "p2");
        command.Parameters.AddWithValue("$tags_json", System.Text.Json.JsonSerializer.Serialize(bug.Tags ?? []));
        command.Parameters.AddWithValue("$close_date", (object?)bug.CloseDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$resolved_by_user_id", (object?)bug.ResolvedByUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$assigned_at", (object?)bug.AssignedAt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public Task SeedDemoAsync() =>
        new DatabaseProvisioner(_connectionFactory, new PasswordHasherService()).SeedDemoAsync();

    public async Task<long> ScalarLongAsync(string sql, string ticketId)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(readOnly: true);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", ticketId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(readOnly: false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private void InitializeDatabase(bool seedBaseline)
    {
        new SqliteMigrationRunner(_connectionFactory).MigrateAsync().GetAwaiter().GetResult();

        if (!seedBaseline)
        {
            return;
        }

        using var connection = _connectionFactory.OpenConnectionAsync(readOnly: false).GetAwaiter().GetResult();

        var passwordHasher = new PasswordHasherService();
        var hash = passwordHasher.Hash(DefaultUserPassword);

        using var seedProjectCommand = connection.CreateCommand();
        seedProjectCommand.CommandText = """
            INSERT OR IGNORE INTO projects (project_id, name, created_at, updated_at)
            VALUES ('project-general', 'General', '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """;
        seedProjectCommand.ExecuteNonQuery();

        using var seedCommand = connection.CreateCommand();
        seedCommand.CommandText = """
            INSERT INTO users (user_id, email, username, password_hash, role, is_active, created_at, updated_at)
            VALUES ($user_id, $email, 'dev_test', $password_hash, 'dev', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """;
        seedCommand.Parameters.AddWithValue("$user_id", DefaultUserId);
        seedCommand.Parameters.AddWithValue("$email", DefaultUserEmail);
        seedCommand.Parameters.AddWithValue("$password_hash", hash);
        seedCommand.ExecuteNonQuery();

        using var seedSeniorCommand = connection.CreateCommand();
        seedSeniorCommand.CommandText = """
            INSERT INTO users (user_id, email, username, password_hash, role, is_active, created_at, updated_at)
            VALUES ($user_id, $email, 'senior_test', $password_hash, 'senior', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """;
        seedSeniorCommand.Parameters.AddWithValue("$user_id", SeniorUserId);
        seedSeniorCommand.Parameters.AddWithValue("$email", SeniorUserEmail);
        seedSeniorCommand.Parameters.AddWithValue("$password_hash", hash);
        seedSeniorCommand.ExecuteNonQuery();

        using var seedAdminCommand = connection.CreateCommand();
        seedAdminCommand.CommandText = """
            INSERT INTO users (user_id, email, username, password_hash, role, is_active, created_at, updated_at)
            VALUES ($user_id, $email, 'admin_test', $password_hash, 'admin', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """;
        seedAdminCommand.Parameters.AddWithValue("$user_id", AdminUserId);
        seedAdminCommand.Parameters.AddWithValue("$email", AdminUserEmail);
        seedAdminCommand.Parameters.AddWithValue("$password_hash", hash);
        seedAdminCommand.ExecuteNonQuery();

        using var seedAgentCommand = connection.CreateCommand();
        seedAgentCommand.CommandText = """
            INSERT INTO users (user_id, email, username, password_hash, role, user_type, is_active, created_at, updated_at)
            VALUES ($user_id, $email, 'agent_test', 'agent-login-disabled', 'dev', 'agent', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """;
        seedAgentCommand.Parameters.AddWithValue("$user_id", AgentUserId);
        seedAgentCommand.Parameters.AddWithValue("$email", AgentUserEmail);
        seedAgentCommand.ExecuteNonQuery();

        using var seedDevAllocationCommand = connection.CreateCommand();
        seedDevAllocationCommand.CommandText = """
            INSERT INTO project_allocations (project_id, user_id, created_at)
            VALUES ('project-general', $user_id, '2026-01-01 00:00:00');
            """;
        seedDevAllocationCommand.Parameters.AddWithValue("$user_id", DefaultUserId);
        seedDevAllocationCommand.ExecuteNonQuery();

        using var seedSeniorAllocationCommand = connection.CreateCommand();
        seedSeniorAllocationCommand.CommandText = """
            INSERT INTO project_allocations (project_id, user_id, created_at)
            VALUES ('project-general', $user_id, '2026-01-01 00:00:00');
            """;
        seedSeniorAllocationCommand.Parameters.AddWithValue("$user_id", SeniorUserId);
        seedSeniorAllocationCommand.ExecuteNonQuery();

        using var seedAdminAllocationCommand = connection.CreateCommand();
        seedAdminAllocationCommand.CommandText = """
            INSERT INTO project_allocations (project_id, user_id, created_at)
            VALUES ('project-general', $user_id, '2026-01-01 00:00:00');
            """;
        seedAdminAllocationCommand.Parameters.AddWithValue("$user_id", AdminUserId);
        seedAdminAllocationCommand.ExecuteNonQuery();

        using var seedAgentAllocationCommand = connection.CreateCommand();
        seedAgentAllocationCommand.CommandText = """
            INSERT INTO project_allocations (project_id, user_id, created_at)
            VALUES ('project-general', $user_id, '2026-01-01 00:00:00');
            """;
        seedAgentAllocationCommand.Parameters.AddWithValue("$user_id", AgentUserId);
        seedAgentAllocationCommand.ExecuteNonQuery();
    }
}
