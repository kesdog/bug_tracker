using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    /// <summary>
    /// Updates the originally submitted bug report for a ticket the user can manage.
    /// </summary>
    private static async Task<IResult> UpdateInitialBugReportAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        ImageValidationService imageValidationService,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromBody] UpdateInitialBugReportRequest request,
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

        if (string.IsNullOrWhiteSpace(request.ReportText))
        {
            return Results.BadRequest(new { error = "report_text is required" });
        }
        if (request.ReportText.Trim().Length > BugReportLimits.InitialReportCharacters)
            return Results.BadRequest(new { error = $"report_text must be {BugReportLimits.InitialReportCharacters} characters or less" });

        var bugId = id.Trim();
        if (ValidateExpectedVersion(request.ExpectedVersion) is { } versionError) return versionError;
        BugTicketDto? ticket;
        try
        {
            ticket = await repository.GetBugByIdAsync(bugId, ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (ticket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        if (!await authorizationService.CanManageTicketAsync(principal, ticket, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var reportImagesResult = ValidateAndNormalizeImages(request.ReportImages, imageValidationService);
        if (!reportImagesResult.IsValid)
        {
            return ToEmbeddedImageValidationFailure(reportImagesResult);
        }

        try
        {
            var updated = await repository.UpdateInitialBugReportAsync(
                bugId,
                request.ReportText.Trim(),
                reportImagesResult.Images,
                principal.UserId,
                principal.UserType,
                request.ExpectedVersion,
                DateTimeOffset.UtcNow,
                ct);

            if (updated.Conflict is not null) return ToVersionConflictResult(updated.Conflict);
            if (updated.Value is null)
            {
                if (updated.ErrorCode == "forbidden") return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (updated.ErrorCode == "image_limit_exceeded") return Results.Json(new { error = "initial report supports at most 3 images across embedded and multipart uploads" }, statusCode: StatusCodes.Status413PayloadTooLarge);
                if (updated.ErrorCode == "image_size_limit_exceeded") return Results.Json(new { error = "initial report images exceed the 12 MiB aggregate quota" }, statusCode: StatusCodes.Status413PayloadTooLarge);
                return updated.ErrorCode == "bug_not_found_or_closed"
                    ? Results.BadRequest(new { error = "closed tickets cannot be edited" })
                    : Results.NotFound(new { error = "ticket not found" });
            }

            return Results.Ok(ToCallerSafeTicket(principal, updated.Value));
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    /// <summary>
    /// Updates report text/images for a ticket the user is allowed to manage.
    /// </summary>
    private static async Task<IResult> UpdateBugReportAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        ImageValidationService imageValidationService,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromBody] UpdateBugReportRequest request,
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

        if (string.IsNullOrWhiteSpace(request.ReportText))
        {
            return Results.BadRequest(new { error = "report_text is required" });
        }
        if (request.ReportText.Trim().Length > BugReportLimits.SolutionReportCharacters)
            return Results.BadRequest(new { error = $"report_text must be {BugReportLimits.SolutionReportCharacters} characters or less" });

        var bugId = id.Trim();
        if (ValidateExpectedVersion(request.ExpectedVersion) is { } versionError) return versionError;
        BugTicketDto? ticket;
        try
        {
            ticket = await repository.GetBugByIdAsync(bugId, ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (ticket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        if (!await authorizationService.CanManageTicketAsync(principal, ticket, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var reportImagesResult = ValidateAndNormalizeImages(request.ReportImages, imageValidationService);
        if (!reportImagesResult.IsValid)
        {
            return ToEmbeddedImageValidationFailure(reportImagesResult);
        }

        try
        {
            var updated = await repository.UpdateBugReportAsync(
                bugId,
                request.ReportText.Trim(),
                reportImagesResult.Images,
                principal.UserId,
                principal.UserType,
                request.ExpectedVersion,
                DateTimeOffset.UtcNow,
                ct);

            if (updated.Conflict is not null) return ToVersionConflictResult(updated.Conflict);
            if (updated.Value is null)
            {
                if (updated.ErrorCode == "forbidden") return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (updated.ErrorCode == "image_limit_exceeded") return Results.BadRequest(new { error = "solution report supports at most 3 images across embedded and multipart uploads" });
                if (updated.ErrorCode == "image_size_limit_exceeded") return Results.BadRequest(new { error = "solution report images exceed the 12 MiB aggregate quota" });
                return Results.NotFound(new { error = "ticket not found" });
            }

            return Results.Ok(ToCallerSafeTicket(principal, updated.Value));
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    /// <summary>
    /// Closes a ticket and stores resolution notes plus optional report images.
    /// </summary>
    private static async Task<IResult> CloseBugAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        ImageValidationService imageValidationService,
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromBody] CloseBugRequest request,
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

        if (string.IsNullOrWhiteSpace(request.ResolutionNotes))
        {
            return Results.BadRequest(new { error = "resolution_notes is required" });
        }
        if (request.ResolutionNotes.Trim().Length > BugReportLimits.SolutionReportCharacters)
            return Results.BadRequest(new { error = $"resolution_notes must be {BugReportLimits.SolutionReportCharacters} characters or less" });

        var bugId = id.Trim();
        if (ValidateExpectedVersion(request.ExpectedVersion) is { } versionError) return versionError;
        BugTicketDto? ticket;
        try
        {
            ticket = await repository.GetBugByIdAsync(bugId, ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (ticket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        if (!await authorizationService.CanCloseTicketAsync(principal, ticket, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var reportImagesResult = ValidateAndNormalizeImages(request.ReportImages, imageValidationService);
        if (!reportImagesResult.IsValid)
        {
            return ToEmbeddedImageValidationFailure(reportImagesResult);
        }

        try
        {
            var closed = await repository.CloseBugAsync(
                bugId,
                principal.UserId,
                principal.UserType,
                request.ResolutionNotes.Trim(),
                reportImagesResult.Images,
                request.ExpectedVersion,
                DateTimeOffset.UtcNow,
                ct);

            if (closed.Conflict is not null) return ToVersionConflictResult(closed.Conflict);
            if (closed.Value is null)
            {
                if (closed.ErrorCode == "forbidden") return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (closed.ErrorCode == "image_limit_exceeded") return Results.BadRequest(new { error = "solution report supports at most 3 images across embedded and multipart uploads" });
                if (closed.ErrorCode == "image_size_limit_exceeded") return Results.BadRequest(new { error = "solution report images exceed the 12 MiB aggregate quota" });
                return closed.ErrorCode == "bug_not_found_or_closed"
                    ? Results.BadRequest(new { error = "ticket is already closed" })
                    : Results.NotFound(new { error = "ticket not found" });
            }

            return Results.Ok(ToCallerSafeTicket(principal, closed.Value));
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    private static async Task<IResult> CancelBugAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        [FromRoute] string id,
        [FromBody] CancelBugRequest request,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new { error = "id and reason are required" });
        }

        var reason = request.Reason.Trim();
        if (reason.Length > BugReportLimits.CommentCharacters)
        {
            return Results.BadRequest(new { error = $"reason must be {BugReportLimits.CommentCharacters} characters or less" });
        }
        if (ValidateExpectedVersion(request.ExpectedVersion) is { } versionError) return versionError;

        var bugId = id.Trim();
        var ticket = await repository.GetBugByIdAsync(bugId, ct);
        if (ticket is null) return Results.NotFound(new { error = "ticket not found" });
        if (!await authorizationService.CanCloseTicketAsync(principal, ticket, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var cancelled = await repository.CancelBugAsync(bugId, principal.UserId, principal.UserType, reason, request.ExpectedVersion, DateTimeOffset.UtcNow, ct);
        if (cancelled.Conflict is not null) return ToVersionConflictResult(cancelled.Conflict);
        if (cancelled.Value is null)
        {
            return cancelled.ErrorCode == "solution_exists"
                ? Results.BadRequest(new { error = "tickets with a solution report must be closed normally" })
                : Results.BadRequest(new { error = "ticket is already archived" });
        }

        return Results.Ok(ToCallerSafeTicket(principal, cancelled.Value));
    }

    private static async Task<IResult> ReopenBugAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromBody] ReopenBugRequest request,
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

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new { error = "reason is required" });
        }

        var reason = request.Reason.Trim();
        if (reason.Length > BugReportLimits.ReopenReasonCharacters)
        {
            return Results.BadRequest(new { error = $"reason must be {BugReportLimits.ReopenReasonCharacters} characters or less" });
        }

        var bugId = id.Trim();
        if (ValidateExpectedVersion(request.ExpectedVersion) is { } versionError) return versionError;
        BugTicketDto? ticket;
        try
        {
            ticket = await repository.GetBugByIdAsync(bugId, ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (ticket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        if (!await authorizationService.CanManageTicketAsync(principal, ticket, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var reopened = await repository.ReopenBugAsync(bugId, principal.UserId, principal.UserType, reason, request.ExpectedVersion, DateTimeOffset.UtcNow, ct);
            if (reopened.Conflict is not null) return ToVersionConflictResult(reopened.Conflict);
            if (reopened.Value is null)
            {
                if (reopened.ErrorCode == "forbidden") return Results.StatusCode(StatusCodes.Status403Forbidden);
                return Results.BadRequest(new { error = "only closed tickets can be reopened" });
            }

            return Results.Ok(ToCallerSafeTicket(principal, reopened.Value));
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    private static async Task<IResult> AddCommentAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromBody] AddBugCommentRequest request,
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

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return Results.BadRequest(new { error = "comment body is required" });
        }

        var body = request.Body.Trim();
        if (body.Length > BugReportLimits.CommentCharacters)
        {
            return Results.BadRequest(new { error = $"comment body must be {BugReportLimits.CommentCharacters} characters or less" });
        }

        var bugId = id.Trim();
        var recipientUserId = string.IsNullOrWhiteSpace(request.RecipientUserId) ? null : request.RecipientUserId.Trim();
        BugTicketDto? ticket;
        try
        {
            ticket = await repository.GetBugByIdAsync(bugId, ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (ticket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        if (!await authorizationService.CanReadTicketAsync(principal, ticket, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (recipientUserId is not null && await repository.GetRelevantActiveContactAsync(bugId, recipientUserId, ct) is null)
        {
            return Results.BadRequest(new { error = "recipientUserId must reference an active ticket participant or project owner", errorCode = "invalid_ticket_contact" });
        }

        try
        {
            var comment = await repository.AddCommentAsync(bugId, principal.UserId, principal.UserType, body, recipientUserId, DateTimeOffset.UtcNow, ct);
            if (comment.Value is null)
            {
                if (comment.ErrorCode == "forbidden") return Results.StatusCode(StatusCodes.Status403Forbidden);
                return Results.NotFound(new { error = "ticket not found" });
            }

            return Results.Created($"/api/bugs/{bugId}#activity-{comment.Value.Id}", comment.Value);
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }
}
