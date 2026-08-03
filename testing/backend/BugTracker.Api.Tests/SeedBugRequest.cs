namespace BugTracker.Api.Tests;

public sealed record SeedBugRequest(
    string Id,
    string IssueTitle,
    string Description,
    string BugType,
    string Status,
    string Severity,
    string CreatedAt,
    string UpdatedAt,
    string? CloseDate,
    string? AssigneeUserId,
    string? ReporterUserId = null,
    string? ProjectId = null,
    string? AssignedAt = null,
    string? ResolvedByUserId = null,
    string? Priority = null,
    IReadOnlyList<string>? Tags = null);
