using BugTracker.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace BugTracker.Api.Audit;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit-logs", ListAuditLogsAsync);
        return app;
    }

    private static async Task<IResult> ListAuditLogsAsync(
        HttpContext context,
        AuditRepository repository,
        [FromQuery] string? actorType,
        [FromQuery] string? search,
        [FromQuery] string? ticketId,
        [FromQuery] string? action,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var principal = context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (principal.Role != "admin" || principal.UserType == "agent")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var normalizedActorType = string.IsNullOrWhiteSpace(actorType) ? null : actorType.Trim().ToLowerInvariant();
        if (normalizedActorType is not null && normalizedActorType is not ("human" or "agent" or "system"))
        {
            return Results.BadRequest(new { error = "actorType must be human, agent, or system" });
        }

        var normalizedLimit = limit ?? 100;
        if (normalizedLimit is <= 0 or > 500)
        {
            return Results.BadRequest(new { error = "limit must be between 1 and 500" });
        }

        var normalizedSearch = Normalize(search, 120)?.ToLowerInvariant();
        var logs = await repository.ListAsync(new AuditLogFilter(
            normalizedActorType,
            normalizedSearch,
            Normalize(ticketId, 120),
            Normalize(action, 80)?.ToLowerInvariant(),
            normalizedLimit), ct);

        return Results.Ok(logs);
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
