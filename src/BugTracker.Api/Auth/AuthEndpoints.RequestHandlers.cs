using BugTracker.Api.Audit;
using BugTracker.Api.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace BugTracker.Api.Auth;

public static partial class AuthEndpoints
{
    private static async Task<IResult> ListRequestsAsync(
        HttpContext httpContext,
        AuthRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsHumanAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var requests = await repository.ListUserRequestsAsync(ct);
        var recoveries = await repository.ListCredentialRecoveryRequestsAsync(ct);
        requests = requests.Concat(recoveries).OrderByDescending(request => request.CreatedAt, StringComparer.Ordinal).ToArray();
        return Results.Ok(requests);
    }

    private static async Task<IResult> CreateRequestAsync(
        HttpContext httpContext,
        [FromBody] CreateUserRequest request,
        AuthRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsHumanAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await CreateRequestInternalAsync(request, repository, ct);
    }

    private static async Task<IResult> CreateRequestAccessAsync(
        HttpContext httpContext,
        [FromBody] CreateUserRequest request,
        AuthRepository repository,
        PublicEndpointIdentityRateLimiter rateLimiter,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.RequestType))
        {
            return Results.BadRequest(new { error = "email and requestType are required" });
        }

        if (request.Email.Length > MaxEmailLength || request.RequestType.Length > 32)
        {
            return Results.BadRequest(new { error = "invalid access request" });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (!LooksLikeEmail(normalizedEmail))
        {
            return Results.BadRequest(new { error = "invalid email format" });
        }

        var requestType = request.RequestType.Trim().ToLowerInvariant();
        if (requestType is not ("human" or "ai_agent"))
        {
            return Results.BadRequest(new { error = "requestType must be human or ai_agent" });
        }

        using var rateLimitLease = await rateLimiter.AcquireAsync(PublicRateLimitPolicies.AccessRequest, normalizedEmail, ct);
        if (!rateLimitLease.IsAcquired)
        {
            return RateLimitResponses.TooManyRequests(httpContext, rateLimitLease);
        }

        var username = BuildUserIdFromEmail(normalizedEmail);
        await repository.CreateUserRequestAsync(normalizedEmail, requestType, username, DateTimeOffset.UtcNow, ct);
        return Results.Accepted(value: new
        {
            message = environment.IsEnvironment("Demo")
                ? "If eligible, the access request will be reviewed. No email is sent by this demo."
                : "If eligible, the access request will be reviewed."
        });
    }

    private static async Task<IResult> CreateCredentialRecoveryRequestAsync(
        HttpContext httpContext,
        [FromBody] CreateCredentialRecoveryRequest request,
        AuthRepository repository,
        PublicEndpointIdentityRateLimiter rateLimiter,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.RequestType)
            || request.Email.Length > MaxEmailLength || request.RequestType.Length > 32)
        {
            return Results.BadRequest(new { error = "email and requestType are required" });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var requestType = request.RequestType.Trim().ToLowerInvariant();
        if (!LooksLikeEmail(email) || requestType is not ("human" or "ai_agent"))
        {
            return Results.BadRequest(new { error = "invalid credential recovery request" });
        }

        using var lease = await rateLimiter.AcquireAsync(PublicRateLimitPolicies.AccessRequest, email, ct);
        if (!lease.IsAcquired)
        {
            return RateLimitResponses.TooManyRequests(httpContext, lease);
        }

        await repository.CreateCredentialRecoveryRequestAsync(email, requestType, DateTimeOffset.UtcNow, ct);
        var message = requestType == "ai_agent"
            ? "If the account exists, an administrator can review the oath-token recovery request."
            : "If the account exists, an administrator can review the password reset request.";
        if (environment.IsEnvironment("Demo"))
        {
            message += " No email is sent by this demo.";
        }

        return Results.Accepted(value: new { message });
    }

    private static async Task<IResult> CreateRequestInternalAsync(
        CreateUserRequest request,
        AuthRepository repository,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.RequestType))
        {
            return Results.BadRequest(new { error = "email and requestType are required" });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (!LooksLikeEmail(normalizedEmail))
        {
            return Results.BadRequest(new { error = "invalid email format" });
        }

        var requestType = request.RequestType.Trim().ToLowerInvariant();
        if (requestType is not ("human" or "ai_agent"))
        {
            return Results.BadRequest(new { error = "requestType must be human or ai_agent" });
        }

        var username = BuildUserIdFromEmail(normalizedEmail);
        var created = await repository.CreateUserRequestAsync(normalizedEmail, requestType, username, DateTimeOffset.UtcNow, ct);
        return created is null
            ? Results.BadRequest(new { error = "request email or username already exists" })
            : Results.Created($"/api/auth/requests/{created.RequestId}", created);
    }

    private static async Task<IResult> UpdateRequestUsernameAsync(
        HttpContext httpContext,
        [FromRoute] string requestId,
        [FromBody] UpdateRequestUsernameRequest request,
        AuthRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsHumanAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.BadRequest(new { error = "requestId and username are required" });
        }

        var normalizedUsername = NormalizeUserId(request.Username);
        var updated = await repository.UpdateRequestUsernameAsync(requestId.Trim(), normalizedUsername, DateTimeOffset.UtcNow, ct);
        return updated is null
            ? Results.BadRequest(new { error = "unable to update request username" })
            : Results.Ok(updated);
    }

    private static async Task<IResult> IssueSetupLinkAsync(
        HttpContext httpContext,
        [FromRoute] string requestId,
        AuthRepository repository,
        TokenService tokenService,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsHumanAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var requestRecord = await repository.GetUserRequestByIdAsync(requestId.Trim(), ct);
        if (requestRecord is null || requestRecord.RequestType != "human")
        {
            return Results.NotFound(new { error = "human request not found" });
        }

        var token = tokenService.GenerateRawToken();
        var tokenHash = tokenService.HashToken(token);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        var updated = await repository.SetRequestSetupTokenAsync(requestRecord.RequestId, tokenHash, expiresAt, DateTimeOffset.UtcNow, ct);
        if (!updated)
        {
            return Results.BadRequest(new { error = "unable to issue setup link" });
        }

        var frontendOrigin = configuration["Frontend:Origin"] ?? "http://127.0.0.1:5173";
        var link = $"{frontendOrigin.TrimEnd('/')}/setup-password?email={Uri.EscapeDataString(requestRecord.Email)}&token={Uri.EscapeDataString(token)}";
        return Results.Ok(new RequestActionResponse(
            "setup link issued",
            link,
            null,
            null,
            expiresAt));
    }

    private static async Task<IResult> IssuePasswordResetAsync(
        HttpContext httpContext,
        [FromRoute] string recoveryId,
        AuthRepository repository,
        TokenService tokenService,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null) return Results.Unauthorized();
        if (!IsHumanAdmin(principal)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var recovery = await repository.GetCredentialRecoveryRequestAsync(recoveryId.Trim(), ct);
        if (recovery is null || recovery.RequestType != "human")
        {
            return Results.NotFound(new { error = "human credential recovery request not found" });
        }

        var token = tokenService.GenerateRawToken();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(30);
        var issued = await repository.IssuePasswordResetAsync(recovery.RecoveryId, tokenService.HashToken(token), expiresAt, principal.UserId, now, ct);
        if (!issued) return Results.BadRequest(new { error = "unable to issue password reset link" });

        var origin = configuration["Frontend:Origin"] ?? "http://127.0.0.1:5173";
        var link = $"{origin.TrimEnd('/')}/setup-password?email={Uri.EscapeDataString(recovery.Email)}&token={Uri.EscapeDataString(token)}";
        return Results.Ok(new RequestActionResponse("password reset link issued", link, null, null, expiresAt));
    }

    private static async Task<IResult> IssueAgentRecoveryApiKeyAsync(
        HttpContext httpContext,
        [FromRoute] string recoveryId,
        [FromBody] IssueAgentApiKeyRequest? request,
        AuthRepository repository,
        TokenService tokenService,
        IHostEnvironment environment,
        AuditLogger auditLogger,
        AgentNotificationSocketHub socketHub,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null) return Results.Unauthorized();
        if (!IsHumanAdmin(principal)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var recovery = await repository.GetCredentialRecoveryRequestAsync(recoveryId.Trim(), ct);
        if (recovery is null || recovery.RequestType != "ai_agent")
        {
            return Results.NotFound(new { error = "AI agent credential recovery request not found" });
        }

        var activeDays = environment.IsEnvironment("Demo") ? 1 : request?.ActiveDays ?? DefaultAgentOathTokenLifespanDays;
        if (activeDays is < MinAgentOathTokenLifespanDays or > MaxAgentOathTokenLifespanDays)
        {
            return Results.BadRequest(new { error = "activeDays must be between 1 and 62 days" });
        }

        var rawKey = GenerateApiKey128();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(activeDays);
        var saved = await repository.RotateAgentApiKeyAsync(recovery.UserId, tokenService.HashToken(rawKey), rawKey[..16], expiresAt, now, ct);
        if (!saved) return Results.BadRequest(new { error = "unable to issue oath token" });
        await socketHub.CloseUserConnectionsAsync(recovery.UserId, ct);
        await auditLogger.LogAsync(principal, "agent_oath_rotated", "AI agent oath token recovered and rotated.", null,
            new { agentUserId = recovery.UserId, recoveryId = recovery.RecoveryId, tokenPrefix = rawKey[..16], expiresAt }, ct);
        return Results.Ok(new RequestActionResponse("oath token issued", null, rawKey, recovery.Username, expiresAt));
    }

    private static async Task<IResult> IssueAgentApiKeyAsync(
        HttpContext httpContext,
        [FromRoute] string requestId,
        [FromBody] IssueAgentApiKeyRequest? request,
        AuthRepository repository,
        TokenService tokenService,
        IHostEnvironment environment,
        AuditLogger auditLogger,
        AgentNotificationSocketHub socketHub,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsHumanAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var requestRecord = await repository.GetUserRequestByIdAsync(requestId.Trim(), ct);
        if (requestRecord is null || requestRecord.RequestType != "ai_agent")
        {
            return Results.NotFound(new { error = "ai request not found" });
        }

        var activeDays = environment.IsEnvironment("Demo") ? 1 : request?.ActiveDays ?? DefaultAgentOathTokenLifespanDays;
        if (activeDays is < MinAgentOathTokenLifespanDays or > MaxAgentOathTokenLifespanDays)
        {
            return Results.BadRequest(new { error = "activeDays must be between 1 and 62 days" });
        }

        var rawKey = GenerateApiKey128();
        var apiKeyHash = tokenService.HashToken(rawKey);
        var apiKeyPrefix = rawKey.Length >= 16 ? rawKey[..16] : rawKey;
        var userId = requestRecord.UserId ?? requestRecord.Username;
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(activeDays);

        var saved = await repository.SetAgentApiKeyAsync(requestRecord.RequestId, apiKeyHash, apiKeyPrefix, expiresAt, userId, now, ct);
        if (!saved)
        {
            return Results.BadRequest(new { error = "unable to issue oath token" });
        }

        await socketHub.CloseUserConnectionsAsync(userId, ct);
        await auditLogger.LogAsync(principal, "agent_oath_issued", "AI agent account created or oath token rotated.", null,
            new { agentUserId = userId, requestId = requestRecord.RequestId, tokenPrefix = apiKeyPrefix, expiresAt }, ct);

        return Results.Ok(new RequestActionResponse(
            "oath token issued",
            null,
            rawKey,
            requestRecord.Username,
            expiresAt));
    }

    private static async Task<IResult> IssueAgentApiKeyForUserAsync(
        HttpContext httpContext,
        [FromRoute] string userId,
        [FromBody] IssueAgentApiKeyRequest? request,
        AuthRepository repository,
        TokenService tokenService,
        IHostEnvironment environment,
        AuditLogger auditLogger,
        AgentNotificationSocketHub socketHub,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsHumanAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.BadRequest(new { error = "userId is required" });
        }

        var requestRecord = await repository.GetAgentRequestByUserIdAsync(userId.Trim(), ct);
        if (requestRecord is null)
        {
            return Results.NotFound(new { error = "ai agent request not found" });
        }

        var activeDays = environment.IsEnvironment("Demo") ? 1 : request?.ActiveDays ?? DefaultAgentOathTokenLifespanDays;
        if (activeDays is < MinAgentOathTokenLifespanDays or > MaxAgentOathTokenLifespanDays)
        {
            return Results.BadRequest(new { error = "activeDays must be between 1 and 62 days" });
        }

        var rawKey = GenerateApiKey128();
        var apiKeyHash = tokenService.HashToken(rawKey);
        var apiKeyPrefix = rawKey.Length >= 16 ? rawKey[..16] : rawKey;
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(activeDays);

        var saved = await repository.RotateAgentApiKeyAsync(requestRecord.UserId!, apiKeyHash, apiKeyPrefix, expiresAt, now, ct);
        if (!saved)
        {
            return Results.BadRequest(new { error = "unable to issue oath token" });
        }

        await socketHub.CloseUserConnectionsAsync(requestRecord.UserId!, ct);
        await auditLogger.LogAsync(principal, "agent_oath_rotated", "AI agent oath token reissued.", null,
            new { agentUserId = requestRecord.UserId, requestId = requestRecord.RequestId, tokenPrefix = apiKeyPrefix, expiresAt }, ct);

        return Results.Ok(new RequestActionResponse(
            "oath token issued",
            null,
            rawKey,
            requestRecord.Username,
            expiresAt));
    }

    private static async Task<IResult> RemoveRequestAsync(
        HttpContext httpContext,
        [FromRoute] string requestId,
        AuthRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsHumanAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var removed = await repository.RemoveRequestAsync(requestId.Trim(), DateTimeOffset.UtcNow, ct);
        return removed
            ? Results.Ok(new { message = "request removed" })
            : Results.NotFound(new { error = "request not found" });
    }

    private static bool IsHumanAdmin(AuthenticatedUser principal) =>
        principal.Role == "admin" && principal.UserType == "human";
}
