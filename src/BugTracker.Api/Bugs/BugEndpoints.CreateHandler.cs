using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    /// <summary>
    /// Creates a new bug ticket after validating required fields, enums, and images.
    /// </summary>
    private static async Task<IResult> CreateBugAsync(
        [FromBody] CreateBugRequest request,
        HttpContext context,
        BugRepository repository,
        ProjectRepository projectRepository,
        ProjectAuthorizationService authorizationService,
        ImageValidationService imageValidationService,
        NotificationRepository notificationRepository,
        AgentNotificationSocketHub socketHub,
        AuditLogger auditLogger,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IssueTitle) || string.IsNullOrWhiteSpace(request.Description))
        {
            return Results.BadRequest(new { error = "issue_title and description are required" });
        }

        if (string.IsNullOrWhiteSpace(request.BugType) || string.IsNullOrWhiteSpace(request.Severity))
        {
            return Results.BadRequest(new { error = "bug_type and severity are required" });
        }

        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            return Results.BadRequest(new { error = "project_id is required" });
        }

        var bugType = request.BugType.Trim().ToLowerInvariant();
        if (!AllowedBugTypes.Contains(bugType))
        {
            return Results.BadRequest(new { error = "invalid bug_type" });
        }

        var severity = request.Severity.Trim().ToLowerInvariant();
        if (!AllowedSeverities.Contains(severity))
        {
            return Results.BadRequest(new { error = "invalid severity" });
        }

        var priority = string.IsNullOrWhiteSpace(request.Priority)
            ? "p2"
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

        var tagsResult = ValidateAndNormalizeTags(request.Tags);
        if (!tagsResult.IsValid)
        {
            return Results.BadRequest(new { error = tagsResult.Error });
        }

        var issueTitle = request.IssueTitle.Trim();
        var description = request.Description.Trim();
        if (issueTitle.Length > BugReportLimits.TitleCharacters)
            return Results.BadRequest(new { error = $"issue_title must be {BugReportLimits.TitleCharacters} characters or less" });
        if (description.Length > BugReportLimits.InitialReportCharacters)
            return Results.BadRequest(new { error = $"description must be {BugReportLimits.InitialReportCharacters} characters or less" });
        var environmentResult = ValidateOptionalText(request.Environment, BugReportLimits.EnvironmentCharacters, "environment");
        var expectedBehaviorResult = ValidateOptionalText(request.ExpectedBehavior, BugReportLimits.ExpectedBehaviorCharacters, "expected_behavior");
        var actualBehaviorResult = ValidateOptionalText(request.ActualBehavior, BugReportLimits.ActualBehaviorCharacters, "actual_behavior");
        var stepsResult = ValidateOptionalText(request.StepsToReproduce, BugReportLimits.StepsToReproduceCharacters, "steps_to_reproduce");
        var invalidOptionalText = new[] { environmentResult, expectedBehaviorResult, actualBehaviorResult, stepsResult }.FirstOrDefault(result => !result.IsValid);
        if (invalidOptionalText is not null) return Results.BadRequest(new { error = invalidOptionalText.Error });
        var environment = environmentResult.Value;
        var expectedBehavior = expectedBehaviorResult.Value;
        var actualBehavior = actualBehaviorResult.Value;
        var stepsToReproduce = stepsResult.Value;
        var frequency = string.IsNullOrWhiteSpace(request.Frequency)
            ? null
            : request.Frequency.Trim().ToLowerInvariant();
        if (frequency is not null && !AllowedFrequencies.Contains(frequency))
        {
            return Results.BadRequest(new
            {
                error = "invalid frequency",
                fieldErrors = new { frequency = "frequency must be unknown, once, intermittent, frequent, or always" }
            });
        }

        var projectId = request.ProjectId.Trim();
        var project = await projectRepository.GetProjectByIdAsync(projectId, ct);
        if (project is null)
        {
            return Results.BadRequest(new { error = "invalid project_id" });
        }

        if (!await authorizationService.CanCreateTicketInProjectAsync(principal, project, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        string? assigneeUserId = null;
        if (request.AssigneeUserId is not null)
        {
            if (string.IsNullOrWhiteSpace(request.AssigneeUserId))
            {
                return Results.BadRequest(new { error = "assignee_user_id cannot be blank" });
            }

            if (!await authorizationService.CanAssignTicketAsync(principal, project, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            assigneeUserId = request.AssigneeUserId.Trim();
        }

        var reportImagesResult = ValidateAndNormalizeImages(request.ReportImages, imageValidationService);
        if (!reportImagesResult.IsValid)
        {
            return ToEmbeddedImageValidationFailure(reportImagesResult);
        }

        var textEvidenceResult = ValidateAndNormalizeTextEvidence(request.TextEvidence);
        if (!textEvidenceResult.IsValid)
        {
            return Results.BadRequest(new { error = textEvidenceResult.Error });
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var createResult = await repository.CreateBugAsync(
                principal.UserId,
                issueTitle,
                description,
                bugType,
                project.ProjectId,
                project.Name,
                severity,
                priority,
                tagsResult.Tags,
                environment,
                expectedBehavior,
                actualBehavior,
                stepsToReproduce,
                frequency,
                reportImagesResult.Images,
                textEvidenceResult.Evidence,
                assigneeUserId,
                principal.UserType,
                now,
                ct);

            if (createResult.Ticket is null)
            {
                return createResult.ErrorCode switch
                {
                    "invalid_assignee" => Results.BadRequest(new { error = "assignee_user_id must reference an active user" }),
                    "assignee_not_project_member" => Results.BadRequest(new { error = "assignee must be added to this sensitive project before assignment", errorCode = "assignee_not_project_member" }),
                    "invalid_assignee_for_project" => Results.BadRequest(new { error = "AI agent assignment requires an active human dev or senior on the ticket project" }),
                    "forbidden" => Results.StatusCode(StatusCodes.Status403Forbidden),
                    "project_not_found" => Results.BadRequest(new { error = "invalid project_id" }),
                    _ => Results.BadRequest(new { error = "unable to create ticket" })
                };
            }

            var bug = createResult.Ticket;

            return Results.Created($"/api/bugs/{bug.Id}", ToCallerSafeTicket(principal, bug));
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

}
