using BugTracker.Api.Audit;
using BugTracker.Api.Notifications;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace BugTracker.Api.Auth;

public static partial class AuthEndpoints
{
    private const int DefaultAgentOathTokenLifespanDays = 30;
    private const int MinAgentOathTokenLifespanDays = 1;
    private const int MaxAgentOathTokenLifespanDays = 62;
    private static readonly TimeSpan HumanOnlineWindow = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync).RequireRateLimiting(PublicRateLimitPolicies.HumanLogin);
        app.MapPost("/api/auth/agent/login", AgentLoginAsync).RequireRateLimiting(PublicRateLimitPolicies.AgentLogin);
        app.MapPost("/api/auth/request-access", CreateRequestAccessAsync).RequireRateLimiting(PublicRateLimitPolicies.AccessRequest);
        app.MapPost("/api/auth/request-credential-recovery", CreateCredentialRecoveryRequestAsync).RequireRateLimiting(PublicRateLimitPolicies.AccessRequest);
        app.MapPost("/api/auth/logout", LogoutAsync);
        app.MapPost("/api/auth/setup-password", SetupPasswordAsync).RequireRateLimiting(PublicRateLimitPolicies.PasswordSetup);
        app.MapGet("/api/auth/me", Me);
        app.MapGet("/api/auth/users", ListUsersAsync);
        app.MapGet("/api/auth/requests", ListRequestsAsync);
        app.MapPost("/api/auth/requests", CreateRequestAsync);
        app.MapPatch("/api/auth/requests/{requestId}/username", UpdateRequestUsernameAsync);
        app.MapPost("/api/auth/requests/{requestId}/issue-setup-link", IssueSetupLinkAsync);
        app.MapPost("/api/auth/recovery-requests/{recoveryId}/issue-password-reset", IssuePasswordResetAsync);
        app.MapPost("/api/auth/recovery-requests/{recoveryId}/issue-api-key", IssueAgentRecoveryApiKeyAsync);
        app.MapPost("/api/auth/requests/{requestId}/issue-api-key", IssueAgentApiKeyAsync);
        app.MapDelete("/api/auth/requests/{requestId}", RemoveRequestAsync);
        app.MapPatch("/api/auth/users/{userId}/role", UpdateUserRoleAsync);
        app.MapPatch("/api/auth/users/{userId}/username", UpdateUsernameAsync);
        app.MapPost("/api/auth/users/{userId}/issue-api-key", IssueAgentApiKeyForUserAsync);

        // Demo authorization endpoints to validate role gating behavior.
        app.MapGet("/api/auth/dev-only", DevOnly);
        app.MapGet("/api/auth/senior-only", SeniorOnly);
        app.MapGet("/api/auth/admin-only", AdminOnly);

        return app;
    }
}
