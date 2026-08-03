using BugTracker.Api.Auth;
using BugTracker.Api.Audit;
using Microsoft.AspNetCore.Mvc;

namespace BugTracker.Api.Notifications;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notifications", ListNotificationsAsync);
        app.MapGet("/api/notifications/unread-count", CountUnreadNotificationsAsync);
        app.MapPatch("/api/notifications/read-all", MarkAllNotificationsReadAsync);
        app.MapPatch("/api/notifications/{id}/read", MarkNotificationReadAsync);
        return app;
    }

    private static async Task<IResult> ListNotificationsAsync(
        HttpContext context,
        NotificationRepository repository,
        NotificationAuthorizationService authorizationService,
        [FromQuery] bool? unreadOnly,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var notifications = await repository.ListForUserAsync(principal.UserId, unreadOnly == true, ct);
        return Results.Ok(await authorizationService.FilterReadableAsync(principal, notifications, ct));
    }

    private static async Task<IResult> CountUnreadNotificationsAsync(
        HttpContext context,
        NotificationRepository repository,
        NotificationAuthorizationService authorizationService,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var notifications = await repository.ListForUserAsync(principal.UserId, unreadOnly: true, ct, limit: null);
        var readable = await authorizationService.FilterReadableAsync(principal, notifications, ct);
        return Results.Ok(new NotificationUnreadCountDto(readable.Count));
    }

    private static async Task<IResult> MarkAllNotificationsReadAsync(
        HttpContext context,
        NotificationRepository repository,
        NotificationAuthorizationService authorizationService,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var notifications = await repository.ListForUserAsync(principal.UserId, unreadOnly: true, ct, limit: null);
        var readable = await authorizationService.FilterReadableAsync(principal, notifications, ct);
        var updated = 0;
        foreach (var notification in readable)
        {
            if (await repository.MarkReadAsync(notification.Id, principal.UserId, DateTimeOffset.UtcNow, ct) is not null)
            {
                updated++;
            }
        }

        return Results.Ok(new MarkNotificationsReadResponse(updated));
    }

    private static async Task<IResult> MarkNotificationReadAsync(
        HttpContext context,
        NotificationRepository repository,
        NotificationAuthorizationService authorizationService,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { error = "id is required" });
        }

        var existing = await repository.GetForUserAsync(id.Trim(), principal.UserId, ct);
        if (existing is null || !await authorizationService.CanReadAsync(principal, existing, ct))
        {
            return Results.NotFound(new { error = "notification not found" });
        }

        var notification = await repository.MarkReadAsync(id.Trim(), principal.UserId, DateTimeOffset.UtcNow, ct);
        return notification is null
            ? Results.NotFound(new { error = "notification not found" })
            : Results.Ok(notification);
    }

    private static async Task<IResult> ConnectAgentNotificationsAsync(
        HttpContext context,
        NotificationRepository repository,
        NotificationAuthorizationService authorizationService,
        AuthRepository authRepository,
        AgentNotificationSocketHub socketHub,
        AuditLogger auditLogger,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!string.Equals(principal.UserType, "agent", StringComparison.Ordinal))
        {
            await auditLogger.LogAsync(
                principal,
                "agent_ws_rejected",
                "Agent notification WebSocket rejected: authenticated user is not an AI agent.",
                null,
                new { reason = "not_agent", principal.UserType },
                ct);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            await auditLogger.LogAsync(
                principal,
                "agent_ws_rejected",
                "Agent notification WebSocket rejected: websocket upgrade is required.",
                null,
                new { reason = "missing_websocket_upgrade" },
                ct);
            return Results.BadRequest(new { error = "websocket upgrade is required" });
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await auditLogger.LogAsync(
            principal,
            "agent_ws_connected",
            "AI agent notification WebSocket connected.",
            null,
            new { principal.TokenExpiresAt },
            ct);

        await socketHub.HandleConnectionAsync(principal, socket, async token =>
        {
            var stored = await repository.ListForUserAsync(principal.UserId, unreadOnly: true, token, limit: null);
            return await authorizationService.FilterReadableAsync(principal, stored, token);
        }, token => authRepository.GetAuthenticatedUserByTokenHashAsync(principal.TokenHash, token),
            () => Task.CompletedTask,
            (state, token) => auditLogger.LogAsync(
                principal,
                "agent_ws_disconnected",
                "AI agent notification WebSocket disconnected.",
                null,
                new { State = state, principal.TokenExpiresAt },
                token),
            ct);
        return Results.Empty;
    }

    private static AuthenticatedUser? GetPrincipal(HttpContext context)
    {
        return context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
    }
}
