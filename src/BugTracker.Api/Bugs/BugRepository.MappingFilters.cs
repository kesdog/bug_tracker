using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    /// <summary>
    /// Maps a database row into the API-facing bug DTO.
    /// </summary>
    private static BugTicketDto MapBugTicket(SqliteDataReader reader, int reportImagesColumnIndex, int resolutionReportImagesColumnIndex, int textEvidenceColumnIndex, int versionColumnIndex, int cancellationReasonColumnIndex)
    {
        return new BugTicketDto(
            reader.GetString(0),
            reader.GetInt32(versionColumnIndex),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(cancellationReasonColumnIndex) ? reader.GetString(10) : "cancelled",
            reader.GetString(11),
            reader.IsDBNull(12) ? "p2" : reader.GetString(12),
            ReadStringList(reader, 13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.IsDBNull(22) ? null : reader.GetString(22),
            reader.IsDBNull(23) ? null : reader.GetString(23),
            ReadReportImages(reader, reportImagesColumnIndex),
            ReadReportImages(reader, resolutionReportImagesColumnIndex),
            ReadTextEvidence(reader, textEvidenceColumnIndex),
            [],
            [],
            CancellationReason: reader.IsDBNull(cancellationReasonColumnIndex) ? null : reader.GetString(cancellationReasonColumnIndex));
    }

    private static string ApplySearchParameter(SqliteCommand command, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return string.Empty;
        }

        var normalized = search.Trim().ToLowerInvariant();
        if (normalized.Length > 80)
        {
            normalized = normalized[..80];
        }

        command.Parameters.AddWithValue("$search", $"%{normalized}%");
        return """
            AND (
                lower(b.issue_title) LIKE $search
                OR lower(b.description) LIKE $search
                OR lower(COALESCE(b.resolution_notes, '')) LIKE $search
                OR lower(COALESCE(b.post_resolution_report, '')) LIKE $search
                OR lower(b.reporter_user_id) LIKE $search
                OR lower(COALESCE(b.assignee_user_id, '')) LIKE $search
                OR lower(COALESCE(p.name, b.project_id)) LIKE $search
                OR lower(b.severity) LIKE $search
                OR lower(b.priority) LIKE $search
                OR lower(b.tags_json) LIKE $search
                OR lower(COALESCE(b.environment, '')) LIKE $search
                OR lower(COALESCE(b.expected_behavior, '')) LIKE $search
                OR lower(COALESCE(b.actual_behavior, '')) LIKE $search
                OR lower(COALESCE(b.steps_to_reproduce, '')) LIKE $search
                OR lower(COALESCE(b.frequency, '')) LIKE $search
            )
            """;
    }

    private static string ApplyListFilters(SqliteCommand command, BugListFilters? filters)
    {
        if (filters is null)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.Priority))
        {
            clauses.Add("b.priority = $filter_priority");
            command.Parameters.AddWithValue("$filter_priority", filters.Priority);
        }

        if (!string.IsNullOrWhiteSpace(filters.Severity))
        {
            clauses.Add("b.severity = $filter_severity");
            command.Parameters.AddWithValue("$filter_severity", filters.Severity);
        }

        if (!string.IsNullOrWhiteSpace(filters.Tag))
        {
            clauses.Add("lower(COALESCE(b.tags_json, '[]')) LIKE $filter_tag");
            command.Parameters.AddWithValue("$filter_tag", $"%\"{filters.Tag}\"%");
        }

        if (!string.IsNullOrWhiteSpace(filters.ProjectId))
        {
            clauses.Add("b.project_id = $filter_project_id");
            command.Parameters.AddWithValue("$filter_project_id", filters.ProjectId);
        }

        if (!string.IsNullOrWhiteSpace(filters.AssigneeUserId))
        {
            if (filters.AssigneeUserId == "unassigned")
            {
                clauses.Add("b.assignee_user_id IS NULL");
            }
            else
            {
                clauses.Add("b.assignee_user_id = $filter_assignee_user_id");
                command.Parameters.AddWithValue("$filter_assignee_user_id", filters.AssigneeUserId);
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.ReporterUserId))
        {
            clauses.Add("b.reporter_user_id = $filter_reporter_user_id");
            command.Parameters.AddWithValue("$filter_reporter_user_id", filters.ReporterUserId);
        }

        return clauses.Count == 0 ? string.Empty : $"AND {string.Join(" AND ", clauses)}";
    }

    private static string ApplyProjectScopeParameters(
        SqliteCommand command,
        string projectColumnName,
        IReadOnlyList<string>? projectIds,
        string parameterPrefix = "$project_id_")
    {
        var condition = BuildProjectInCondition(command, projectColumnName, projectIds, parameterPrefix);
        if (string.IsNullOrWhiteSpace(condition))
        {
            return string.Empty;
        }

        return $"AND {condition}";
    }

    private static string ApplyTicketAccessScope(SqliteCommand command, BugListAccessScope scope)
    {
        if (scope.Role == "admin")
        {
            return string.Empty;
        }

        command.Parameters.AddWithValue("$access_user_id", scope.UserId);
        command.Parameters.AddWithValue("$access_role", scope.Role);
        return """
            AND (
                EXISTS (
                    SELECT 1
                    FROM project_allocations access_pa
                    WHERE access_pa.project_id = b.project_id
                      AND access_pa.user_id = $access_user_id
                )
                OR ($access_role = 'senior' AND COALESCE(p.visibility, 'normal') = 'normal')
                OR (
                    COALESCE(p.visibility, 'normal') = 'normal'
                    AND (b.reporter_user_id = $access_user_id OR b.assignee_user_id = $access_user_id)
                )
            )
            """;
    }

    private static string BuildProjectInCondition(
        SqliteCommand command,
        string projectColumnName,
        IReadOnlyList<string>? projectIds,
        string parameterPrefix)
    {
        if (projectIds is null || projectIds.Count == 0)
        {
            return string.Empty;
        }

        var parameterNames = new List<string>(projectIds.Count);
        for (var i = 0; i < projectIds.Count; i++)
        {
            var parameterName = $"{parameterPrefix}{i}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, projectIds[i]);
        }

        return $"{projectColumnName} IN ({string.Join(", ", parameterNames)})";
    }

    /// <summary>
    /// Deserializes stored report image JSON safely.
    /// </summary>
    private static IReadOnlyList<ReportImageDto> ReadReportImages(SqliteDataReader reader, int columnIndex)
    {
        if (reader.IsDBNull(columnIndex))
        {
            return [];
        }

        var raw = reader.GetString(columnIndex);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var images = JsonSerializer.Deserialize<List<ReportImageDto>>(raw, JsonOptions);
            return images is null ? [] : images;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadStringList(SqliteDataReader reader, int columnIndex)
    {
        if (reader.IsDBNull(columnIndex))
        {
            return [];
        }

        var raw = reader.GetString(columnIndex);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(raw, JsonOptions);
            return values is null ? [] : values;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<TextEvidenceDto> ReadTextEvidence(SqliteDataReader reader, int columnIndex)
    {
        if (reader.IsDBNull(columnIndex))
        {
            return [];
        }

        var raw = reader.GetString(columnIndex);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var evidence = JsonSerializer.Deserialize<List<TextEvidenceDto>>(raw, JsonOptions);
            return evidence is null ? [] : evidence;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
