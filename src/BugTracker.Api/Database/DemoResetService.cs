using BugTracker.Api.Auth;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Database;

/// <summary>
/// Explicitly guarded atomic reset operation used only by the internal reset coordinator.
/// </summary>
public sealed class DemoResetService(
    SqliteConnectionFactory connectionFactory,
    PasswordHasherService passwordHasher,
    TimeProvider? timeProvider = null)
{
    private const int CheckpointAttempts = 3;
    private static readonly TimeSpan CheckpointRetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DateTimeOffset?> GetLastResetAtAsync(CancellationToken ct = default)
        => (await GetStateAsync(ct)).LastResetAt;

    public async Task<DemoResetState> GetStateAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT generation, last_reset_at, cleanup_pending,
                   wal_checkpoint_completed, audit_file_cleanup_completed
            FROM demo_reset_state WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("The singleton demo reset state is missing.");
        }

        DateTimeOffset? lastResetAt = reader.IsDBNull(1) ? null : DateTimeOffset.Parse(
            reader.GetString(1),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        return new DemoResetState(reader.GetInt32(0), lastResetAt, reader.GetInt32(2) == 1,
            reader.GetInt32(3) == 1, reader.GetInt32(4) == 1);
    }

    public async Task<DemoResetResult> ResetAsync(
        DemoResetOptions options,
        string environment,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AssertAllowed(environment);

        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        int generation;
        DateTimeOffset resetAt;
        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct))
        {
            await DemoFixtureStore.DeleteBusinessDataChildFirstAsync(connection, transaction, ct);
            generation = await DemoFixtureStore.ReadNextGenerationAsync(connection, transaction, ct);
            resetAt = _timeProvider.GetUtcNow();
            await DemoFixtureStore.InsertAsync(connection, transaction, passwordHasher, generation, resetAt, ct);
            await DemoFixtureStore.UpdateResetStateAsync(connection, transaction, generation, resetAt, environment.Trim(), ct);
            await MarkCleanupPendingAsync(connection, transaction, ct);
            await DemoFixtureStore.ValidateAsync(connection, transaction, generation, ct);
            await transaction.CommitAsync(ct);
        }

        return new DemoResetResult(generation, resetAt, DemoFixtureStore.UserCount, DemoFixtureStore.ProjectCount, DemoFixtureStore.TicketCount);
    }

    public async Task CheckpointWalAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using (var boundBusyWait = connection.CreateCommand())
        {
            // The normal writer timeout is intentionally longer. Cleanup must instead fail fast so
            // reset orchestration can report a reader that prevented WAL truncation.
            boundBusyWait.CommandText = "PRAGMA busy_timeout=50;";
            await boundBusyWait.ExecuteNonQueryAsync(ct);
        }

        for (var attempt = 1; attempt <= CheckpointAttempts; attempt++)
        {
            await using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using var reader = await checkpoint.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.FieldCount < 3)
            {
                throw new InvalidOperationException("SQLite WAL checkpoint did not return its required result row.");
            }

            var busy = reader.GetInt64(0);
            var logFrames = reader.GetInt64(1);
            var checkpointedFrames = reader.GetInt64(2);
            if (busy == 0)
            {
                if (logFrames < 0 || checkpointedFrames < 0 || checkpointedFrames > logFrames)
                {
                    throw new InvalidOperationException("SQLite WAL checkpoint returned an invalid result row.");
                }
                return;
            }
            if (busy != 1)
            {
                throw new InvalidOperationException($"SQLite WAL checkpoint returned invalid busy status {busy}.");
            }
            if (attempt < CheckpointAttempts)
            {
                await Task.Delay(CheckpointRetryDelay, ct);
            }
        }

        throw new InvalidOperationException(
            $"SQLite WAL checkpoint remained busy after {CheckpointAttempts} bounded attempts; cleanup was not completed.");
    }

    public Task MarkWalCheckpointCompletedAsync(CancellationToken ct = default) =>
        MarkCleanupStepCompletedAsync(walCheckpoint: true, ct);

    public Task MarkAuditFileCleanupCompletedAsync(CancellationToken ct = default) =>
        MarkCleanupStepCompletedAsync(walCheckpoint: false, ct);

    public async Task CompleteCleanupAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE demo_reset_state
            SET cleanup_pending = 0
            WHERE singleton_id = 1 AND cleanup_pending = 1
              AND wal_checkpoint_completed = 1 AND audit_file_cleanup_completed = 1;
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkCleanupStepCompletedAsync(bool walCheckpoint, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = walCheckpoint
            ? "UPDATE demo_reset_state SET wal_checkpoint_completed = 1 WHERE singleton_id = 1 AND cleanup_pending = 1;"
            : "UPDATE demo_reset_state SET audit_file_cleanup_completed = 1 WHERE singleton_id = 1 AND cleanup_pending = 1;";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkCleanupPendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE demo_reset_state
            SET cleanup_pending = 1,
                wal_checkpoint_completed = 0,
                audit_file_cleanup_completed = 0
            WHERE singleton_id = 1;
            """;
        await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed record DemoResetState(
    int Generation,
    DateTimeOffset? LastResetAt,
    bool CleanupPending,
    bool WalCheckpointCompleted,
    bool AuditFileCleanupCompleted);
