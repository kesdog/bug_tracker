using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Health;
using System.Text.Json;

namespace BugTracker.Api.Notifications;

public static class AgentNotificationWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task HandleAsync(HttpContext context)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AgentNotificationWebSocket");

        if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, StatusCodes.Status200OK, new
            {
                ok = true,
                endpoint = "/api/agent/notifications/ws",
                auth = "Authorization: Bearer <agent-token>",
                auditIdentity = "resolved bearer tokens only",
                websocketMiddleware = true
            });
            return;
        }

        LogRequestShape(context, logger);

        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await WriteJsonAsync(context, StatusCodes.Status405MethodNotAllowed, new { error = "GET websocket upgrade is required" });
            return;
        }

        var authentication = await AuthenticateAsync(context, logger);
        var principal = authentication?.Principal;
        if (principal is null)
        {
            await WriteJsonAsync(context, StatusCodes.Status401Unauthorized, new { error = "valid bearer token is required" });
            return;
        }

        if (!string.Equals(principal.UserType, "agent", StringComparison.Ordinal))
        {
            await TryAuditAsync(context, principal, "agent_ws_rejected", "Agent notification WebSocket rejected: authenticated user is not an AI agent.", new { reason = "not_agent", principal.UserType }, context.RequestAborted);
            await WriteJsonAsync(context, StatusCodes.Status403Forbidden, new { error = "agent token is required" });
            return;
        }

        if (!(await context.RequestServices.GetRequiredService<FirstRunSetupService>().GetAsync(context.RequestAborted)).IsComplete)
        {
            await WriteJsonAsync(context, StatusCodes.Status503ServiceUnavailable, new { error = "first-run setup is incomplete", errorCode = "setup_incomplete" });
            return;
        }

        {
            var protection = context.RequestServices.GetRequiredService<AuthenticatedAbuseProtection>();
            using var ipLease = protection.Acquire("websocket", $"ip:{context.Connection.RemoteIpAddress}");
            using var userLease = protection.Acquire("websocket", $"user:{principal.UserId}", permitMultiplier: 10);
            if (!ipLease.IsAcquired || !userLease.IsAcquired)
            {
                RateLimitResponses.SetRetryAfter(context.Response, !ipLease.IsAcquired ? ipLease : userLease);
                await WriteJsonAsync(context, StatusCodes.Status429TooManyRequests, new { error = "too many requests", errorCode = "rate_limited" });
                return;
            }
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new { error = "websocket upgrade is required" });
            return;
        }

        try
        {
            var repository = context.RequestServices.GetRequiredService<NotificationRepository>();
            var notificationAuthorization = context.RequestServices.GetRequiredService<NotificationAuthorizationService>();
            var socketHub = context.RequestServices.GetRequiredService<AgentNotificationSocketHub>();
            var maintenanceState = context.RequestServices.GetRequiredService<IResetMaintenanceState>();
            if (maintenanceState.IsResetInProgress)
            {
                await WriteJsonAsync(context, StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "service temporarily unavailable during demo reset",
                    errorCode = "demo_reset_in_progress"
                });
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await TryAuditAsync(context, principal, "agent_ws_connected", "AI agent notification WebSocket connected.", new { principal.TokenExpiresAt }, context.RequestAborted);
            var authRepository = context.RequestServices.GetRequiredService<AuthRepository>();
            await socketHub.HandleConnectionAsync(
                principal,
                socket,
                async token =>
                {
                    var stored = await repository.ListForUserAsync(principal.UserId, unreadOnly: true, token, limit: null);
                    return await notificationAuthorization.FilterReadableAsync(principal, stored, token);
                },
                token => authRepository.GetAuthenticatedUserByTokenHashAsync(authentication!.TokenHash, token),
                () =>
                {
                    context.Features.Get<IWebSocketEstablishmentLease>()?.CompleteEstablishment();
                    return Task.CompletedTask;
                },
                (state, token) => TryAuditAsync(
                    context,
                    principal,
                    "agent_ws_disconnected",
                    "AI agent notification WebSocket disconnected.",
                    new { State = state, principal.TokenExpiresAt },
                    token),
                context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Agent WebSocket request cancelled for {UserId}", principal.UserId);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Agent WebSocket failed for {UserId}", principal.UserId);
            if (!context.Response.HasStarted)
            {
                await WriteJsonAsync(context, StatusCodes.Status500InternalServerError, new { error = "websocket connection failed" });
            }
        }
    }

    private static async Task<WebSocketAuthentication?> AuthenticateAsync(HttpContext context, ILogger logger)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await TryLogAuthFailureAsync(context, null, "missing_bearer", "missing bearer token");
            logger.LogWarning("Agent WebSocket rejected: missing bearer token");
            return null;
        }

        var rawToken = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            await TryLogAuthFailureAsync(context, null, "empty_bearer", "empty bearer token");
            logger.LogWarning("Agent WebSocket rejected: empty bearer token");
            return null;
        }

        var tokenService = context.RequestServices.GetRequiredService<TokenService>();
        var authRepository = context.RequestServices.GetRequiredService<AuthRepository>();
        var tokenHash = tokenService.HashToken(rawToken);
        var principal = await authRepository.GetAuthenticatedUserByTokenHashAsync(tokenHash, context.RequestAborted);

        if (principal is null)
        {
            var tokenRecord = await authRepository.GetAuthTokenAuditRecordAsync(tokenHash, context.RequestAborted);
            var reason = tokenRecord switch
            {
                { RevokedAt: not null } => "revoked_token",
                { IsActive: not 1 } => "inactive_user",
                { ExpiresAt: var expiresAt } when expiresAt <= DateTimeOffset.UtcNow => "expired_token",
                _ => "invalid_token"
            };
            await TryLogAuthFailureAsync(context, tokenHash, reason, reason.Replace('_', ' '));
            logger.LogWarning("Agent WebSocket rejected: {Reason} for hint {Hint}", reason, GetWebSocketUserHint(context) ?? "none");
            return null;
        }

        try
        {
            await authRepository.UpdateLastSeenAsync(principal.UserId, DateTimeOffset.UtcNow, context.RequestAborted);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Could not update last-seen for WebSocket user {UserId}", principal.UserId);
        }

        return new WebSocketAuthentication(principal, tokenHash);
    }

    private static void LogRequestShape(HttpContext context, ILogger logger)
    {
        logger.LogInformation(
            "Agent WebSocket request {Method} {Path}{Query}; isWs={IsWebSocket}; upgrade={Upgrade}; connection={Connection}; secKey={HasKey}; auth={HasAuth}; userHint={UserHint}",
            context.Request.Method,
            context.Request.PathBase + context.Request.Path,
            context.Request.QueryString,
            context.WebSockets.IsWebSocketRequest,
            context.Request.Headers.Upgrade.ToString(),
            context.Request.Headers.Connection.ToString(),
            context.Request.Headers.ContainsKey("Sec-WebSocket-Key"),
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase),
            GetWebSocketUserHint(context) ?? "none");
    }

    private static async Task TryLogAuthFailureAsync(HttpContext context, string? tokenHash, string reason, string message)
    {
        var authRepository = context.RequestServices.GetRequiredService<AuthRepository>();
        AuthAuditUserRecord? actor = null;
        object? metadata = null;

        if (!string.IsNullOrWhiteSpace(tokenHash))
        {
            var tokenRecord = await authRepository.GetAuthTokenAuditRecordAsync(tokenHash, context.RequestAborted);
            if (tokenRecord is not null)
            {
                actor = new AuthAuditUserRecord(tokenRecord.UserId, tokenRecord.UserType);
                metadata = new { reason, tokenRecord.ExpiresAt, tokenRecord.RevokedAt };
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
            await auditLogger.LogAsync(actor.UserId, actor.UserType, "agent_ws_auth_failed", $"Agent notification WebSocket authentication failed: {message}.", null, metadata ?? new { reason }, context.RequestAborted);
        }
        catch
        {
            // Authentication should not fail because audit logging failed.
        }
    }

    private static async Task TryAuditAsync(HttpContext context, AuthenticatedUser principal, string action, string message, object? metadata, CancellationToken ct)
    {
        var auditLogger = context.RequestServices.GetService<AuditLogger>();
        if (auditLogger is null)
        {
            return;
        }

        try
        {
            await auditLogger.LogAsync(principal, action, message, null, metadata, ct);
        }
        catch
        {
            // WebSocket delivery should not fail solely because audit logging failed.
        }
    }

    private static string? GetWebSocketUserHint(HttpContext context)
    {
        var queryHint = context.Request.Query["userId"].ToString();
        if (!string.IsNullOrWhiteSpace(queryHint))
        {
            return queryHint.Trim();
        }

        var headerHint = context.Request.Headers["X-Agent-User-Id"].ToString();
        return string.IsNullOrWhiteSpace(headerHint) ? null : headerHint.Trim();
    }

    private static async Task WriteJsonAsync(HttpContext context, int statusCode, object payload)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions, context.RequestAborted);
    }

    private sealed record WebSocketAuthentication(AuthenticatedUser Principal, string TokenHash);
}
