using System.Text.Json;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    /// <summary>
    /// Assigns a bug to an active user and returns the updated ticket.
    /// </summary>
    public async Task<AllocateBugResult> AllocateBugAsync(string bugId, string assigneeUserId, string actorUserId, string actorType, int? expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        var result = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(actorUserId, TicketWriteOperation.Assign, bugId, AssigneeUserId: assigneeUserId), token);
            if (!authorization.IsAllowed) return new AllocateBugResult(null, authorization.ErrorCode);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return AllocateBugResult.VersionConflict(conflict);
            var beforeTicket = await GetBugByIdAsync(connection, bugId, token);
            var projectId = await GetBugProjectIdAsync(connection, bugId, token);
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return AllocateBugResult.NotFound();
            }

            var assigneeType = await GetAssigneeUserTypeAsync(connection, assigneeUserId, token);
            if (assigneeType is null)
            {
                return AllocateBugResult.InvalidAssignee();
            }

            var visibility = await GetProjectVisibilityAsync(connection, projectId, token);
            if (visibility == "sensitive" &&
                !await IsUserAllocatedToProjectAsync(connection, assigneeUserId, projectId, token))
            {
                return AllocateBugResult.AssigneeNotProjectMember();
            }

            if (assigneeType == "agent" && !await HasHumanDeveloperOnProjectAsync(connection, projectId, token))
            {
                return AllocateBugResult.InvalidAgentProject();
            }

            const string sql = """
                UPDATE bug_tickets
                SET assignee_user_id = $assignee_user_id,
                    status = 'open',
                    assigned_at = COALESCE(assigned_at, $assigned_at),
                    updated_at = $updated_at,
                    version = version + 1
                WHERE id = $id
                  AND status IN ('todo', 'open', 'reopened')
                  AND ($expected_version IS NULL OR version = $expected_version);
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$assignee_user_id", assigneeUserId);
            command.Parameters.AddWithValue("$assigned_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("$id", bugId);
            command.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);
            var rows = await command.ExecuteNonQueryAsync(token);
            if (rows <= 0)
            {
                return AllocateBugResult.NotFound();
            }

            var version = await GetTicketVersionAsync(connection, bugId, token);
            await using (var obsolete = connection.CreateCommand())
            {
                obsolete.CommandText = """
                    UPDATE notifications SET is_read = 1
                    WHERE ticket_id = $ticket AND kind = 'ticket_assigned' AND user_id <> $assignee AND is_read = 0;
                    """;
                obsolete.Parameters.AddWithValue("$ticket", bugId);
                obsolete.Parameters.AddWithValue("$assignee", assigneeUserId);
                await obsolete.ExecuteNonQueryAsync(token);
            }
            var changedFields = new List<string>();
            if (beforeTicket?.AssigneeUserId != assigneeUserId) changedFields.Add("assigneeUserId");
            if (beforeTicket?.Status != "open") changedFields.Add("status");
            if (beforeTicket?.AssignedAt is null) changedFields.Add("assignedAt");
            await RecordMutationSideEffectsAsync(connection, bugId, version, actorUserId, actorType, "assigned",
                $"Ticket assigned to {assigneeUserId}.", "ticket_assigned", changedFields,
                "ticket_assigned", $"Ticket {bugId} was assigned to you.", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"), token, [assigneeUserId], subjectUserId: assigneeUserId);
            return new AllocateBugResult(null, null);
        }, ct);
        if (result.ErrorCode is not null || result.Conflict is not null) return result;
        var ticket = await GetBugByIdAsync(bugId, ct);
        return ticket is null ? AllocateBugResult.NotFound() : AllocateBugResult.Success(ticket);
    }

    public async Task<BulkAllocateResult> BulkAllocateBugsAsync(IReadOnlyList<BulkAllocateItem> items, string assigneeUserId, string actorUserId, string actorType, DateTimeOffset now, CancellationToken ct)
    {
        var updated = new List<BugTicketDto>();
        var failed = new List<BulkAllocateFailureDto>();
        foreach (var item in items)
        {
            var result = await AllocateBugAsync(item.TicketId, assigneeUserId, actorUserId, actorType, item.ExpectedVersion, now, ct);
            if (result.Ticket is not null)
            {
                updated.Add(result.Ticket);
                continue;
            }
            if (result.ErrorCode == "invalid_assignee")
            {
                return BulkAllocateResult.InvalidAssignee();
            }
            var errorCode = result.ErrorCode;
            if (errorCode == "bug_not_found" && await GetBugByIdAsync(item.TicketId, ct) is not null)
            {
                errorCode = "ticket_not_active";
            }
            failed.Add(new BulkAllocateFailureDto(item.TicketId, errorCode ?? "unable_to_allocate", result.Conflict));
        }
        return new BulkAllocateResult(updated, failed);
    }

    public async Task<TicketMutationResult<BugTicketDto>> UpdateMetadataAsync(
        string bugId,
        string issueTitle,
        string bugType,
        string projectId,
        string severity,
        string priority,
        IReadOnlyList<string> tags,
        string actorUserId,
        string actorType,
        int? expectedVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outcome = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(actorUserId, TicketWriteOperation.Manage, bugId, projectId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<string>.Failure(authorization.ErrorCode!);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return TicketMutationResult<string>.VersionConflict(conflict);
            var beforeTicket = await GetBugByIdAsync(connection, bugId, token);
            var tagsJson = JsonSerializer.Serialize(tags, JsonOptions);
            var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

            const string sql = """
                UPDATE bug_tickets
                SET issue_title = $issue_title,
                    bug_type = $bug_type,
                    project_id = $project_id,
                    severity = $severity,
                    priority = $priority,
                    tags_json = $tags_json,
                    updated_at = $updated_at,
                    version = version + 1
                WHERE id = $id
                  AND status IN ('todo', 'open', 'reopened')
                  AND ($expected_version IS NULL OR version = $expected_version);
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$issue_title", issueTitle);
            command.Parameters.AddWithValue("$bug_type", bugType);
            command.Parameters.AddWithValue("$project_id", projectId);
            command.Parameters.AddWithValue("$severity", severity);
            command.Parameters.AddWithValue("$priority", priority);
            command.Parameters.AddWithValue("$tags_json", tagsJson);
            command.Parameters.AddWithValue("$updated_at", nowText);
            command.Parameters.AddWithValue("$id", bugId);
            command.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);

            var rows = await command.ExecuteNonQueryAsync(token);
            if (rows <= 0)
            {
                return TicketMutationResult<string>.Failure("bug_not_found_or_closed");
            }
            var version = await GetTicketVersionAsync(connection, bugId, token);
            var changedFields = new List<string>();
            if (beforeTicket?.IssueTitle != issueTitle) changedFields.Add("issueTitle");
            if (beforeTicket?.BugType != bugType) changedFields.Add("bugType");
            if (beforeTicket?.ProjectId != projectId) changedFields.Add("projectId");
            if (beforeTicket?.Severity != severity) changedFields.Add("severity");
            if (beforeTicket?.Priority != priority) changedFields.Add("priority");
            if (beforeTicket is null || !beforeTicket.Tags.SequenceEqual(tags)) changedFields.Add("tags");
            await RecordMutationSideEffectsAsync(connection, bugId, version, actorUserId, actorType, "edited", "Ticket metadata updated.",
                "ticket_metadata_updated", changedFields, "ticket_metadata_updated",
                $"Ticket {bugId} metadata was updated.", nowText, token);
            return TicketMutationResult<string>.Success(bugId);
        }, ct);
        if (outcome.Conflict is not null) return TicketMutationResult<BugTicketDto>.VersionConflict(outcome.Conflict);
        if (outcome.Value is null) return TicketMutationResult<BugTicketDto>.Failure(outcome.ErrorCode!);
        return TicketMutationResult<BugTicketDto>.Success((await GetBugByIdAsync(bugId, ct))!);
    }

    public async Task<TicketMutationResult<BugTicketDto>> ReopenBugAsync(string bugId, string actorUserId, string actorType, string reason, int? expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        var outcome = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(actorUserId, TicketWriteOperation.Manage, bugId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<string>.Failure(authorization.ErrorCode!);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return TicketMutationResult<string>.VersionConflict(conflict);
            var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

            const string sql = """
                UPDATE bug_tickets
                SET status = 'reopened',
                    close_date = NULL,
                    resolved_by_user_id = NULL,
                    updated_at = $updated_at,
                    version = version + 1
                WHERE id = $id
                  AND status = 'closed'
                  AND ($expected_version IS NULL OR version = $expected_version);
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$updated_at", nowText);
            command.Parameters.AddWithValue("$id", bugId);
            command.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);

            var rows = await command.ExecuteNonQueryAsync(token);
            if (rows <= 0)
            {
                return TicketMutationResult<string>.Failure("ticket_not_closed");
            }
            var version = await GetTicketVersionAsync(connection, bugId, token);
            await RecordMutationSideEffectsAsync(connection, bugId, version, actorUserId, actorType, "reopened", $"Ticket reopened: {reason}",
                "ticket_reopened", ["status", "closeDate", "resolvedByUserId"], "ticket_reopened", $"Ticket {bugId} was reopened.", nowText, token);
            return TicketMutationResult<string>.Success(bugId);
        }, ct);
        if (outcome.Conflict is not null) return TicketMutationResult<BugTicketDto>.VersionConflict(outcome.Conflict);
        if (outcome.Value is null) return TicketMutationResult<BugTicketDto>.Failure(outcome.ErrorCode!);
        return TicketMutationResult<BugTicketDto>.Success((await GetBugByIdAsync(bugId, ct))!);
    }
}
