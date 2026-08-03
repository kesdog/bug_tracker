using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    private static async Task<IReadOnlyList<TicketAttachmentDto>> ListTicketAttachmentsAsync(SqliteConnection connection, string bugId, CancellationToken ct)
    {
        const string sql = """
            SELECT
                attachment_id,
                ticket_id,
                purpose,
                file_name,
                content_type,
                kind,
                size_bytes,
                width,
                height,
                sha256,
                uploaded_by_user_id,
                created_at
            FROM ticket_attachments
            WHERE ticket_id = $ticket_id
            ORDER BY created_at ASC, rowid ASC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticket_id", bugId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<TicketAttachmentDto>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapTicketAttachment(reader));
        }

        return results;
    }

    private static TicketAttachmentDto MapTicketAttachment(SqliteDataReader reader)
    {
        return new TicketAttachmentDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11));
    }

    private static async Task<IReadOnlyList<BugActivityDto>> ListTicketActivityAsync(SqliteConnection connection, string bugId, CancellationToken ct)
    {
        const string sql = """
            SELECT a.activity_id, a.ticket_id, a.actor_user_id, a.actor_type, a.kind, a.body, a.created_at,
                   a.event_id, a.ticket_version, a.changed_fields_json,
                   actor.username, actor.role, actor.user_type, actor.email,
                   a.subject_user_id, subject.username, subject.role, subject.user_type, subject.email
            FROM ticket_activity a
            LEFT JOIN users actor ON actor.user_id = a.actor_user_id
            LEFT JOIN users subject ON subject.user_id = a.subject_user_id
            WHERE a.ticket_id = $ticket_id
            ORDER BY a.created_at DESC, a.activity_id DESC;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticket_id", bugId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<BugActivityDto>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new BugActivityDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? [] : JsonSerializer.Deserialize<string[]>(reader.GetString(9), JsonOptions) ?? [],
                reader.IsDBNull(10) ? null : new UserIdentityDto(reader.GetString(2), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13)),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(14) || reader.IsDBNull(15) ? null : new UserIdentityDto(reader.GetString(14), reader.GetString(15), reader.GetString(16), reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetString(18))));
        }

        return results;
    }

    private static async Task<BugActivityDto> LogActivityAsync(
        SqliteConnection connection,
        string bugId,
        string actorUserId,
        string actorType,
        string kind,
        string body,
        string createdAt,
        CancellationToken ct,
        SqliteTransaction? transaction = null)
    {
        var activity = new BugActivityDto(
            Guid.NewGuid().ToString("N"),
            bugId,
            actorUserId,
            actorType,
            kind,
            body,
            createdAt);

        const string sql = """
            INSERT INTO ticket_activity (activity_id, ticket_id, actor_user_id, actor_type, kind, body, created_at)
            VALUES ($activity_id, $ticket_id, $actor_user_id, $actor_type, $kind, $body, $created_at);
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$activity_id", activity.Id);
        command.Parameters.AddWithValue("$ticket_id", activity.TicketId);
        command.Parameters.AddWithValue("$actor_user_id", activity.ActorUserId);
        command.Parameters.AddWithValue("$actor_type", activity.ActorType);
        command.Parameters.AddWithValue("$kind", activity.Kind);
        command.Parameters.AddWithValue("$body", activity.Body);
        command.Parameters.AddWithValue("$created_at", activity.CreatedAt);
        await command.ExecuteNonQueryAsync(ct);

        return activity;
    }

    /// <summary>
    /// Executes write operations with bounded retries for SQLITE_BUSY/SQLITE_LOCKED scenarios.
    /// </summary>
    private async Task<T> ExecuteWriteWithRetryAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        for (var attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var connection = await OpenConnectionAsync(readOnly: false, ct);
                return await operation(connection, ct);
            }
            catch (SqliteException ex) when (SqliteResilience.IsUnreachable(ex))
            {
                throw new BugDataAccessException(
                    BugDataAccessError.Unreachable,
                    "Database is unreachable. Check DB path, file access, and storage health.",
                    ex,
                    attempt);
            }
            catch (SqliteException ex) when (SqliteResilience.IsBusy(ex))
            {
                var elapsed = DateTimeOffset.UtcNow - startedAt;
                if (attempt >= MaxWriteAttempts || elapsed >= MaxWriteRetryWindow)
                {
                    throw new BugDataAccessException(
                        BugDataAccessError.BusyConcurrency,
                        "Database is busy due to concurrent writes. Please retry shortly.",
                        ex,
                        attempt);
                }

                var retryDelay = SqliteResilience.GetRetryDelay(attempt);
                var remaining = MaxWriteRetryWindow - elapsed;
                if (retryDelay > remaining)
                {
                    retryDelay = remaining;
                }

                if (retryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelay, ct);
                }
            }
        }

        throw new BugDataAccessException(BugDataAccessError.BusyConcurrency, "Database is busy due to concurrent writes.");
    }

    private async Task<SqliteConnection> OpenConnectionAsync(bool readOnly, CancellationToken ct)
    {
        return await _connectionFactory.OpenConnectionAsync(readOnly, ct);
    }

    /// <summary>
    /// Generates a readable bug id using timestamp + reporter + title slug.
    /// </summary>
    private static string BuildBaseId(DateTimeOffset now, string reporterUserId, string issueTitle)
    {
        var timestamp = now.UtcDateTime.ToString("yyyyMMddHHmmss");
        var reporterPart = SanitizeForSlug(reporterUserId, 12);
        var titlePart = SanitizeForSlug(issueTitle, 24);
        return $"{timestamp}-{reporterPart}-{titlePart}";
    }

    /// <summary>
    /// Normalizes text into a safe lowercase slug with max length.
    /// </summary>
    private static string SanitizeForSlug(string input, int maxLength)
    {
        var normalized = input.Trim().ToLowerInvariant();
        var slug = NonSlugChars.Replace(normalized, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "na";
        }

        return slug.Length <= maxLength ? slug : slug[..maxLength];
    }
}
