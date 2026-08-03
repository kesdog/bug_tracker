namespace BugTracker.Api.Notifications;

public sealed record NotificationDto(
    string Id,
    string UserId,
    string? TicketId,
    string Kind,
    string Message,
    bool IsRead,
    string CreatedAt,
    string? EventId = null,
    int? TicketVersion = null)
{
    public AgentNotificationInstructions? AgentInstructions => TicketId is null
        ? null
        : AgentNotificationInstructions.ForTicket(TicketId, Id);
}

public sealed record AgentNotificationInstructions(
    bool ActionRequired,
    string RequiredWorkflow,
    string TicketDetailPath,
    string CommentPath,
    string MarkNotificationReadPath,
    string CompletionAction,
    string UnableToResolveAction,
    string SafetyNote)
{
    public static AgentNotificationInstructions ForTicket(string ticketId, string notificationId) => new(
        true,
        "Deduplicate eventId, ignore older ticketVersion events, then fetch the latest ticket and inspect its full details/activity. Use that current version as expectedVersion for any mutation. Do not receive and ignore this notification.",
        $"/api/bugs/{ticketId}",
        $"/api/bugs/{ticketId}/comments",
        $"/api/notifications/{notificationId}/read",
        "After you have handled the ticket, resolved it, or left a blocker comment, mark this notification read so it is consumed and will not remain in the unread work queue.",
        "If you cannot resolve or safely progress the ticket, leave a comment explaining what you checked, what blocked you, and what a human should do next.",
        "Comments are the low-risk fallback for AI agents: they do not change ticket state and do not overwrite ticket data.");
}

public sealed record AgentSocketSessionInstructions(
    string RequiredWorkflow,
    string RecoveryWorkflow,
    string CompletionWorkflow,
    string UnableToResolveAction,
    string SafetyNote)
{
    public static AgentSocketSessionInstructions Current { get; } = new(
        "For every ticket notification, deduplicate eventId and ignore events older than the newest ticketVersion already observed; fetch links.ticket (or agentInstructions.ticketDetailPath) before acting and send the latest ticket version as expectedVersion for every aggregate mutation.",
        "After reconnecting, load unread notifications. On HTTP 409, refetch the ticket, resolve and merge concurrent changes, then retry with the current version; never blindly replay stale writes.",
        "Once the ticket has been handled, resolved, or documented with a blocker comment, call PATCH agentInstructions.markNotificationReadPath so the notification is consumed.",
        "If you cannot resolve or safely progress a ticket, POST a comment to /api/bugs/{id}/comments with your findings and blocker instead of silently dropping the notification.",
        "Adding a comment is the low-risk AI fallback because it does not change ticket state and does not overwrite any report or resolution data.");
}

public sealed record NotificationUnreadCountDto(int Count);

public sealed record MarkNotificationsReadResponse(int Updated);
