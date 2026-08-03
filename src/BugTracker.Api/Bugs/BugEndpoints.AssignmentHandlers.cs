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
    /// Assigns a ticket to a user; restricted to senior/admin roles.
    /// </summary>
    private static async Task<IResult> AllocateBugAsync(
        HttpContext context,
        BugRepository repository,
        ProjectRepository projectRepository,
        ProjectAuthorizationService authorizationService,
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromBody] AllocateBugRequest request,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsSeniorOrAdmin(principal.Role) || principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { error = "id is required" });
        }

        if (string.IsNullOrWhiteSpace(request.AssigneeUserId))
        {
            return Results.BadRequest(new { error = "assignee_user_id is required" });
        }
        if (ValidateExpectedVersion(request.ExpectedVersion) is { } versionError) return versionError;

        var bugId = id.Trim();
        var existingTicket = await repository.GetBugByIdAsync(bugId, ct);
        if (existingTicket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        var project = await projectRepository.GetProjectByIdAsync(existingTicket.ProjectId, ct);
        if (project is null)
        {
            return Results.NotFound(new { error = "project not found" });
        }

        if (!await authorizationService.CanAssignTicketAsync(principal, project, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var result = await repository.AllocateBugAsync(bugId, request.AssigneeUserId.Trim(), principal.UserId, principal.UserType, request.ExpectedVersion, DateTimeOffset.UtcNow, ct);
            if (result.Conflict is not null) return ToVersionConflictResult(result.Conflict);

            return result.ErrorCode switch
            {
                null => Results.Ok(ToCallerSafeTicket(principal, result.Ticket!)),
                "bug_not_found" => Results.NotFound(new { error = "ticket not found" }),
                "invalid_assignee" => Results.BadRequest(new { error = "assignee_user_id must reference an active user" }),
                "assignee_not_project_member" => Results.BadRequest(new { error = "assignee must be added to this sensitive project before assignment", errorCode = "assignee_not_project_member" }),
                "invalid_assignee_for_project" => Results.BadRequest(new { error = "AI agent assignment requires an active human dev or senior on the ticket project" }),
                "forbidden" => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => Results.BadRequest(new { error = "unable to allocate ticket" })
            };
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    private static async Task<IResult> BulkAllocateBugsAsync(
        HttpContext context,
        BugRepository repository,
        ProjectRepository projectRepository,
        ProjectAuthorizationService authorizationService,
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        AuditLogger auditLogger,
        [FromBody] BulkAllocateBugRequest request,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsSeniorOrAdmin(principal.Role) || principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if ((request.TicketIds is null || request.TicketIds.Count == 0) && (request.Items is null || request.Items.Count == 0))
        {
            return Results.BadRequest(new { error = "ticketIds is required" });
        }

        if ((request.Items?.Count ?? request.TicketIds?.Count ?? 0) > 100)
        {
            return Results.BadRequest(new { error = "ticketIds supports at most 100 ids" });
        }

        if (string.IsNullOrWhiteSpace(request.AssigneeUserId))
        {
            return Results.BadRequest(new { error = "assignee_user_id is required" });
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return VersionRequiredResult();
        }

        var requestedItems = request.Items;
        if (requestedItems.Any(item => item.ExpectedVersion is null))
            return VersionRequiredResult();
        if (requestedItems.Any(item => item.ExpectedVersion is <= 0))
            return Results.BadRequest(new { error = "expectedVersion must be a positive integer" });
        var ticketIds = (requestedItems ?? [])
            .Select(item => item.TicketId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ticketIds.Count == 0)
        {
            return Results.BadRequest(new { error = "ticketIds is required" });
        }

        var assigneeUserId = request.AssigneeUserId.Trim();

        foreach (var ticketId in ticketIds)
        {
            var ticket = await repository.GetBugByIdAsync(ticketId, ct);
            if (ticket is null)
            {
                continue;
            }

            var project = await projectRepository.GetProjectByIdAsync(ticket.ProjectId, ct);
            if (project is null || !await authorizationService.CanAssignTicketAsync(principal, project, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        try
        {
            var itemVersions = (requestedItems ?? []).Where(item => !string.IsNullOrWhiteSpace(item.TicketId))
                .GroupBy(item => item.TicketId.Trim(), StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().ExpectedVersion, StringComparer.Ordinal);
            var items = ticketIds.Select(ticketId => new BulkAllocateItem(ticketId, itemVersions.GetValueOrDefault(ticketId))).ToList();
            var result = await repository.BulkAllocateBugsAsync(items, assigneeUserId, principal.UserId, principal.UserType, DateTimeOffset.UtcNow, ct);
            if (result.ErrorCode == "invalid_assignee")
            {
                return Results.BadRequest(new { error = "assignee_user_id must reference an active user" });
            }
            if (result.Failed.Any(failure => failure.Error == "forbidden"))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                updated = result.Updated.Select(ToListItem).ToList(),
                failed = result.Failed
            });
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    private static async Task<IResult> UpdateBugMetadataAsync(
        HttpContext context,
        BugRepository repository,
        ProjectRepository projectRepository,
        ProjectAuthorizationService authorizationService,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromBody] UpdateBugMetadataRequest request,
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

        var issueTitle = string.IsNullOrWhiteSpace(request.IssueTitle)
            ? ticket.IssueTitle
            : request.IssueTitle.Trim();
        if (string.IsNullOrWhiteSpace(issueTitle))
        {
            return Results.BadRequest(new { error = "issue_title is required" });
        }
        if (issueTitle.Length > BugReportLimits.TitleCharacters)
            return Results.BadRequest(new { error = $"issue_title must be {BugReportLimits.TitleCharacters} characters or less" });

        var bugType = string.IsNullOrWhiteSpace(request.BugType)
            ? ticket.BugType
            : request.BugType.Trim().ToLowerInvariant();
        if (!AllowedBugTypes.Contains(bugType))
        {
            return Results.BadRequest(new { error = "invalid bug_type" });
        }

        var severity = string.IsNullOrWhiteSpace(request.Severity)
            ? ticket.Severity
            : request.Severity.Trim().ToLowerInvariant();
        if (!AllowedSeverities.Contains(severity))
        {
            return Results.BadRequest(new { error = "invalid severity" });
        }

        var priority = string.IsNullOrWhiteSpace(request.Priority)
            ? ticket.Priority
            : request.Priority.Trim().ToLowerInvariant();
        if (!AllowedPriorities.Contains(priority))
        {
            return Results.BadRequest(new { error = "invalid priority" });
        }

        if (!IsPriorityValidForSeverity(severity, priority))
        {
            return Results.BadRequest(new
            {
                error = "urgent severity requires priority p0 or p1",
                fieldErrors = new { priority = "priority must be p0 or p1 when severity is urgent" }
            });
        }

        var tagsResult = request.Tags is null
            ? TagsValidationResult.Valid(ticket.Tags)
            : ValidateAndNormalizeTags(request.Tags);
        if (!tagsResult.IsValid)
        {
            return Results.BadRequest(new { error = tagsResult.Error });
        }

        var projectId = string.IsNullOrWhiteSpace(request.ProjectId)
            ? ticket.ProjectId
            : request.ProjectId.Trim();
        var project = await projectRepository.GetProjectByIdAsync(projectId, ct);
        if (project is null)
        {
            return Results.BadRequest(new { error = "invalid project_id" });
        }

        var currentProject = await projectRepository.GetProjectByIdAsync(ticket.ProjectId, ct);
        if (currentProject is null)
        {
            return Results.NotFound(new { error = "project not found" });
        }

        if (!string.Equals(projectId, ticket.ProjectId, StringComparison.Ordinal) &&
            !await authorizationService.CanCreateTicketInProjectAsync(principal, project, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!string.Equals(projectId, ticket.ProjectId, StringComparison.Ordinal) &&
            !string.Equals(currentProject.Visibility, project.Visibility, StringComparison.Ordinal) &&
            (principal.Role != "admin" || principal.UserType != "human"))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!string.Equals(projectId, ticket.ProjectId, StringComparison.Ordinal) &&
            project.Visibility == ProjectVisibilities.Sensitive &&
            ticket.AssigneeUserId is not null &&
            !await projectRepository.IsUserAllocatedToProjectAsync(ticket.AssigneeUserId, project.ProjectId, ct))
        {
            return Results.BadRequest(new
            {
                error = "the current assignee must be added to the sensitive project before moving the ticket",
                errorCode = "assignee_not_project_member"
            });
        }

        var before = ToMetadataSnapshot(ticket);
        try
        {
            var updated = await repository.UpdateMetadataAsync(
                bugId,
                issueTitle,
                bugType,
                project.ProjectId,
                severity,
                priority,
                tagsResult.Tags,
                principal.UserId,
                principal.UserType,
                request.ExpectedVersion,
                DateTimeOffset.UtcNow,
                ct);
            if (updated.Conflict is not null) return ToVersionConflictResult(updated.Conflict);
            if (updated.Value is null)
            {
                if (updated.ErrorCode == "forbidden") return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (updated.ErrorCode == "assignee_not_project_member") return Results.BadRequest(new { error = "the current assignee must be added to the sensitive project before moving the ticket", errorCode = updated.ErrorCode });
                return updated.ErrorCode == "bug_not_found_or_closed"
                    ? Results.BadRequest(new { error = "closed tickets cannot be edited; reopen first" })
                    : Results.NotFound(new { error = "ticket not found" });
            }

            return Results.Ok(ToCallerSafeTicket(principal, updated.Value));
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }
}
