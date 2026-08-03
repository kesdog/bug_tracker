using Microsoft.Data.Sqlite;
using BugTracker.Api.Database;
using System.Text.Json;

namespace BugTracker.Api.Notifications;

public sealed class NotificationRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public NotificationRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<NotificationDto> CreateAsync(string userId, string? ticketId, string kind, string message, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var eventId = Guid.NewGuid().ToString("N");
        var notification = new NotificationDto(
            Guid.NewGuid().ToString("N"),
            userId,
            ticketId,
            kind,
            message,
            false,
            now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            eventId);

        const string sql = """
            INSERT INTO notifications (notification_id, user_id, ticket_id, kind, message, is_read, created_at, event_id)
            VALUES ($notification_id, $user_id, $ticket_id, $kind, $message, 0, $created_at, $event_id);
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$notification_id", notification.Id);
        command.Parameters.AddWithValue("$user_id", notification.UserId);
        command.Parameters.AddWithValue("$ticket_id", (object?)notification.TicketId ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", notification.Kind);
        command.Parameters.AddWithValue("$message", notification.Message);
        command.Parameters.AddWithValue("$created_at", notification.CreatedAt);
        command.Parameters.AddWithValue("$event_id", eventId);
        await command.ExecuteNonQueryAsync(ct);

        await using var outbox = connection.CreateCommand();
        outbox.Transaction = transaction;
        outbox.CommandText = """
            INSERT INTO outbox_messages
                (outbox_id, event_id, event_type, aggregate_id, payload_json, created_at, available_at)
            VALUES ($id, $event, 'notification.websocket', $aggregate, $payload, $created, $created);
            """;
        outbox.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        outbox.Parameters.AddWithValue("$event", eventId);
        outbox.Parameters.AddWithValue("$aggregate", (object?)ticketId ?? DBNull.Value);
        outbox.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(notification, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        outbox.Parameters.AddWithValue("$created", notification.CreatedAt);
        await outbox.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);

        return notification;
    }

    public async Task<IReadOnlyList<NotificationDto>> ListForUserAsync(string userId, bool unreadOnly, CancellationToken ct, int? limit = 100)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT notification_id, user_id, ticket_id, kind, message, is_read, created_at, event_id, ticket_version
            FROM notifications
            WHERE user_id = $user_id
              {(unreadOnly ? "AND is_read = 0" : string.Empty)}
            ORDER BY created_at DESC, notification_id DESC
            {(limit is null ? string.Empty : "LIMIT $limit")};
            """;
        command.Parameters.AddWithValue("$user_id", userId);
        if (limit is not null)
        {
            command.Parameters.AddWithValue("$limit", limit.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<NotificationDto>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapNotification(reader));
        }

        return results;
    }

    public async Task<int> CountUnreadForUserAsync(string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT COUNT(*)
            FROM notifications
            WHERE user_id = $user_id
              AND is_read = 0;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);

        var scalar = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(scalar);
    }

    public async Task<NotificationDto?> GetForUserAsync(string notificationId, string userId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT notification_id, user_id, ticket_id, kind, message, is_read, created_at, event_id, ticket_version
            FROM notifications
            WHERE notification_id = $notification_id
              AND user_id = $user_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$notification_id", notificationId);
        command.Parameters.AddWithValue("$user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapNotification(reader) : null;
    }

    public async Task<NotificationDto?> MarkReadAsync(string notificationId, string userId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string updateSql = """
            UPDATE notifications
            SET is_read = 1
            WHERE notification_id = $notification_id
              AND user_id = $user_id;
            """;

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.CommandText = updateSql;
            updateCommand.Parameters.AddWithValue("$notification_id", notificationId);
            updateCommand.Parameters.AddWithValue("$user_id", userId);
            var rows = await updateCommand.ExecuteNonQueryAsync(ct);
            if (rows <= 0)
            {
                return null;
            }
        }

        const string selectSql = """
            SELECT notification_id, user_id, ticket_id, kind, message, is_read, created_at, event_id, ticket_version
            FROM notifications
            WHERE notification_id = $notification_id
              AND user_id = $user_id
            LIMIT 1;
            """;

        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = selectSql;
        selectCommand.Parameters.AddWithValue("$notification_id", notificationId);
        selectCommand.Parameters.AddWithValue("$user_id", userId);

        await using var reader = await selectCommand.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapNotification(reader) : null;
    }

    public async Task<int> MarkAllReadAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: false, ct);

        const string sql = """
            UPDATE notifications
            SET is_read = 1
            WHERE user_id = $user_id
              AND is_read = 0;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user_id", userId);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static NotificationDto MapNotification(SqliteDataReader reader)
    {
        return new NotificationDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5) == 1,
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8));
    }

    private async Task<SqliteConnection> OpenConnectionAsync(bool readOnly, CancellationToken ct)
    {
        return await _connectionFactory.OpenConnectionAsync(readOnly, ct);
    }
}
