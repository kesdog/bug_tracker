using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    private async Task<T> ExecuteAtomicWriteWithRetryAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        return await ExecuteWriteWithRetryAsync(async (connection, token) =>
        {
            await using var begin = connection.CreateCommand();
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync(token);
            try
            {
                var result = await operation(connection, token);
                await using var commit = connection.CreateCommand();
                commit.CommandText = "COMMIT;";
                await commit.ExecuteNonQueryAsync(token);
                return result;
            }
            catch
            {
                try
                {
                    await using var rollback = connection.CreateCommand();
                    rollback.CommandText = "ROLLBACK;";
                    await rollback.ExecuteNonQueryAsync(CancellationToken.None);
                }
                catch (SqliteException)
                {
                    // Preserve the original failure.
                }
                throw;
            }
        }, ct);
    }

    private static async Task<TicketVersionConflict?> GetVersionConflictAsync(
        SqliteConnection connection,
        string ticketId,
        int? expectedVersion,
        CancellationToken ct)
    {
        if (expectedVersion is null)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, status FROM bug_tickets WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", ticketId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetInt32(0) == expectedVersion.Value)
        {
            return null;
        }

        var currentVersion = reader.GetInt32(0);
        var currentStatus = reader.GetString(1);
        await reader.DisposeAsync();

        var changedFields = new HashSet<string>(StringComparer.Ordinal);
        await using var changes = connection.CreateCommand();
        changes.CommandText = """
            SELECT changed_fields_json
            FROM ticket_activity
            WHERE ticket_id = $ticket_id AND ticket_version > $expected_version
            ORDER BY ticket_version;
            """;
        changes.Parameters.AddWithValue("$ticket_id", ticketId);
        changes.Parameters.AddWithValue("$expected_version", expectedVersion.Value);
        await using var changesReader = await changes.ExecuteReaderAsync(ct);
        while (await changesReader.ReadAsync(ct))
        {
            if (changesReader.IsDBNull(0)) continue;
            try
            {
                foreach (var field in JsonSerializer.Deserialize<string[]>(changesReader.GetString(0), JsonOptions) ?? [])
                {
                    changedFields.Add(field);
                }
            }
            catch (JsonException)
            {
                changedFields.Add("unknown");
            }
        }

        if (changedFields.Count == 0) changedFields.Add("unknown");
        return new TicketVersionConflict(ticketId, expectedVersion.Value, currentVersion, currentStatus, changedFields.Order().ToArray());
    }

    private static async Task<int> GetTicketVersionAsync(SqliteConnection connection, string ticketId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM bug_tickets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", ticketId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<string> RecordMutationSideEffectsAsync(
        SqliteConnection connection,
        string ticketId,
        int ticketVersion,
        string actorUserId,
        string actorType,
        string activityKind,
        string activityBody,
        string auditAction,
        IReadOnlyList<string> changedFields,
        string notificationKind,
        string notificationMessage,
        string createdAt,
        CancellationToken ct,
        IReadOnlyList<string>? explicitRecipients = null,
        string? suppliedActivityId = null,
        SqliteTransaction? transaction = null,
        string? subjectUserId = null)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var activityId = suppliedActivityId ?? Guid.NewGuid().ToString("N");
        var changedFieldsJson = JsonSerializer.Serialize(changedFields, JsonOptions);

        await using (var activity = connection.CreateCommand())
        {
            activity.Transaction = transaction;
            activity.CommandText = """
                INSERT INTO ticket_activity
                    (activity_id, ticket_id, actor_user_id, actor_type, kind, body, created_at, event_id, ticket_version, changed_fields_json, subject_user_id)
                VALUES ($id, $ticket, $actor, $actor_type, $kind, $body, $created, $event, $version, $changed, $subject);
                """;
            activity.Parameters.AddWithValue("$id", activityId);
            activity.Parameters.AddWithValue("$ticket", ticketId);
            activity.Parameters.AddWithValue("$actor", actorUserId);
            activity.Parameters.AddWithValue("$actor_type", actorType);
            activity.Parameters.AddWithValue("$kind", activityKind);
            activity.Parameters.AddWithValue("$body", activityBody);
            activity.Parameters.AddWithValue("$created", createdAt);
            activity.Parameters.AddWithValue("$event", eventId);
            activity.Parameters.AddWithValue("$version", ticketVersion);
            activity.Parameters.AddWithValue("$changed", changedFieldsJson);
            activity.Parameters.AddWithValue("$subject", (object?)subjectUserId ?? DBNull.Value);
            await activity.ExecuteNonQueryAsync(ct);
        }

        long auditId;
        var normalizedActorType = actorType == "agent" ? "agent" : "human";
        var metadataJson = JsonSerializer.Serialize(new { eventId, ticketVersion, changedFields }, JsonOptions);
        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO audit_logs
                    (ticket_id, actor_user_id, actor_type, action, message, metadata_json, created_at, event_id, ticket_version)
                VALUES ($ticket, $actor, $actor_type, $action, $message, $metadata, $created, $event, $version)
                RETURNING audit_id;
                """;
            audit.Parameters.AddWithValue("$ticket", ticketId);
            audit.Parameters.AddWithValue("$actor", actorUserId);
            audit.Parameters.AddWithValue("$actor_type", normalizedActorType);
            audit.Parameters.AddWithValue("$action", auditAction);
            audit.Parameters.AddWithValue("$message", activityBody);
            audit.Parameters.AddWithValue("$metadata", metadataJson);
            audit.Parameters.AddWithValue("$created", createdAt);
            audit.Parameters.AddWithValue("$event", eventId);
            audit.Parameters.AddWithValue("$version", ticketVersion);
            auditId = (long)(await audit.ExecuteScalarAsync(ct) ?? 0L);
        }

        var auditPayload = JsonSerializer.Serialize(new
        {
            auditId,
            ticketId,
            actorUserId,
            actorType = normalizedActorType,
            action = auditAction,
            message = activityBody,
            metadataJson,
            createdAt,
            eventId,
            duplicateIdentity = eventId,
            ticketVersion
        }, JsonOptions);
        await InsertOutboxAsync(connection, eventId, "audit.jsonl", ticketId, ticketVersion, auditPayload, createdAt, ct, transaction);

        var recipients = explicitRecipients is null
            ? await GetParticipantRecipientsAsync(connection, ticketId, actorUserId, ct, transaction)
            : explicitRecipients.Where(x => x != actorUserId).Distinct(StringComparer.Ordinal).ToArray();

        foreach (var recipient in recipients)
        {
            var notificationId = Guid.NewGuid().ToString("N");
            await using (var notification = connection.CreateCommand())
            {
                notification.Transaction = transaction;
                notification.CommandText = """
                    INSERT INTO notifications
                        (notification_id, user_id, ticket_id, kind, message, is_read, created_at, event_id, ticket_version)
                    VALUES ($id, $user, $ticket, $kind, $message, 0, $created, $event, $version);
                    """;
                notification.Parameters.AddWithValue("$id", notificationId);
                notification.Parameters.AddWithValue("$user", recipient);
                notification.Parameters.AddWithValue("$ticket", ticketId);
                notification.Parameters.AddWithValue("$kind", notificationKind);
                notification.Parameters.AddWithValue("$message", notificationMessage);
                notification.Parameters.AddWithValue("$created", createdAt);
                notification.Parameters.AddWithValue("$event", eventId);
                notification.Parameters.AddWithValue("$version", ticketVersion);
                await notification.ExecuteNonQueryAsync(ct);
            }

            var payload = JsonSerializer.Serialize(new Notifications.NotificationDto(
                notificationId, recipient, ticketId, notificationKind, notificationMessage, false, createdAt, eventId, ticketVersion), JsonOptions);
            await InsertOutboxAsync(connection, eventId, "notification.websocket", ticketId, ticketVersion, payload, createdAt, ct, transaction);
        }
        return activityId;
    }

    private static async Task<string[]> GetParticipantRecipientsAsync(SqliteConnection connection, string ticketId, string actorUserId, CancellationToken ct, SqliteTransaction? transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT reporter_user_id, assignee_user_id FROM bug_tickets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", ticketId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return [];
        return new[] { reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1) }
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != actorUserId)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task InsertOutboxAsync(SqliteConnection connection, string eventId, string type, string ticketId, int version, string payload, string createdAt, CancellationToken ct, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO outbox_messages
                (outbox_id, event_id, event_type, aggregate_id, ticket_version, payload_json, created_at, available_at)
            VALUES ($id, $event, $type, $aggregate, $version, $payload, $created, $created);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$event", eventId);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$aggregate", ticketId);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$created", createdAt);
        await command.ExecuteNonQueryAsync(ct);
    }
}
