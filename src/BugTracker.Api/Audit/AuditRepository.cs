using Microsoft.Data.Sqlite;
using BugTracker.Api.Database;
using System.Text.Json;

namespace BugTracker.Api.Audit;

public sealed class AuditRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AuditRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AuditLogEntryDto> CreateAsync(
        string? ticketId,
        string actorUserId,
        string actorType,
        string action,
        string message,
        string? metadataJson,
        DateTimeOffset createdAt,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var createdAtText = createdAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var eventId = Guid.NewGuid().ToString("N");

        const string sql = """
            INSERT INTO audit_logs (ticket_id, actor_user_id, actor_type, action, message, metadata_json, created_at, event_id)
            VALUES ($ticket_id, $actor_user_id, $actor_type, $action, $message, $metadata_json, $created_at, $event_id)
            RETURNING audit_id;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticket_id", (object?)ticketId ?? DBNull.Value);
        command.Parameters.AddWithValue("$actor_user_id", actorUserId);
        command.Parameters.AddWithValue("$actor_type", actorType);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$metadata_json", (object?)metadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", createdAtText);
        command.Parameters.AddWithValue("$event_id", eventId);

        var auditId = (long)(await command.ExecuteScalarAsync(ct) ?? 0L);
        var entry = new AuditLogEntryDto(auditId, ticketId, actorUserId, actorType, action, message, metadataJson, createdAtText);
        var payload = JsonSerializer.Serialize(new
        {
            entry.AuditId,
            entry.TicketId,
            entry.ActorUserId,
            entry.ActorType,
            entry.Action,
            entry.Message,
            entry.MetadataJson,
            entry.CreatedAt,
            eventId,
            duplicateIdentity = eventId
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var outbox = connection.CreateCommand();
        outbox.Transaction = transaction;
        outbox.CommandText = """
            INSERT INTO outbox_messages
                (outbox_id, event_id, event_type, aggregate_id, payload_json, created_at, available_at)
            VALUES ($id, $event, 'audit.jsonl', $aggregate, $payload, $created, $created);
            """;
        outbox.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        outbox.Parameters.AddWithValue("$event", eventId);
        outbox.Parameters.AddWithValue("$aggregate", (object?)ticketId ?? DBNull.Value);
        outbox.Parameters.AddWithValue("$payload", payload);
        outbox.Parameters.AddWithValue("$created", createdAtText);
        await outbox.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<AuditLogEntryDto>> ListAsync(AuditLogFilter filter, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();

        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.ActorType))
        {
            clauses.Add("actor_type = $actor_type");
            command.Parameters.AddWithValue("$actor_type", filter.ActorType);
        }

        if (!string.IsNullOrWhiteSpace(filter.TicketId))
        {
            clauses.Add("ticket_id = $ticket_id");
            command.Parameters.AddWithValue("$ticket_id", filter.TicketId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            clauses.Add("action = $action");
            command.Parameters.AddWithValue("$action", filter.Action);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            clauses.Add("""
                (
                    lower(action) LIKE $search
                    OR lower(COALESCE(ticket_id, '')) LIKE $search
                    OR lower(actor_user_id) LIKE $search
                    OR lower(COALESCE(message, '')) LIKE $search
                )
                """);
            command.Parameters.AddWithValue("$search", $"%{filter.Search}%");
        }

        var whereClause = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
        command.CommandText = $"""
            SELECT audit_id, ticket_id, actor_user_id, COALESCE(actor_type, 'human'), action, COALESCE(message, ''), metadata_json, created_at
            FROM audit_logs
            {whereClause}
            ORDER BY created_at DESC, audit_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", filter.Limit);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<AuditLogEntryDto>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new AuditLogEntryDto(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7)));
        }

        return results;
    }

    public async Task<bool> HasSystemStartEventAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM audit_logs
                WHERE actor_user_id = 'system'
                  AND action IN ('system.started', 'system.restarted')
            );
            """;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 0;
    }

    public async Task EnsureSystemUserAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO users (
                user_id, email, username, password_hash, role, user_type, projects_json, is_active, created_at, updated_at
            ) VALUES (
                'system', 'system@internal.invalid', 'system', 'disabled', 'dev', 'human', '[]', 0, datetime('now'), datetime('now')
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(bool readOnly, CancellationToken ct)
    {
        return await _connectionFactory.OpenConnectionAsync(readOnly, ct);
    }
}
