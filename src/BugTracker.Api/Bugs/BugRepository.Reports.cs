using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    /// <summary>
    /// Updates the submitted bug report text and image payload.
    /// </summary>
    public async Task<TicketMutationResult<BugTicketDto>> UpdateInitialBugReportAsync(
        string bugId,
        string reportText,
        IReadOnlyList<ReportImageDto> reportImages,
        string actorUserId,
        string actorType,
        int? expectedVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outcome = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(actorUserId, TicketWriteOperation.Manage, bugId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<string>.Failure(authorization.ErrorCode!);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return TicketMutationResult<string>.VersionConflict(conflict);
            if (await GetMultipartImageCountAsync(connection, bugId, initialReport: true, token) + reportImages.Count > BugReportLimits.MaxImagesPerReport)
                return TicketMutationResult<string>.Failure("image_limit_exceeded");
            if (await GetMultipartImageSizeAsync(connection, bugId, initialReport: true, token) + GetEmbeddedImageSize(reportImages) > BugReportLimits.MaxImageAggregateDecodedBytes)
                return TicketMutationResult<string>.Failure("image_size_limit_exceeded");
            var reportImagesJson = reportImages.Count > 0
                ? JsonSerializer.Serialize(reportImages, JsonOptions)
                : null;

            const string sql = """
                UPDATE bug_tickets
                SET description = $description,
                    report_images_json = $report_images_json,
                    updated_at = $updated_at,
                    version = version + 1
                WHERE id = $id AND status <> 'closed'
                  AND ($expected_version IS NULL OR version = $expected_version);
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$description", reportText);
            command.Parameters.AddWithValue("$report_images_json", (object?)reportImagesJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("$id", bugId);
            command.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);

            var rows = await command.ExecuteNonQueryAsync(token);
            if (rows <= 0)
            {
                return TicketMutationResult<string>.Failure("bug_not_found_or_closed");
            }
            var version = await GetTicketVersionAsync(connection, bugId, token);
            await RecordMutationSideEffectsAsync(connection, bugId, version, actorUserId, actorType, "edited",
                "Initial bug report updated.", "ticket_edited", ["description", "reportImages"], "ticket_edited",
                $"Ticket {bugId} was edited.", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"), token);
            return TicketMutationResult<string>.Success(bugId);
        }, ct);
        if (outcome.Conflict is not null) return TicketMutationResult<BugTicketDto>.VersionConflict(outcome.Conflict);
        if (outcome.Value is null) return TicketMutationResult<BugTicketDto>.Failure(outcome.ErrorCode!);
        return TicketMutationResult<BugTicketDto>.Success((await GetBugByIdAsync(bugId, ct))!);
    }

    /// <summary>
    /// Updates report text and image payload for an existing ticket.
    /// </summary>
    public async Task<TicketMutationResult<BugTicketDto>> UpdateBugReportAsync(
        string bugId,
        string reportText,
        IReadOnlyList<ReportImageDto> reportImages,
        string actorUserId,
        string actorType,
        int? expectedVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outcome = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(actorUserId, TicketWriteOperation.Manage, bugId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<string>.Failure(authorization.ErrorCode!);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return TicketMutationResult<string>.VersionConflict(conflict);
            if (await GetMultipartImageCountAsync(connection, bugId, initialReport: false, token) + reportImages.Count > BugReportLimits.MaxImagesPerReport)
                return TicketMutationResult<string>.Failure("image_limit_exceeded");
            if (await GetMultipartImageSizeAsync(connection, bugId, initialReport: false, token) + GetEmbeddedImageSize(reportImages) > BugReportLimits.MaxImageAggregateDecodedBytes)
                return TicketMutationResult<string>.Failure("image_size_limit_exceeded");
            var reportImagesJson = reportImages.Count > 0
                ? JsonSerializer.Serialize(reportImages, JsonOptions)
                : null;

            const string sql = """
                UPDATE bug_tickets
                SET post_resolution_report = $post_resolution_report,
                    resolution_report_images_json = $resolution_report_images_json,
                    updated_at = $updated_at,
                    version = version + 1
                WHERE id = $id AND ($expected_version IS NULL OR version = $expected_version);
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$post_resolution_report", reportText);
            command.Parameters.AddWithValue("$resolution_report_images_json", (object?)reportImagesJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_at", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("$id", bugId);
            command.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);

            var rows = await command.ExecuteNonQueryAsync(token);
            if (rows <= 0)
            {
                return TicketMutationResult<string>.Failure("bug_not_found");
            }
            var version = await GetTicketVersionAsync(connection, bugId, token);
            await RecordMutationSideEffectsAsync(connection, bugId, version, actorUserId, actorType, "edited",
                "Solution report updated.", "ticket_edited", ["postResolutionReport", "resolutionReportImages"], "ticket_edited",
                $"Ticket {bugId} was edited.", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"), token);
            return TicketMutationResult<string>.Success(bugId);
        }, ct);
        if (outcome.Conflict is not null) return TicketMutationResult<BugTicketDto>.VersionConflict(outcome.Conflict);
        if (outcome.Value is null) return TicketMutationResult<BugTicketDto>.Failure(outcome.ErrorCode!);
        return TicketMutationResult<BugTicketDto>.Success((await GetBugByIdAsync(bugId, ct))!);
    }

    /// <summary>
    /// Marks a ticket closed and persists resolution report metadata.
    /// </summary>
    public async Task<TicketMutationResult<BugTicketDto>> CloseBugAsync(
        string bugId,
        string resolvedByUserId,
        string actorType,
        string resolutionNotes,
        IReadOnlyList<ReportImageDto> reportImages,
        int? expectedVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outcome = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(resolvedByUserId, TicketWriteOperation.Manage, bugId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<string>.Failure(authorization.ErrorCode!);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return TicketMutationResult<string>.VersionConflict(conflict);
            if (await GetMultipartImageCountAsync(connection, bugId, initialReport: false, token) + reportImages.Count > BugReportLimits.MaxImagesPerReport)
                return TicketMutationResult<string>.Failure("image_limit_exceeded");
            if (await GetMultipartImageSizeAsync(connection, bugId, initialReport: false, token) + GetEmbeddedImageSize(reportImages) > BugReportLimits.MaxImageAggregateDecodedBytes)
                return TicketMutationResult<string>.Failure("image_size_limit_exceeded");
            var reportImagesJson = reportImages.Count > 0
                ? JsonSerializer.Serialize(reportImages, JsonOptions)
                : null;
            var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

            const string sql = """
                UPDATE bug_tickets
                SET status = 'closed',
                    close_date = $close_date,
                    resolved_by_user_id = $resolved_by_user_id,
                    resolution_notes = $resolution_notes,
                    post_resolution_report = $post_resolution_report,
                    resolution_report_images_json = $resolution_report_images_json,
                    updated_at = $updated_at,
                    version = version + 1
                WHERE id = $id AND status <> 'closed'
                  AND ($expected_version IS NULL OR version = $expected_version);
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$close_date", nowText);
            command.Parameters.AddWithValue("$resolved_by_user_id", resolvedByUserId);
            command.Parameters.AddWithValue("$resolution_notes", resolutionNotes);
            command.Parameters.AddWithValue("$post_resolution_report", resolutionNotes);
            command.Parameters.AddWithValue("$resolution_report_images_json", (object?)reportImagesJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_at", nowText);
            command.Parameters.AddWithValue("$id", bugId);
            command.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);

            var rows = await command.ExecuteNonQueryAsync(token);
            if (rows <= 0)
            {
                return TicketMutationResult<string>.Failure("bug_not_found_or_closed");
            }
            var version = await GetTicketVersionAsync(connection, bugId, token);
            await RecordMutationSideEffectsAsync(connection, bugId, version, resolvedByUserId, actorType, "closed",
                "Ticket closed with resolution notes.", "ticket_closed", ["status", "closeDate", "resolvedByUserId", "resolutionNotes", "postResolutionReport", "resolutionReportImages"],
                "ticket_closed", $"Ticket {bugId} was closed.", nowText, token);
            return TicketMutationResult<string>.Success(bugId);
        }, ct);
        if (outcome.Conflict is not null) return TicketMutationResult<BugTicketDto>.VersionConflict(outcome.Conflict);
        if (outcome.Value is null) return TicketMutationResult<BugTicketDto>.Failure(outcome.ErrorCode!);
        return TicketMutationResult<BugTicketDto>.Success((await GetBugByIdAsync(bugId, ct))!);
    }

    public async Task<TicketMutationResult<BugTicketDto>> CancelBugAsync(
        string bugId,
        string cancelledByUserId,
        string actorType,
        string reason,
        int? expectedVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outcome = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection, new(cancelledByUserId, TicketWriteOperation.Manage, bugId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<string>.Failure(authorization.ErrorCode!);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return TicketMutationResult<string>.VersionConflict(conflict);
            var nowText = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            const string sql = """
                UPDATE bug_tickets
                SET status = 'closed', close_date = $close_date, resolved_by_user_id = $resolved_by_user_id,
                    resolution_notes = NULL, post_resolution_report = NULL, resolution_report_images_json = NULL,
                    cancellation_reason = $cancellation_reason, updated_at = $updated_at, version = version + 1
                WHERE id = $id AND status <> 'closed'
                  AND trim(COALESCE(resolution_notes, '')) = ''
                  AND trim(COALESCE(post_resolution_report, '')) = ''
                  AND COALESCE(json_array_length(resolution_report_images_json), 0) = 0
                  AND ($expected_version IS NULL OR version = $expected_version);
                """;
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$close_date", nowText);
            command.Parameters.AddWithValue("$resolved_by_user_id", cancelledByUserId);
            command.Parameters.AddWithValue("$cancellation_reason", reason);
            command.Parameters.AddWithValue("$updated_at", nowText);
            command.Parameters.AddWithValue("$id", bugId);
            command.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);
            if (await command.ExecuteNonQueryAsync(token) != 1)
            {
                return TicketMutationResult<string>.Failure("solution_exists");
            }

            var version = await GetTicketVersionAsync(connection, bugId, token);
            await RecordMutationSideEffectsAsync(connection, bugId, version, cancelledByUserId, actorType, "closed",
                $"Ticket cancelled: {reason}", "ticket_closed", ["status", "closeDate", "resolvedByUserId", "cancellationReason"],
                "ticket_closed", $"Ticket {bugId} was cancelled.", nowText, token);
            return TicketMutationResult<string>.Success(bugId);
        }, ct);
        if (outcome.Conflict is not null) return TicketMutationResult<BugTicketDto>.VersionConflict(outcome.Conflict);
        if (outcome.Value is null) return TicketMutationResult<BugTicketDto>.Failure(outcome.ErrorCode!);
        return TicketMutationResult<BugTicketDto>.Success((await GetBugByIdAsync(bugId, ct))!);
    }

    public async Task<TicketMutationResult<BugActivityDto>> AddCommentAsync(
        string bugId,
        string actorUserId,
        string actorType,
        string body,
        string? recipientUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        return await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(actorUserId, TicketWriteOperation.Read, bugId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<BugActivityDto>.Failure(authorization.ErrorCode!);

            var createdAt = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            var version = await GetTicketVersionAsync(connection, bugId, token);
            var activityId = await RecordMutationSideEffectsAsync(connection, bugId, version, actorUserId, actorType,
                "comment", body, "ticket_commented", [], "ticket_commented", $"Ticket {bugId} has a new comment.", createdAt, token,
                recipientUserId is null ? null : [recipientUserId], subjectUserId: recipientUserId);
            return TicketMutationResult<BugActivityDto>.Success(
                new BugActivityDto(activityId, bugId, actorUserId, actorType, "comment", body, createdAt, SubjectUserId: recipientUserId));
        }, ct);
    }

}
