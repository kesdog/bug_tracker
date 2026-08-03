using System.Text.Json;
using BugTracker.Api.Database;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Notifications;

public sealed class OutboxDispatcher(
    SqliteConnectionFactory connectionFactory,
    IAgentNotificationPublisher notificationPublisher,
    AuditFilePublisher auditFilePublisher,
    ILogger<OutboxDispatcher> logger,
    OutboxDispatchGate? dispatchGate = null,
    IHostEnvironment? environment = null) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
    private readonly string _dispatcherId = Guid.NewGuid().ToString("N");
    private readonly OutboxDispatchGate _dispatchGate = dispatchGate ?? new OutboxDispatchGate();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (dispatched == 0) await Task.Delay(250, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch failed; committed mutations remain durable and will be retried.");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        using var dispatchLease = await _dispatchGate.EnterAsync(ct);
        var items = await ClaimBatchAsync(ct);
        foreach (var item in items)
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
            try
            {
                if (item.Type == "notification.websocket")
                {
                    var notification = JsonSerializer.Deserialize<NotificationDto>(item.Payload, JsonOptions)
                        ?? throw new JsonException("Invalid notification outbox payload.");
                    if (!await CheckDeliveryEligibilityAsync(connection, notification, ct))
                    {
                        await MarkProcessedAsync(connection, item.Id, ct);
                        continue;
                    }

                    using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    sendCts.CancelAfter(SendTimeout);
                    await notificationPublisher.SendNotificationAsync(notification, sendCts.Token);
                    await MarkProcessedAsync(connection, item.Id, ct);
                }
                else if (item.Type == "audit.jsonl")
                {
                    await auditFilePublisher.AppendAsync(item.Payload, ct);
                    await MarkProcessedAsync(connection, item.Id, ct);
                }
                else
                {
                    await MarkProcessedAsync(connection, item.Id, ct);
                }
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Outbox WebSocket send {OutboxId} exceeded the bounded send timeout and will be retried.", item.Id);
                await MarkFailedAsync(connection, item.Id, "WebSocket send timed out.", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Outbox message {OutboxId} failed and will be retried.", item.Id);
                await MarkFailedAsync(connection, item.Id, ex.Message, ct);
            }
        }

        await PruneProcessedAsync(ct);
        return items.Count;
    }

    private async Task PruneProcessedAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM outbox_messages
            WHERE outbox_id IN (
                SELECT outbox_id
                FROM outbox_messages
                WHERE processed_at IS NOT NULL
                  AND processed_at <= datetime('now', '-1 hour')
                ORDER BY processed_at
                LIMIT 100
            );
            """;
        await command.ExecuteNonQueryAsync(ct);

        if (environment?.IsEnvironment("Demo") == true)
        {
            await using var auditCommand = connection.CreateCommand();
            auditCommand.CommandText = """
                DELETE FROM audit_logs
                WHERE audit_id IN (
                    SELECT audit_id
                    FROM audit_logs
                    ORDER BY audit_id DESC
                    LIMIT -1 OFFSET 20000
                );
                """;
            await auditCommand.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<bool> CheckDeliveryEligibilityAsync(
        SqliteConnection connection,
        NotificationDto notification,
        CancellationToken ct)
    {
        // Take a short writer turn so the final read-state, ticket version and assignee snapshot is
        // ordered against concurrent ticket writes. Commit before any socket establishment/network I/O.
        await using (var begin = connection.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync(ct);
        }

        try
        {
            var eligible = await RecipientStillHasAccessAsync(connection, notification, ct);
            await using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT;";
            await commit.ExecuteNonQueryAsync(ct);
            return eligible;
        }
        catch
        {
            await using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            await rollback.ExecuteNonQueryAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<List<(string Id, string Type, string Payload)>> ClaimBatchAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: false, ct);
        await using (var begin = connection.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync(ct);
        }

        try
        {
            var items = new List<(string Id, string Type, string Payload)>();
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = """
                    SELECT outbox_id, event_type, payload_json
                    FROM outbox_messages
                    WHERE processed_at IS NULL AND available_at <= datetime('now')
                      AND (claimed_until IS NULL OR claimed_until <= datetime('now'))
                    ORDER BY created_at, outbox_id LIMIT 25;
                    """;
                await using var reader = await select.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) items.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            foreach (var item in items)
            {
                await using var claim = connection.CreateCommand();
                claim.CommandText = """
                    UPDATE outbox_messages
                    SET claim_owner = $owner, claimed_until = datetime('now', '+5 minutes')
                    WHERE outbox_id = $id AND processed_at IS NULL
                      AND (claimed_until IS NULL OR claimed_until <= datetime('now'));
                    """;
                claim.Parameters.AddWithValue("$owner", _dispatcherId);
                claim.Parameters.AddWithValue("$id", item.Id);
                await claim.ExecuteNonQueryAsync(ct);
            }

            await using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT;";
            await commit.ExecuteNonQueryAsync(ct);
            return items;
        }
        catch
        {
            await using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            await rollback.ExecuteNonQueryAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<bool> RecipientStillHasAccessAsync(SqliteConnection connection, NotificationDto notification, CancellationToken ct)
    {
        if (notification.TicketId is null)
        {
            await using var standalone = connection.CreateCommand();
            standalone.CommandText = "SELECT 1 FROM notifications WHERE notification_id = $id AND user_id = $user AND is_read = 0;";
            standalone.Parameters.AddWithValue("$id", notification.Id);
            standalone.Parameters.AddWithValue("$user", notification.UserId);
            return await standalone.ExecuteScalarAsync(ct) is not null;
        }
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM notifications n
            JOIN bug_tickets b ON b.id = n.ticket_id
            JOIN projects p ON p.project_id = b.project_id
            JOIN users u ON u.user_id = $user_id AND u.is_active = 1
            WHERE n.notification_id = $notification_id AND n.user_id = $user_id AND n.is_read = 0
              AND b.id = $ticket_id
              AND (n.ticket_version IS NULL OR b.version = n.ticket_version)
              AND (n.kind <> 'ticket_assigned' OR b.assignee_user_id = n.user_id)
              AND (
                u.role = 'admin'
                OR EXISTS (SELECT 1 FROM project_allocations pa WHERE pa.project_id = b.project_id AND pa.user_id = u.user_id)
                OR (u.role = 'senior' AND p.visibility = 'normal')
                OR (p.visibility = 'normal' AND (b.reporter_user_id = u.user_id OR b.assignee_user_id = u.user_id))
              )
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$user_id", notification.UserId);
        command.Parameters.AddWithValue("$ticket_id", notification.TicketId);
        command.Parameters.AddWithValue("$notification_id", notification.Id);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private async Task MarkProcessedAsync(SqliteConnection connection, string id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE outbox_messages SET processed_at = datetime('now'), attempts = attempts + 1, last_error = NULL, claim_owner = NULL, claimed_until = NULL WHERE outbox_id = $id AND claim_owner = $owner;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$owner", _dispatcherId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkFailedAsync(SqliteConnection connection, string id, string error, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE outbox_messages
            SET attempts = attempts + 1,
                last_error = $error,
                available_at = datetime('now', '+' || min(60, attempts + 1) || ' seconds'),
                claim_owner = NULL,
                claimed_until = NULL
            WHERE outbox_id = $id AND claim_owner = $owner;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$owner", _dispatcherId);
        command.Parameters.AddWithValue("$error", error.Length <= 1000 ? error : error[..1000]);
        await command.ExecuteNonQueryAsync(ct);
    }
}

public class AuditFilePublisher(string logDirectory)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task AppendAsync(string payloadJson, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var actorType = document.RootElement.TryGetProperty("actorType", out var value) ? value.GetString() : "human";
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(logDirectory, actorType == "agent" ? "agent-activity.jsonl" : "human-activity.jsonl");
        await _lock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(path, payloadJson + Environment.NewLine, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public virtual async Task ClearAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            foreach (var fileName in new[] { "agent-activity.jsonl", "human-activity.jsonl" })
            {
                var path = Path.Combine(logDirectory, fileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
