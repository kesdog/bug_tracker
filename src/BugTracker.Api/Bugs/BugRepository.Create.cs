using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    /// <summary>
    /// Creates a bug record and retries id generation on unique-key collisions.
    /// </summary>
    public async Task<CreateBugResult> CreateBugAsync(
        string reporterUserId,
        string issueTitle,
        string description,
        string bugType,
        string projectId,
        string projectName,
        string severity,
        string priority,
        IReadOnlyList<string> tags,
        string? environment,
        string? expectedBehavior,
        string? actualBehavior,
        string? stepsToReproduce,
        string? frequency,
        IReadOnlyList<ReportImageDto> reportImages,
        IReadOnlyList<TextEvidenceDto> textEvidence,
        string? assigneeUserId,
        string actorType,
        DateTimeOffset now,
        CancellationToken ct)
    {
        return await ExecuteWriteWithRetryAsync(async (connection, token) =>
        {
            string? assigneeType = null;
            if (assigneeUserId is not null)
            {
                assigneeType = await GetAssigneeUserTypeAsync(connection, assigneeUserId, token);
                if (assigneeType is null)
                {
                    return CreateBugResult.InvalidAssignee();
                }

                var visibility = await GetProjectVisibilityAsync(connection, projectId, token);
                if (visibility == "sensitive" &&
                    !await IsUserAllocatedToProjectAsync(connection, assigneeUserId, projectId, token))
                {
                    return CreateBugResult.AssigneeNotProjectMember();
                }

                if (assigneeType == "agent" && !await HasHumanDeveloperOnProjectAsync(connection, projectId, token))
                {
                    return CreateBugResult.InvalidAgentProject();
                }
            }

            var createdAt = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            var updatedAt = createdAt;
            var status = assigneeUserId is null ? "todo" : "open";
            var baseId = BuildBaseId(now, reporterUserId, issueTitle);
            var reportImagesJson = reportImages.Count > 0
                ? JsonSerializer.Serialize(reportImages, JsonOptions)
                : null;
            var tagsJson = JsonSerializer.Serialize(tags, JsonOptions);
            var textEvidenceJson = textEvidence.Count > 0
                ? JsonSerializer.Serialize(textEvidence, JsonOptions)
                : null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                await using var transaction = connection.BeginTransaction(deferred: false);
                var id = attempt == 0
                    ? baseId
                    : $"{baseId}-{Guid.NewGuid().ToString("N")[..6]}";

                var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                    new(reporterUserId, TicketWriteOperation.Create, TargetProjectId: projectId, AssigneeUserId: assigneeUserId), token);
                if (!authorization.IsAllowed)
                {
                    await transaction.RollbackAsync(token);
                    return new CreateBugResult(null, authorization.ErrorCode);
                }

                const string sql = """
                    INSERT INTO bug_tickets (
                        id,
                        issue_title,
                        description,
                        bug_type,
                        project_id,
                        reporter_user_id,
                        assignee_user_id,
                        created_at,
                        updated_at,
                        status,
                        severity,
                        priority,
                        tags_json,
                        environment,
                        expected_behavior,
                        actual_behavior,
                        steps_to_reproduce,
                        frequency,
                        close_date,
                        resolved_by_user_id,
                        assigned_at,
                        resolution_notes,
                        post_resolution_report,
                        report_images_json,
                        resolution_report_images_json,
                        text_evidence_json
                    )
                    VALUES (
                        $id,
                        $issue_title,
                        $description,
                        $bug_type,
                        $project_id,
                        $reporter_user_id,
                        $assignee_user_id,
                        $created_at,
                        $updated_at,
                        $status,
                        $severity,
                        $priority,
                        $tags_json,
                        $environment,
                        $expected_behavior,
                        $actual_behavior,
                        $steps_to_reproduce,
                        $frequency,
                        NULL,
                        NULL,
                        $assigned_at,
                        NULL,
                        NULL,
                        $report_images_json,
                        NULL,
                        $text_evidence_json
                    );
                    """;

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$issue_title", issueTitle);
                command.Parameters.AddWithValue("$description", description);
                command.Parameters.AddWithValue("$bug_type", bugType);
                command.Parameters.AddWithValue("$project_id", projectId);
                command.Parameters.AddWithValue("$reporter_user_id", reporterUserId);
                command.Parameters.AddWithValue("$assignee_user_id", (object?)assigneeUserId ?? DBNull.Value);
                command.Parameters.AddWithValue("$created_at", createdAt);
                command.Parameters.AddWithValue("$updated_at", updatedAt);
                command.Parameters.AddWithValue("$severity", severity);
                command.Parameters.AddWithValue("$status", status);
                command.Parameters.AddWithValue("$priority", priority);
                command.Parameters.AddWithValue("$tags_json", tagsJson);
                command.Parameters.AddWithValue("$environment", (object?)environment ?? DBNull.Value);
                command.Parameters.AddWithValue("$expected_behavior", (object?)expectedBehavior ?? DBNull.Value);
                command.Parameters.AddWithValue("$actual_behavior", (object?)actualBehavior ?? DBNull.Value);
                command.Parameters.AddWithValue("$steps_to_reproduce", (object?)stepsToReproduce ?? DBNull.Value);
                command.Parameters.AddWithValue("$frequency", (object?)frequency ?? DBNull.Value);
                command.Parameters.AddWithValue("$report_images_json", (object?)reportImagesJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$text_evidence_json", (object?)textEvidenceJson ?? DBNull.Value);
                command.Parameters.AddWithValue("$assigned_at", assigneeUserId is null ? DBNull.Value : createdAt);

                try
                {
                    await command.ExecuteNonQueryAsync(token);
                    var createdActivityId = await RecordMutationSideEffectsAsync(
                        connection,
                        id,
                        1,
                        reporterUserId,
                        actorType,
                        "created",
                        $"Ticket created with {severity} severity and {priority} priority.",
                        "ticket_created",
                        ["issueTitle", "description", "bugType", "projectId", "status", "severity", "priority", "tags"],
                        "ticket_created",
                        $"Ticket {id} was created.",
                        createdAt,
                        token,
                        [],
                        transaction: transaction);

                    string? assignedActivityId = null;
                    if (assigneeUserId is not null)
                    {
                        assignedActivityId = await RecordMutationSideEffectsAsync(
                            connection, id, 1, reporterUserId, actorType, "assigned", $"Ticket assigned to {assigneeUserId}.",
                            "ticket_assigned", ["assigneeUserId", "status", "assignedAt"], "ticket_assigned",
                            $"Ticket {id} was assigned to you.", createdAt, token, [assigneeUserId], transaction: transaction, subjectUserId: assigneeUserId);
                    }

                    await transaction.CommitAsync(token);

                    var responseActivity = new List<BugActivityDto>
                    {
                        new(createdActivityId, id, reporterUserId, actorType, "created",
                            $"Ticket created with {severity} severity and {priority} priority.", createdAt)
                    };
                    if (assignedActivityId is not null)
                    {
                        responseActivity.Add(new BugActivityDto(assignedActivityId, id, reporterUserId, actorType, "assigned",
                            $"Ticket assigned to {assigneeUserId}.", createdAt));
                    }

                    var ticket = new BugTicketDto(
                        id,
                        1,
                        issueTitle,
                        description,
                        bugType,
                        projectId,
                        projectName,
                        reporterUserId,
                        assigneeUserId,
                        createdAt,
                        updatedAt,
                        status,
                        severity,
                        priority,
                        tags,
                        environment,
                        expectedBehavior,
                        actualBehavior,
                        stepsToReproduce,
                        frequency,
                        null,
                        null,
                        assigneeUserId is null ? null : createdAt,
                        null,
                        null,
                        reportImages,
                        [],
                        textEvidence,
                        [],
                        responseActivity);
                    return CreateBugResult.Success(ticket);
                }
                catch (SqliteException ex) when (
                    ex.SqliteErrorCode == 19
                    && ex.Message.Contains("bug_tickets.id", StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync(token);
                    continue;
                }
            }

            throw new InvalidOperationException("Unable to create bug ticket id after retries.");
        }, ct);
    }

}
