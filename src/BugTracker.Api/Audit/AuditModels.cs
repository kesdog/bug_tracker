namespace BugTracker.Api.Audit;

public sealed record AuditLogEntryDto(
    long AuditId,
    string? TicketId,
    string ActorUserId,
    string ActorType,
    string Action,
    string Message,
    string? MetadataJson,
    string CreatedAt);

public sealed record AuditLogFilter(
    string? ActorType,
    string? Search,
    string? TicketId,
    string? Action,
    int Limit);
