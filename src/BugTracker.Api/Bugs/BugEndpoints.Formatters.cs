using System.Text;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    private static BugTicketListItemDto ToListItem(BugTicketDto ticket)
    {
        return new BugTicketListItemDto(
            ticket.Id,
            ticket.Version,
            ticket.IssueTitle,
            ticket.BugType,
            ticket.ProjectId,
            ticket.ProjectName,
            ticket.ReporterUserId,
            ticket.AssigneeUserId,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.Status,
            ticket.Severity,
            ticket.Priority,
            ticket.Tags,
            ticket.CloseDate,
            ticket.ResolvedByUserId,
            ticket.AssignedAt);
    }

    private static object ToExportJsonTicket(BugTicketDto ticket)
    {
        return new
        {
            ticket.Id,
            ticket.IssueTitle,
            ticket.Description,
            ticket.BugType,
            ticket.ProjectId,
            ticket.ProjectName,
            ticket.ReporterUserId,
            ticket.AssigneeUserId,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.Status,
            ticket.Severity,
            ticket.Priority,
            ticket.Tags,
            ticket.Environment,
            ticket.ExpectedBehavior,
            ticket.ActualBehavior,
            ticket.StepsToReproduce,
            ticket.Frequency,
            ticket.CloseDate,
            ticket.ResolvedByUserId,
            ticket.AssignedAt,
            ticket.ResolutionNotes,
            ticket.PostResolutionReport,
            evidence = new
            {
                reportImages = new
                {
                    count = ticket.ReportImages.Count,
                    items = ticket.ReportImages.Select(image => new { image.Name, image.ContentType }).ToList()
                },
                resolutionReportImages = new
                {
                    count = ticket.ResolutionReportImages.Count,
                    items = ticket.ResolutionReportImages.Select(image => new { image.Name, image.ContentType }).ToList()
                },
                textEvidence = new
                {
                    count = ticket.TextEvidence.Count,
                    items = ticket.TextEvidence.Select(evidence => new { evidence.Name, evidence.ContentType, length = evidence.Text.Length }).ToList()
                },
                attachments = new
                {
                    count = ticket.Attachments.Count,
                    items = ticket.Attachments.Select(attachment => new
                    {
                        attachment.Id,
                        attachment.Purpose,
                        attachment.Name,
                        attachment.ContentType,
                        attachment.Kind,
                        attachment.SizeBytes,
                        attachment.Width,
                        attachment.Height,
                        attachment.Sha256,
                        attachment.UploadedByUserId,
                        attachment.CreatedAt
                    }).ToList()
                }
            },
            activity = ticket.Activity
        };
    }

    private static string BuildTicketsCsv(IReadOnlyList<BugTicketDto> tickets)
    {
        var builder = new StringBuilder();
        builder.AppendLine("id,issue_title,bug_type,project_id,project_name,reporter_user_id,assignee_user_id,status,severity,priority,tags,created_at,updated_at,closed_at,resolved_by_user_id,assigned_at,activity_count");
        foreach (var ticket in tickets)
        {
            var values = new[]
            {
                ticket.Id,
                ticket.IssueTitle,
                ticket.BugType,
                ticket.ProjectId,
                ticket.ProjectName,
                ticket.ReporterUserId,
                ticket.AssigneeUserId ?? string.Empty,
                ticket.Status,
                ticket.Severity,
                ticket.Priority,
                string.Join(';', ticket.Tags),
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.CloseDate ?? string.Empty,
                ticket.ResolvedByUserId ?? string.Empty,
                ticket.AssignedAt ?? string.Empty,
                ticket.Activity.Count.ToString()
            };

            builder.AppendLine(string.Join(',', values.Select(EscapeCsv)));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (StartsWithSpreadsheetFormula(value))
        {
            value = "'" + value;
        }

        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}" + "\"";
    }
    private static bool StartsWithSpreadsheetFormula(string value)
    {
        if (value.Length == 0) return false;
        if (value[0] is '\t' or '\r' or '\n') return true;

        var index = 0;
        while (index < value.Length && (char.IsWhiteSpace(value[index]) || char.IsControl(value[index]))) index++;
        return index < value.Length && value[index] is '=' or '+' or '-' or '@';
    }
    private static object ToMetadataSnapshot(BugTicketDto ticket)
    {
        return new
        {
            ticket.IssueTitle,
            ticket.BugType,
            ticket.ProjectId,
            ticket.Severity,
            ticket.Priority,
            ticket.Tags
        };
    }

    private static IReadOnlyDictionary<string, object> BuildMetadataChanges(BugTicketDto before, BugTicketDto after)
    {
        var changes = new Dictionary<string, object>(StringComparer.Ordinal);
        AddChange(changes, "issueTitle", before.IssueTitle, after.IssueTitle);
        AddChange(changes, "bugType", before.BugType, after.BugType);
        AddChange(changes, "projectId", before.ProjectId, after.ProjectId);
        AddChange(changes, "severity", before.Severity, after.Severity);
        AddChange(changes, "priority", before.Priority, after.Priority);
        if (!before.Tags.SequenceEqual(after.Tags))
        {
            changes["tags"] = new { before = before.Tags, after = after.Tags };
        }

        return changes;
    }

    private static void AddChange(Dictionary<string, object> changes, string field, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes[field] = new { before, after };
        }
    }
}
