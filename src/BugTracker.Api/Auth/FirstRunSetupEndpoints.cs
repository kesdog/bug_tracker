using Microsoft.AspNetCore.Mvc;

namespace BugTracker.Api.Auth;

public static class FirstRunSetupEndpoints
{
    public static IEndpointRouteBuilder MapFirstRunSetupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/first-run/status", StatusAsync);
        app.MapPost("/api/first-run/password", ChangePasswordAsync);
        app.MapPost("/api/first-run/complete", CompleteAsync);
        return app;
    }

    private static async Task<IResult> StatusAsync(HttpContext context, FirstRunSetupService setup, CancellationToken ct)
    {
        var principal = context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
        if (principal is null) return Results.Unauthorized();
        var state = await setup.GetAsync(ct);
        return Results.Ok(new { state.Phase, isRootAdmin = state.IsRootAdmin(principal.UserId), state.HumanTokenTtlMinutes, state.AgentOathTtlDays });
    }

    private static async Task<IResult> ChangePasswordAsync(HttpContext context, [FromBody] FirstRunPasswordRequest request, FirstRunSetupService setup, PasswordHasherService hasher, CancellationToken ct)
    {
        var principal = context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
        if (principal is null) return Results.Unauthorized();
        var state = await setup.GetAsync(ct);
        if (state.Phase != "password_change_required" || !state.IsRootAdmin(principal.UserId)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length > 256) return Results.BadRequest(new { error = "a new password is required" });
        if (!IsStrongPassword(request.NewPassword, out var error)) return Results.BadRequest(new { error });
        return await setup.CompletePasswordChangeAsync(principal.UserId, hasher.Hash(request.NewPassword), DateTimeOffset.UtcNow, ct)
            ? Results.Ok(new { message = "password changed; sign in again" })
            : Results.BadRequest(new { error = "password change could not be completed" });
    }

    private static async Task<IResult> CompleteAsync(HttpContext context, [FromBody] FirstRunTtlRequest request, FirstRunSetupService setup, CancellationToken ct)
    {
        var principal = context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
        if (principal is null) return Results.Unauthorized();
        var state = await setup.GetAsync(ct);
        if (state.Phase != "ttl_required" || !state.IsRootAdmin(principal.UserId)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.HumanTokenTtlMinutes is < FirstRunSetupService.MinHumanTokenTtlMinutes or > FirstRunSetupService.MaxHumanTokenTtlMinutes ||
            request.AgentOathTtlDays is < FirstRunSetupService.MinAgentOathTtlDays or > FirstRunSetupService.MaxAgentOathTtlDays)
        {
            return Results.BadRequest(new { error = "TTL values are outside the supported range" });
        }
        return await setup.CompleteAsync(principal.UserId, request.HumanTokenTtlMinutes, request.AgentOathTtlDays, DateTimeOffset.UtcNow, ct)
            ? Results.Ok(new { message = "first-run setup complete" })
            : Results.BadRequest(new { error = "first-run setup could not be completed" });
    }

    private static bool IsStrongPassword(string password, out string error)
    {
        if (password.Length < 12) { error = "password must be at least 12 characters"; return false; }
        if (!password.Any(char.IsDigit)) { error = "password must include at least one number"; return false; }
        if (!password.Any(ch => !char.IsLetterOrDigit(ch))) { error = "password must include at least one special character"; return false; }
        error = string.Empty;
        return true;
    }
}

public sealed record FirstRunPasswordRequest(string NewPassword);
public sealed record FirstRunTtlRequest(int HumanTokenTtlMinutes, int AgentOathTtlDays);
