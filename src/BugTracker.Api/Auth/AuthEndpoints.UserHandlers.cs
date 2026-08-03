using BugTracker.Api.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Auth;

public static partial class AuthEndpoints
{
    private static async Task<IResult> ListUsersAsync(
        HttpContext httpContext,
        AuthRepository repository,
        AgentNotificationSocketHub socketHub,
        CancellationToken ct)
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

        var now = DateTimeOffset.UtcNow;
        var users = await repository.ListUsersAsync(ct);
        return Results.Ok(users.Select(user => WithPresence(user, socketHub, now)).ToList());
    }

    private static UserRoleRecord WithPresence(UserRoleRecord user, AgentNotificationSocketHub socketHub, DateTimeOffset now)
    {
        if (user.IsActive != 1)
        {
            return user with { IsOnline = false, PresenceStatus = "inactive" };
        }

        if (user.UserType == "agent")
        {
            var connected = socketHub.IsUserConnected(user.UserId);
            return user with { IsOnline = connected, PresenceStatus = connected ? "connected" : "offline" };
        }

        if (TryParseSqliteUtc(user.LastSeenAt, out var lastSeenAt) && now - lastSeenAt <= HumanOnlineWindow)
        {
            return user with { IsOnline = true, PresenceStatus = "active" };
        }

        return user with { IsOnline = false, PresenceStatus = string.IsNullOrWhiteSpace(user.LastSeenAt) ? "offline" : "last_online" };
    }

    private static bool TryParseSqliteUtc(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!DateTime.TryParse(value, out var parsed))
        {
            return false;
        }

        timestamp = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
        return true;
    }

    private static async Task<IResult> UpdateUserRoleAsync(
        HttpContext httpContext,
        [FromRoute] string userId,
        [FromBody] UserRoleUpdateRequest request,
        AuthRepository repository,
        CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(request.Role))
        {
            return Results.BadRequest(new { error = "userId and role are required" });
        }

        var normalizedRole = request.Role.Trim().ToLowerInvariant();
        if (normalizedRole is not ("dev" or "senior" or "admin"))
        {
            return Results.BadRequest(new { error = "role must be dev, senior, or admin" });
        }

        if (string.Equals(principal.UserId, userId.Trim(), StringComparison.Ordinal) && normalizedRole != "admin")
        {
            return Results.BadRequest(new { error = "admin cannot remove their own admin role" });
        }

        var updated = await repository.UpdateUserRoleAsync(userId.Trim(), normalizedRole, DateTimeOffset.UtcNow, ct);
        return updated is null
            ? Results.NotFound(new { error = "user not found" })
            : Results.Ok(updated);
    }

    private static async Task<IResult> UpdateUsernameAsync(
        HttpContext httpContext,
        [FromRoute] string userId,
        [FromBody] UserUsernameUpdateRequest request,
        AuthRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(httpContext);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (principal.Role != "admin" || principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.BadRequest(new { error = "userId is required", errorCode = "username_invalid" });
        }

        if (!UsernamePolicy.TryNormalize(request.Username, out var username, out var error))
        {
            return Results.BadRequest(new { error, errorCode = "username_invalid" });
        }

        try
        {
            var updated = await repository.UpdateUsernameAsync(userId.Trim(), username, DateTimeOffset.UtcNow, ct);
            return updated is null
                ? Results.NotFound(new { error = "user not found" })
                : Results.Ok(updated);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Results.Conflict(new { error = "username already exists", errorCode = "username_taken" });
        }
    }
}
