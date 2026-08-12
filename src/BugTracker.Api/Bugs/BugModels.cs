namespace BugTracker.Api.Bugs;

public sealed record CreateBugRequest(
    string IssueTitle,
    string Description,
    string BugType,
    string ProjectId,
    string Severity,
    string? Priority,
    IReadOnlyList<string>? Tags,
    string? Environment,
    string? ExpectedBehavior,
    string? ActualBehavior,
    string? StepsToReproduce,
    string? Frequency,
    IReadOnlyList<TextEvidenceInput>? TextEvidence,
    IReadOnlyList<ReportImageInput>? ReportImages,
    string? AssigneeUserId = null);

public sealed record ReportImageInput(string Name, string ContentType, string DataUrl);
public sealed record TextEvidenceInput(string Name, string ContentType, string Text);

public sealed record ReportImageDto(string Name, string ContentType, string DataUrl);
public sealed record TextEvidenceDto(string Name, string ContentType, string Text);
public sealed record TicketAttachmentDto(
    string Id,
    string TicketId,
    string Purpose,
    string Name,
    string ContentType,
    string Kind,
    long SizeBytes,
    int? Width,
    int? Height,
    string Sha256,
    string UploadedByUserId,
    string CreatedAt);

public sealed record TicketAttachmentUpload(
    string Purpose,
    string Name,
    string ContentType,
    string Kind,
    long SizeBytes,
    int? Width,
    int? Height,
    string Sha256,
    byte[] Content);

public sealed record TicketAttachmentContentDto(
    TicketAttachmentDto Metadata,
    byte[] Content);

public sealed record TicketAttachmentsMutationDto(
    IReadOnlyList<TicketAttachmentDto> Attachments,
    int Version);

public sealed record AllocateBugRequest(string AssigneeUserId, int? ExpectedVersion = null);
public sealed record BulkAllocateItem(string TicketId, int? ExpectedVersion);
public sealed record BulkAllocateBugRequest(IReadOnlyList<string>? TicketIds, string AssigneeUserId, IReadOnlyList<BulkAllocateItem>? Items = null);
public sealed record UpdateBugMetadataRequest(
    string? IssueTitle,
    string? BugType,
    string? ProjectId,
    string? Severity,
    string? Priority,
    IReadOnlyList<string>? Tags,
    int? ExpectedVersion = null);
public sealed record ReopenBugRequest(string Reason, int? ExpectedVersion = null);
public sealed record UpdateInitialBugReportRequest(string ReportText, IReadOnlyList<ReportImageInput>? ReportImages, int? ExpectedVersion = null);
public sealed record UpdateBugReportRequest(string ReportText, IReadOnlyList<ReportImageInput>? ReportImages, int? ExpectedVersion = null);
public sealed record CloseBugRequest(string ResolutionNotes, IReadOnlyList<ReportImageInput>? ReportImages, int? ExpectedVersion = null);
public sealed record CancelBugRequest(string Reason, int? ExpectedVersion = null);
public sealed record AddBugCommentRequest(string Body, string? RecipientUserId = null);
public sealed record ExportBugsRequest(string Format, IReadOnlyList<string>? TicketIds);

public sealed record BugActivityDto(
    string Id,
    string TicketId,
    string ActorUserId,
    string ActorType,
    string Kind,
    string Body,
    string CreatedAt,
    string? EventId = null,
    int? TicketVersion = null,
    IReadOnlyList<string>? ChangedFields = null,
    UserIdentityDto? Actor = null,
    string? SubjectUserId = null,
    UserIdentityDto? Subject = null);

public sealed record UserIdentityDto(string UserId, string Username, string Role, string UserType, string? Email = null);
public sealed record TicketContactDto(string UserId, string Username, string Role, string UserType, string? Email, IReadOnlyList<string> Kinds);

public sealed record AssignableUserDto(string UserId, string Username, string Email, string Role, string UserType);

public sealed record BugListAccessScope(string UserId, string Role, string UserType);
public sealed record BugCursor(string CreatedAt, string Id);
public sealed record BugSummaryDto(
    long ActiveTotal,
    long AllocatedToMe,
    long VisibleProjects,
    long UrgentActive,
    long UnassignedActive,
    IReadOnlyDictionary<string, long> StatusCounts);

public sealed record CreateBugResult(BugTicketDto? Ticket, string? ErrorCode)
{
    public static CreateBugResult Success(BugTicketDto ticket) => new(ticket, null);
    public static CreateBugResult InvalidAssignee() => new(null, "invalid_assignee");
    public static CreateBugResult AssigneeNotProjectMember() => new(null, "assignee_not_project_member");
    public static CreateBugResult InvalidAgentProject() => new(null, "invalid_assignee_for_project");
}

public sealed record AllocateBugResult(BugTicketDto? Ticket, string? ErrorCode, TicketVersionConflict? Conflict = null)
{
    public static AllocateBugResult Success(BugTicketDto ticket) => new(ticket, null);
    public static AllocateBugResult NotFound() => new(null, "bug_not_found");
    public static AllocateBugResult InvalidAssignee() => new(null, "invalid_assignee");
    public static AllocateBugResult AssigneeNotProjectMember() => new(null, "assignee_not_project_member");
    public static AllocateBugResult InvalidAgentProject() => new(null, "invalid_assignee_for_project");
    public static AllocateBugResult VersionConflict(TicketVersionConflict conflict) => new(null, "ticket_version_conflict", conflict);
}

public sealed record BulkAllocateFailureDto(string TicketId, string Error, TicketVersionConflict? Conflict = null);
public sealed record BulkAllocateResult(IReadOnlyList<BugTicketDto> Updated, IReadOnlyList<BulkAllocateFailureDto> Failed, string? ErrorCode = null)
{
    public static BulkAllocateResult InvalidAssignee() => new([], [], "invalid_assignee");
}

public sealed record BugListFilters(
    string? Priority,
    string? Severity,
    string? Tag,
    string? ProjectId,
    string? AssigneeUserId,
    string? ReporterUserId);

public sealed record BugTicketDto(
    string Id,
    int Version,
    string IssueTitle,
    string Description,
    string BugType,
    string ProjectId,
    string ProjectName,
    string ReporterUserId,
    string? AssigneeUserId,
    string CreatedAt,
    string UpdatedAt,
    string Status,
    string Severity,
    string Priority,
    IReadOnlyList<string> Tags,
    string? Environment,
    string? ExpectedBehavior,
    string? ActualBehavior,
    string? StepsToReproduce,
    string? Frequency,
    string? CloseDate,
    string? ResolvedByUserId,
    string? AssignedAt,
    string? ResolutionNotes,
    string? PostResolutionReport,
    IReadOnlyList<ReportImageDto> ReportImages,
    IReadOnlyList<ReportImageDto> ResolutionReportImages,
    IReadOnlyList<TextEvidenceDto> TextEvidence,
    IReadOnlyList<TicketAttachmentDto> Attachments,
    IReadOnlyList<BugActivityDto> Activity,
    UserIdentityDto? Reporter = null,
    UserIdentityDto? Assignee = null,
    UserIdentityDto? Resolver = null,
    IReadOnlyList<TicketContactDto>? Contacts = null,
    string? CancellationReason = null);

public sealed record BugTicketListItemDto(
    string Id,
    int Version,
    string IssueTitle,
    string BugType,
    string ProjectId,
    string ProjectName,
    string ReporterUserId,
    string? AssigneeUserId,
    string CreatedAt,
    string UpdatedAt,
    string Status,
    string Severity,
    string Priority,
    IReadOnlyList<string> Tags,
    string? CloseDate,
    string? ResolvedByUserId,
    string? AssignedAt);

public sealed record TicketVersionConflict(
    string TicketId,
    int ExpectedVersion,
    int CurrentVersion,
    string CurrentStatus,
    IReadOnlyList<string> ChangedFields,
    string Recovery = "Refetch the ticket, merge your changes, and retry with the current version.");

public sealed record TicketMutationResult<T>(T? Value, TicketVersionConflict? Conflict, string? ErrorCode = null)
{
    public static TicketMutationResult<T> Success(T value) => new(value, null);
    public static TicketMutationResult<T> VersionConflict(TicketVersionConflict conflict) => new(default, conflict, "ticket_version_conflict");
    public static TicketMutationResult<T> Failure(string errorCode) => new(default, null, errorCode);
}

public enum BugStatusFilter
{
    Active,
    Closed
}
