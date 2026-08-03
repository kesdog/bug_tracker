namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    /// <summary>
    /// Lists active or closed bug tickets with optional report images.
    /// </summary>
    public async Task<IReadOnlyList<BugTicketDto>> ListBugsAsync(
        BugStatusFilter statusFilter,
        int limit,
        bool includeReportImages,
        BugListAccessScope accessScope,
        IReadOnlyList<string>? preferredProjectIds,
        string? search,
        BugListFilters? filters,
        BugCursor? cursor,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        var reportImagesSelect = includeReportImages ? "b.report_images_json" : "NULL AS report_images_json";
        var resolutionReportImagesSelect = includeReportImages ? "b.resolution_report_images_json" : "NULL AS resolution_report_images_json";
        var textEvidenceSelect = includeReportImages ? "b.text_evidence_json" : "NULL AS text_evidence_json";
        await using var command = connection.CreateCommand();
        var projectScopeClause = ApplyTicketAccessScope(command, accessScope);
        var preferredProjectCondition = BuildProjectInCondition(command, "b.project_id", preferredProjectIds, "$preferred_project_id_");
        var searchClause = ApplySearchParameter(command, search);
        var filterClause = ApplyListFilters(command, filters);
        var cursorClause = string.Empty;
        if (cursor is not null)
        {
            cursorClause = "AND (b.created_at < $cursor_created OR (b.created_at = $cursor_created AND b.id < $cursor_id))";
            command.Parameters.AddWithValue("$cursor_created", cursor.CreatedAt);
            command.Parameters.AddWithValue("$cursor_id", cursor.Id);
        }
        var orderBy = string.IsNullOrWhiteSpace(preferredProjectCondition)
            ? "ORDER BY b.created_at DESC, b.id DESC"
            : $"ORDER BY CASE WHEN {preferredProjectCondition} THEN 0 ELSE 1 END, b.created_at DESC, b.id DESC";

        var sql = statusFilter == BugStatusFilter.Active
            ? $"""
            SELECT
                b.id,
                b.issue_title,
                b.description,
                b.bug_type,
                b.project_id,
                COALESCE(p.name, b.project_id) AS project_name,
                b.reporter_user_id,
                b.assignee_user_id,
                b.created_at,
                b.updated_at,
                b.status,
                b.severity,
                b.priority,
                b.tags_json,
                b.environment,
                b.expected_behavior,
                b.actual_behavior,
                b.steps_to_reproduce,
                b.frequency,
                b.close_date,
                b.resolved_by_user_id,
                b.assigned_at,
                 b.resolution_notes,
                 b.post_resolution_report,
                 b.cancellation_reason,
                 {reportImagesSelect},
                {resolutionReportImagesSelect},
                 {textEvidenceSelect},
                 b.version
            FROM bug_tickets b
            LEFT JOIN projects p ON p.project_id = b.project_id
            WHERE b.status IN ('todo', 'open', 'reopened')
              {projectScopeClause}
              {searchClause}
              {filterClause}
              {cursorClause}
            {orderBy}
            LIMIT $limit;
            """
            : $"""
            SELECT
                b.id,
                b.issue_title,
                b.description,
                b.bug_type,
                b.project_id,
                COALESCE(p.name, b.project_id) AS project_name,
                b.reporter_user_id,
                b.assignee_user_id,
                b.created_at,
                b.updated_at,
                b.status,
                b.severity,
                b.priority,
                b.tags_json,
                b.environment,
                b.expected_behavior,
                b.actual_behavior,
                b.steps_to_reproduce,
                b.frequency,
                b.close_date,
                b.resolved_by_user_id,
                b.assigned_at,
                b.resolution_notes,
                b.post_resolution_report,
                b.cancellation_reason,
                {reportImagesSelect},
                {resolutionReportImagesSelect},
                 {textEvidenceSelect},
                 b.version
            FROM bug_tickets b
            LEFT JOIN projects p ON p.project_id = b.project_id
            WHERE b.status = 'closed'
              {projectScopeClause}
              {searchClause}
              {filterClause}
              {cursorClause}
            ORDER BY b.created_at DESC, b.id DESC
            LIMIT $limit;
            """;

        command.CommandText = sql;
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<BugTicketDto>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapBugTicket(reader, 25, 26, 27, 28, 24));
        }

        return results;
    }

    public async Task<long> CountBugsAsync(BugStatusFilter statusFilter, BugListAccessScope accessScope, string? search, BugListFilters? filters, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        var scope = ApplyTicketAccessScope(command, accessScope);
        var searchClause = ApplySearchParameter(command, search);
        var filterClause = ApplyListFilters(command, filters);
        var statusClause = statusFilter == BugStatusFilter.Active ? "b.status IN ('todo','open','reopened')" : "b.status = 'closed'";
        command.CommandText = $"""
            SELECT COUNT(*) FROM bug_tickets b
            LEFT JOIN projects p ON p.project_id = b.project_id
            WHERE {statusClause} {scope} {searchClause} {filterClause};
            """;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    public async Task<BugSummaryDto> GetSummaryAsync(BugListAccessScope accessScope, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        var scope = ApplyTicketAccessScope(command, accessScope);
        command.CommandText = $"""
            SELECT
                SUM(CASE WHEN b.status IN ('todo','open','reopened') THEN 1 ELSE 0 END),
                SUM(CASE WHEN b.status IN ('todo','open','reopened') AND b.assignee_user_id = $me THEN 1 ELSE 0 END),
                COUNT(DISTINCT b.project_id),
                SUM(CASE WHEN b.status IN ('todo','open','reopened') AND (b.severity = 'urgent' OR b.priority = 'p0') THEN 1 ELSE 0 END),
                SUM(CASE WHEN b.status IN ('todo','open','reopened') AND b.assignee_user_id IS NULL THEN 1 ELSE 0 END),
                SUM(CASE WHEN b.status = 'todo' THEN 1 ELSE 0 END),
                SUM(CASE WHEN b.status = 'open' THEN 1 ELSE 0 END),
                SUM(CASE WHEN b.status = 'reopened' THEN 1 ELSE 0 END),
                SUM(CASE WHEN b.status = 'closed' THEN 1 ELSE 0 END)
            FROM bug_tickets b LEFT JOIN projects p ON p.project_id = b.project_id
            WHERE 1=1 {scope};
            """;
        command.Parameters.AddWithValue("$me", accessScope.UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        long Value(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt64(i);
        return new BugSummaryDto(Value(0), Value(1), Value(2), Value(3), Value(4), new Dictionary<string, long>
        {
            ["todo"] = Value(5), ["open"] = Value(6), ["reopened"] = Value(7), ["closed"] = Value(8)
        });
    }

    /// <summary>
    /// Retrieves one bug ticket by id.
    /// </summary>
    public async Task<BugTicketDto?> GetBugByIdAsync(string id, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        return await GetBugByIdAsync(connection, id, ct);
    }

    /// <summary>
    /// Lists active bugs assigned to a specific user.
    /// </summary>
    public async Task<IReadOnlyList<BugTicketDto>> ListAllocatedBugsAsync(string assigneeUserId, int limit, bool includeReportImages, string? search, BugListFilters? filters, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        var reportImagesSelect = includeReportImages ? "b.report_images_json" : "NULL AS report_images_json";
        var resolutionReportImagesSelect = includeReportImages ? "b.resolution_report_images_json" : "NULL AS resolution_report_images_json";
        var textEvidenceSelect = includeReportImages ? "b.text_evidence_json" : "NULL AS text_evidence_json";
        await using var command = connection.CreateCommand();
        var searchClause = ApplySearchParameter(command, search);
        var filterClause = ApplyListFilters(command, filters);

        var sql = $"""
            SELECT
                b.id,
                b.issue_title,
                b.description,
                b.bug_type,
                b.project_id,
                COALESCE(p.name, b.project_id) AS project_name,
                b.reporter_user_id,
                b.assignee_user_id,
                b.created_at,
                b.updated_at,
                b.status,
                b.severity,
                b.priority,
                b.tags_json,
                b.environment,
                b.expected_behavior,
                b.actual_behavior,
                b.steps_to_reproduce,
                b.frequency,
                b.close_date,
                b.resolved_by_user_id,
                b.assigned_at,
                 b.resolution_notes,
                 b.post_resolution_report,
                 b.cancellation_reason,
                 {reportImagesSelect},
                {resolutionReportImagesSelect},
                {textEvidenceSelect},
                b.version
            FROM bug_tickets b
            LEFT JOIN projects p ON p.project_id = b.project_id
            WHERE b.assignee_user_id = $assignee_user_id
              AND b.status IN ('todo', 'open', 'reopened')
              {searchClause}
              {filterClause}
            ORDER BY b.created_at DESC
            LIMIT $limit;
            """;

        command.CommandText = sql;
        command.Parameters.AddWithValue("$assignee_user_id", assigneeUserId);
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<BugTicketDto>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapBugTicket(reader, 25, 26, 27, 28, 24));
        }

        return results;
    }
}
