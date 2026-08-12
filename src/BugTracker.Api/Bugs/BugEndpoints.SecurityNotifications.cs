using BugTracker.Api.Auth;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    /// <summary>
    /// Reads the authenticated principal attached by auth middleware.
    /// </summary>
    private static AuthenticatedUser? GetPrincipal(HttpContext context)
    {
        return context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
    }

    /// <summary>
    /// Role guard for operations that require elevated permissions.
    /// </summary>
    private static bool IsSeniorOrAdmin(string role)
    {
        return role is "senior" or "admin";
    }

    private static IResult? ValidateExpectedVersion(int? expectedVersion)
    {
        if (expectedVersion is null)
        {
            return VersionRequiredResult();
        }

        return expectedVersion <= 0
            ? Results.BadRequest(new { error = "expectedVersion must be a positive integer" })
            : null;
    }

    private static IResult VersionRequiredResult()
    {
        return Results.Json(new
        {
            error = "expectedVersion is required for ticket mutations.",
            errorCode = "ticket_version_required",
            recovery = "Fetch the latest ticket, merge your intended change, and retry with its current version."
        }, statusCode: StatusCodes.Status428PreconditionRequired);
    }

    private static IResult ToMutationFailure(string? errorCode, string fallback)
    {
        return errorCode == "forbidden"
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : Results.BadRequest(new { error = fallback });
    }

    private static async Task NotifyTicketParticipantsAsync(
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        ProjectAuthorizationService authorizationService,
        BugTicketDto ticket,
        string actorUserId,
        string kind,
        string message,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var recipients = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(ticket.ReporterUserId))
        {
            recipients.Add(ticket.ReporterUserId);
        }

        if (!string.IsNullOrWhiteSpace(ticket.AssigneeUserId))
        {
            recipients.Add(ticket.AssigneeUserId);
        }

        recipients.Remove(actorUserId);
        foreach (var recipient in recipients)
        {
            if (!await authorizationService.CanUserReadTicketAsync(recipient, ticket, ct))
            {
                continue;
            }

            await CreateAndPublishNotificationAsync(notificationRepository, socketHub, recipient, ticket.Id, kind, message, now, ct);
        }
    }

    private static async Task<NotificationDto> CreateAndPublishNotificationAsync(
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        string userId,
        string? ticketId,
        string kind,
        string message,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var notification = await notificationRepository.CreateAsync(userId, ticketId, kind, message, now, ct);
        return notification;
    }
    private static IResult ToDatabaseFailureResult(BugDataAccessException ex)
    {
        return ex.Error switch
        {
            BugDataAccessError.BusyConcurrency => Results.Json(
                new
                {
                    error = "Database is busy due to concurrent writes. Please retry shortly.",
                    errorCode = "db_busy_concurrency",
                    attempts = ex.Attempts
                },
                statusCode: StatusCodes.Status503ServiceUnavailable),
            BugDataAccessError.Unreachable => Results.Json(
                new
                {
                    error = "The service database is temporarily unavailable. Please retry shortly.",
                    errorCode = "db_unreachable",
                    attempts = ex.Attempts
                },
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(new { error = "Unexpected database failure.", errorCode = "db_unknown" }, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult ToVersionConflictResult(TicketVersionConflict conflict)
    {
        return Results.Json(new
        {
            error = "The ticket changed since it was read.",
            errorCode = "ticket_version_conflict",
            conflict.TicketId,
            conflict.ExpectedVersion,
            conflict.CurrentVersion,
            conflict.CurrentStatus,
            conflict.ChangedFields,
            conflict.Recovery
        }, statusCode: StatusCodes.Status409Conflict);
    }

    private static IResult ToDatabaseFailureResult(SqliteException ex)
    {
        if (SqliteResilience.IsBusy(ex))
        {
            return Results.Json(
                new { error = "Database is busy. Please retry shortly.", errorCode = "db_busy" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (SqliteResilience.IsUnreachable(ex))
        {
            return Results.Json(
                new { error = "Database is unreachable.", errorCode = "db_unreachable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(
            new { error = "Unexpected database failure.", errorCode = "db_unknown" },
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
