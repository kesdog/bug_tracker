using BugTracker.Api.Auth;
using System.Text.Json;

namespace BugTracker.Api.Audit;

public sealed class AuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuditRepository _repository;

    public AuditLogger(AuditRepository repository, string logDirectory)
    {
        _repository = repository;
        _ = logDirectory; // JSONL publication is performed asynchronously by the durable outbox.
    }

    public Task<AuditLogEntryDto> LogAsync(
        AuthenticatedUser actor,
        string action,
        string message,
        string? ticketId,
        object? metadata,
        CancellationToken ct)
    {
        return LogAsync(actor.UserId, actor.UserType, action, message, ticketId, metadata, ct);
    }

    public async Task<AuditLogEntryDto> LogAsync(
        string actorUserId,
        string actorType,
        string action,
        string message,
        string? ticketId,
        object? metadata,
        CancellationToken ct)
    {
        var normalizedActorType = actorType is "agent" ? "agent" : "human";
        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions);
        return await _repository.CreateAsync(
            ticketId,
            actorUserId,
            normalizedActorType,
            action,
            message,
            metadataJson,
            DateTimeOffset.UtcNow,
            ct);
    }
}
