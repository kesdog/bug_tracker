using BugTracker.Api.Auth;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Database;

public sealed class DatabaseProvisioner
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly PasswordHasherService _passwordHasher;
    private readonly TimeProvider _timeProvider;

    public DatabaseProvisioner(
        SqliteConnectionFactory connectionFactory,
        PasswordHasherService passwordHasher,
        TimeProvider? timeProvider = null)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> BootstrapAdminAsync(string email, string password, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var username = UsernamePolicy.DefaultFromEmail(normalizedEmail);
        ValidatePassword(password);

        await using var connection = await _connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM users WHERE role = 'admin';";
            if (Convert.ToInt64(await countCommand.ExecuteScalarAsync(ct)) != 0)
            {
                throw new InvalidOperationException("Administrator bootstrap is disabled because an administrator already exists.");
            }
        }

        var userId = $"usr_admin_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO users (
                    user_id, email, username, password_hash, role, user_type, projects_json, is_active, created_at, updated_at
                ) VALUES (
                    $user_id, $email, $username, $password_hash, 'admin', 'human', '[]', 1, $now, $now
                );

                INSERT INTO audit_logs (actor_user_id, actor_type, action, message, created_at)
                VALUES ($user_id, 'human', 'bootstrap_admin', 'Initial administrator created by local bootstrap command.', $now);
                """;
            insertCommand.Parameters.AddWithValue("$user_id", userId);
            insertCommand.Parameters.AddWithValue("$email", normalizedEmail);
            insertCommand.Parameters.AddWithValue("$username", username);
            insertCommand.Parameters.AddWithValue("$password_hash", _passwordHasher.Hash(password));
            insertCommand.Parameters.AddWithValue("$now", now);
            await insertCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return userId;
    }

    public async Task SeedDemoAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM users)
                    + (SELECT COUNT(*) FROM projects)
                    + (SELECT COUNT(*) FROM bug_tickets)
                    + (SELECT COUNT(*) FROM user_requests)
                    + (SELECT COUNT(*) FROM project_access_requests)
                    + (SELECT COUNT(*) FROM auth_tokens)
                    + (SELECT COUNT(*) FROM notifications)
                    + (SELECT COUNT(*) FROM outbox_messages)
                    + (SELECT COUNT(*) FROM audit_logs);
                """;
            if (Convert.ToInt64(await countCommand.ExecuteScalarAsync(ct)) != 0)
            {
                throw new InvalidOperationException("Demo seeding requires an empty business/runtime database.");
            }
        }

        var generation = await DemoFixtureStore.ReadNextGenerationAsync(connection, transaction, ct);
        var now = _timeProvider.GetUtcNow();
        await DemoFixtureStore.InsertAsync(connection, transaction, _passwordHasher, generation, now, ct);
        await DemoFixtureStore.UpdateResetStateAsync(connection, transaction, generation, now, "seed-demo", ct);
        await DemoFixtureStore.ValidateAsync(connection, transaction, generation, ct);
        await transaction.CommitAsync(ct);
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 254 || !normalized.Contains('@') || normalized.StartsWith('@') || normalized.EndsWith('@'))
        {
            throw new ArgumentException("A valid administrator email is required.", nameof(email));
        }

        return normalized;
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 6
            || !password.Any(char.IsDigit)
            || !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            throw new ArgumentException(
                "Password must be at least 6 characters and include a number and special character.",
                nameof(password));
        }
    }
}
