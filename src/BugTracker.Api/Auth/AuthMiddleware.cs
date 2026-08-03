using BugTracker.Api.Audit;

namespace BugTracker.Api.Auth;

public sealed class AuthMiddleware : IMiddleware
{
    public const string AuthContextKey = "auth_user";
    private const string AgentNotificationWebSocketPath = "/api/agent/notifications/ws";
    private static readonly string[] AnonymousApiPaths =
    [
        "/api/auth/login",
        "/api/auth/agent/login",
        "/api/auth/request-access",
        "/api/auth/request-credential-recovery",
        "/api/auth/setup-password",
        "/api/demo/config"
    ];

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            AnonymousApiPaths.Any(path => context.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await TryLogWebSocketAuthFailureAsync(context, null, "missing_bearer", "missing bearer token");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var rawToken = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            await TryLogWebSocketAuthFailureAsync(context, null, "empty_bearer", "empty bearer token");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var tokenService = context.RequestServices.GetRequiredService<TokenService>();
        var repository = context.RequestServices.GetRequiredService<AuthRepository>();
        var tokenHash = tokenService.HashToken(rawToken);
        var principal = await repository.GetAuthenticatedUserByTokenHashAsync(tokenHash, context.RequestAborted);
        if (principal is null)
        {
            await TryLogWebSocketAuthFailureAsync(context, tokenHash, "invalid_token", "invalid bearer token");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[AuthContextKey] = principal;
        try
        {
            await repository.UpdateLastSeenAsync(principal.UserId, DateTimeOffset.UtcNow, context.RequestAborted);
        }
        catch
        {
            // Authentication should not fail solely because presence tracking is temporarily unavailable.
        }

        await next(context);
    }

    private static async Task TryLogWebSocketAuthFailureAsync(HttpContext context, string? tokenHash, string fallbackReason, string fallbackMessage)
    {
        if (!context.Request.Path.Equals(AgentNotificationWebSocketPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var repository = context.RequestServices.GetRequiredService<AuthRepository>();
        AuthAuditUserRecord? actor = null;
        var reason = fallbackReason;
        var message = fallbackMessage;
        object? metadata = null;

        if (!string.IsNullOrWhiteSpace(tokenHash))
        {
            var tokenRecord = await repository.GetAuthTokenAuditRecordAsync(tokenHash, context.RequestAborted);
            if (tokenRecord is not null)
            {
                actor = new AuthAuditUserRecord(tokenRecord.UserId, tokenRecord.UserType);
                if (tokenRecord.IsActive != 1)
                {
                    reason = "inactive_user";
                    message = "user is inactive";
                }
                else if (!string.IsNullOrWhiteSpace(tokenRecord.RevokedAt))
                {
                    reason = "revoked_token";
                    message = "bearer token is revoked";
                }
                else if (tokenRecord.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    reason = "expired_token";
                    message = "bearer token is expired";
                }

                metadata = new { reason, tokenExpiresAt = tokenRecord.ExpiresAt, tokenRecord.RevokedAt };
            }
        }

        if (actor is null)
        {
            return;
        }

        var auditLogger = context.RequestServices.GetService<AuditLogger>();
        if (auditLogger is null)
        {
            return;
        }

        try
        {
            await auditLogger.LogAsync(
                actor.UserId,
                actor.UserType,
                "agent_ws_auth_failed",
                $"Agent notification WebSocket authentication failed: {message}.",
                null,
                metadata ?? new { reason },
                context.RequestAborted);
        }
        catch
        {
            // Authentication should not fail solely because audit logging failed.
        }
    }

}
