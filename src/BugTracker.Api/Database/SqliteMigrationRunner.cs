using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Database;

public sealed class SqliteMigrationRunner(SqliteConnectionFactory connectionFactory)
{
    private static readonly Migration[] Migrations =
    [
        LoadMigration(1, "initial_schema", "001_initial.sql"),
        LoadMigration(2, "usernames", "002_usernames.sql"),
        LoadMigration(3, "ticket_concurrency_outbox", "003_ticket_concurrency_outbox.sql"),
        LoadMigration(4, "outbox_leases", "004_outbox_leases.sql"),
        LoadMigration(5, "project_ownership_access", "005_project_ownership_access.sql"),
        LoadMigration(6, "demo_reset_state", "006_demo_reset_state.sql"),
        LoadMigration(7, "demo_reset_cleanup_state", "007_demo_reset_cleanup_state.sql"),
        LoadMigration(8, "outbox_retention", "008_outbox_retention.sql"),
        LoadMigration(9, "credential_recovery_requests", "009_credential_recovery_requests.sql"),
        LoadMigration(10, "ticket_cancellation", "010_ticket_cancellation.sql"),
        LoadMigration(11, "system_lifecycle_audit", "011_system_lifecycle_audit.sql"),
        LoadMigration(12, "first_run_setup", "012_first_run_setup.sql"),
        LoadMigration(13, "login_security_state", "013_login_security_state.sql")
    ];

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);

        await using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await pragmas.ExecuteNonQueryAsync(ct);
        }

        var schemaObjects = await ReadApplicationSchemaObjectsAsync(connection, ct);
        if (schemaObjects.Count > 0 && !schemaObjects.Contains("schema_migrations"))
        {
            throw new InvalidOperationException(
                "Unsupported nonempty unversioned SQLite database. Rebuild the disposable database or migrate it with a supported tool before startup.");
        }

        await using (var createJournal = connection.CreateCommand())
        {
            createJournal.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    checksum TEXT NOT NULL,
                    applied_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
                """;
            await createJournal.ExecuteNonQueryAsync(ct);
        }

        var applied = await ReadAppliedMigrationsAsync(connection, ct);
        foreach (var entry in applied)
        {
            var known = Migrations.SingleOrDefault(migration => migration.Version == entry.Key);
            if (known is null)
            {
                throw new InvalidOperationException($"Database contains unsupported migration version {entry.Key}.");
            }

            if (!string.Equals(known.Checksum, entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Checksum mismatch for SQLite migration {known.Version} ({known.Name}).");
            }
        }

        foreach (var migration in Migrations)
        {
            if (applied.ContainsKey(migration.Version))
            {
                continue;
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync(ct);
            }

            await using (var journal = connection.CreateCommand())
            {
                journal.Transaction = transaction;
                journal.CommandText = """
                    INSERT INTO schema_migrations (version, name, checksum)
                    VALUES ($version, $name, $checksum);
                    """;
                journal.Parameters.AddWithValue("$version", migration.Version);
                journal.Parameters.AddWithValue("$name", migration.Name);
                journal.Parameters.AddWithValue("$checksum", migration.Checksum);
                await journal.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        await AssertDatabaseIntegrityAsync(connection, ct);
    }

    private static async Task<HashSet<string>> ReadApplicationSchemaObjectsAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<Dictionary<int, string>> ReadAppliedMigrationsAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, checksum FROM schema_migrations ORDER BY version;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var applied = new Dictionary<int, string>();
        while (await reader.ReadAsync(ct))
        {
            applied.Add(reader.GetInt32(0), reader.GetString(1));
        }

        return applied;
    }

    private static async Task AssertDatabaseIntegrityAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using (var foreignKeyCheck = connection.CreateCommand())
        {
            foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await foreignKeyCheck.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException(
                    $"SQLite foreign-key check failed for table {reader.GetString(0)}, row {reader.GetValue(1)}.");
            }
        }

        await using var quickCheck = connection.CreateCommand();
        quickCheck.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await quickCheck.ExecuteScalarAsync(ct));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite quick check failed: {result ?? "no result"}.");
        }
    }

    private static Migration LoadMigration(int version, string name, string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(resource => resource.EndsWith($".Migrations.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource {fileName} was not found.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        var checksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        return new Migration(version, name, checksum, sql);
    }

    private sealed record Migration(int Version, string Name, string Checksum, string Sql);
}
