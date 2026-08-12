using BugTracker.Api.Audit;
using Microsoft.AspNetCore.Mvc;

namespace BugTracker.Api.Auth;

public static partial class AuthEndpoints
{
    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        [FromBody] LoginRequest request,
        AuthRepository repository,
        PasswordHasherService hasher,
        TokenService tokenService,
        FirstRunSetupService setup,
        LoginSecurityMonitor loginSecurity,
        AuditLogger auditLogger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "email and password are required" });
        }

        if (request.Email.Length > MaxEmailLength || request.Password.Length > MaxPasswordLength)
        {
            return Results.BadRequest(new { error = "invalid login credentials" });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var publicIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var decision = await loginSecurity.CheckAsync(normalizedEmail, publicIp, "human-password", ct);
        if (decision.IsLocked)
        {
            return LoginSecurityMonitor.LockedResult(httpContext, decision.LockedUntil);
        }

        var user = await repository.GetUserByEmailAsync(normalizedEmail, ct);
        if (user is null || user.IsActive != 1)
        {
            var failed = await loginSecurity.RecordFailureAsync(normalizedEmail, publicIp, "human-password", ct);
            return failed.IsLocked ? LoginSecurityMonitor.LockedResult(httpContext, failed.LockedUntil) : Results.Unauthorized();
        }

        if (!hasher.Verify(request.Password, user.PasswordHash))
        {
            var failed = await loginSecurity.RecordFailureAsync(normalizedEmail, publicIp, "human-password", ct);
            return failed.IsLocked ? LoginSecurityMonitor.LockedResult(httpContext, failed.LockedUntil) : Results.Unauthorized();
        }

        await loginSecurity.RecordSuccessAsync(normalizedEmail, publicIp, "human-password", ct);

        var token = tokenService.GenerateRawToken();
        var tokenHash = tokenService.HashToken(token);
        var now = DateTimeOffset.UtcNow;
        var setupState = await setup.GetAsync(ct);
        var expiresAt = now.AddMinutes(setupState.HumanTokenTtlMinutes ?? FirstRunSetupService.DefaultHumanTokenTtlMinutes);

        await repository.CreateAuthTokenAsync(user.UserId, tokenHash, now, expiresAt, ct);
        await repository.UpdateLastSeenAsync(user.UserId, now, ct);
        await auditLogger.LogAsync(
            user.UserId,
            user.UserType,
            "login",
            "Human user logged in.",
            null,
            new { user.Role, expiresAt = expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") },
            ct);

        var profile = await repository.GetUserProfileByUserIdAsync(user.UserId, ct);
        return profile is null
            ? Results.Unauthorized()
            : Results.Ok(new LoginResponse(token, expiresAt, profile));
    }

    private static async Task<IResult> AgentLoginAsync(
        HttpContext httpContext,
        [FromBody] AgentLoginRequest request,
        AuthRepository repository,
        TokenService tokenService,
        FirstRunSetupService setup,
        LoginSecurityMonitor loginSecurity,
        AuditLogger auditLogger,
        CancellationToken ct)
    {
        if (!(await setup.GetAsync(ct)).IsComplete)
        {
            return Results.Json(
                new { error = "first-run setup is incomplete", errorCode = "setup_incomplete" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.OathToken))
        {
            return Results.BadRequest(new { error = "username and oathToken are required" });
        }

        if (request.Username.Length > MaxUsernameLength || request.OathToken.Length > MaxCredentialTokenLength)
        {
            return Results.BadRequest(new { error = "invalid agent credentials" });
        }

        var username = NormalizeUserId(request.Username);
        var publicIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var decision = await loginSecurity.CheckAsync(username, publicIp, "agent-oath", ct);
        if (decision.IsLocked)
        {
            return LoginSecurityMonitor.LockedResult(httpContext, decision.LockedUntil);
        }

        var oathTokenHash = tokenService.HashToken(request.OathToken.Trim());
        var user = await repository.GetAgentUserByOathTokenAsync(username, oathTokenHash, DateTimeOffset.UtcNow, ct);
        if (user is null || user.IsActive != 1)
        {
            var failed = await loginSecurity.RecordFailureAsync(username, publicIp, "agent-oath", ct);
            return failed.IsLocked ? LoginSecurityMonitor.LockedResult(httpContext, failed.LockedUntil) : Results.Unauthorized();
        }

        await loginSecurity.RecordSuccessAsync(username, publicIp, "agent-oath", ct);

        var token = tokenService.GenerateRawToken();
        var tokenHash = tokenService.HashToken(token);
        var now = DateTimeOffset.UtcNow;
        var configuredExpiry = now.AddDays((await setup.GetAsync(ct)).AgentOathTtlDays ?? FirstRunSetupService.DefaultAgentOathTtlDays);
        var expiresAt = user.OathTokenExpiresAt < configuredExpiry ? user.OathTokenExpiresAt : configuredExpiry;

        await repository.CreateAuthTokenAsync(user.UserId, tokenHash, now, expiresAt, ct);
        await repository.UpdateLastSeenAsync(user.UserId, now, ct);
        await auditLogger.LogAsync(
            user.UserId,
            "agent",
            "agent_login",
            "AI agent logged in with oath token.",
            null,
            new { username, oathTokenVerified = true, expiresAt = expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") },
            ct);

        var profile = await repository.GetUserProfileByUserIdAsync(user.UserId, ct);
        return profile is null
            ? Results.Unauthorized()
            : Results.Ok(new LoginResponse(token, expiresAt, profile));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        AuthRepository repository,
        AuditLogger auditLogger,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        await repository.RevokeAuthTokenAsync(principal.TokenHash, DateTimeOffset.UtcNow, ct);
        await auditLogger.LogAsync(
            principal,
            principal.UserType == "agent" ? "agent_logout" : "logout",
            principal.UserType == "agent" ? "AI agent logged out." : "Human user logged out.",
            null,
            null,
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(HttpContext httpContext, AuthRepository repository, CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var profile = await repository.GetUserProfileByUserIdAsync(principal.UserId, ct);
        if (profile is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(profile);
    }

    private static async Task<IResult> SetupPasswordAsync(
        HttpContext httpContext,
        [FromBody] SetupPasswordRequest request,
        AuthRepository repository,
        TokenService tokenService,
        PasswordHasherService hasher,
        PublicEndpointIdentityRateLimiter rateLimiter,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Results.BadRequest(new { error = "email, token, and newPassword are required" });
        }

        if (request.Email.Length > MaxEmailLength
            || request.Token.Length > MaxCredentialTokenLength
            || request.NewPassword.Length > MaxPasswordLength)
        {
            return Results.BadRequest(new { error = "invalid setup credentials" });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (!LooksLikeEmail(normalizedEmail))
        {
            return Results.BadRequest(new { error = "invalid email format" });
        }

        if (!IsStrongPassword(request.NewPassword, out var passwordError))
        {
            return Results.BadRequest(new { error = passwordError });
        }

        using var rateLimitLease = await rateLimiter.AcquireAsync(PublicRateLimitPolicies.PasswordSetup, normalizedEmail, ct);
        if (!rateLimitLease.IsAcquired)
        {
            return RateLimitResponses.TooManyRequests(httpContext, rateLimitLease);
        }

        var tokenHash = tokenService.HashToken(request.Token.Trim());
        var now = DateTimeOffset.UtcNow;
        if (await repository.ConsumePasswordResetAsync(normalizedEmail, tokenHash, hasher.Hash(request.NewPassword), now, ct))
        {
            return Results.Ok(new { message = "password updated" });
        }

        var userRequest = await repository.GetHumanRequestByEmailAndTokenHashAsync(normalizedEmail, tokenHash, now, ct);
        if (userRequest is null)
        {
            return Results.BadRequest(new { error = "invalid setup credentials" });
        }

        var updatedUser = await repository.UpsertHumanUserFromRequestAsync(
            userRequest.RequestId,
            userRequest.Username,
            userRequest.Email,
            hasher.Hash(request.NewPassword),
            now,
            ct);

        if (updatedUser is null)
        {
            return Results.BadRequest(new { error = "unable to set password" });
        }

        return Results.Ok(new { message = "password updated" });
    }
    private static IResult DevOnly(HttpContext httpContext)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (principal.Role is not ("dev" or "senior" or "admin"))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new { message = "dev-authorized endpoint" });
    }

    private static IResult SeniorOnly(HttpContext httpContext)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (principal.Role is not ("senior" or "admin"))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new { message = "senior-authorized endpoint" });
    }

    private static IResult AdminOnly(HttpContext httpContext)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (principal.Role != "admin")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new { message = "admin-authorized endpoint" });
    }
}
