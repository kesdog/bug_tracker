namespace BugTracker.Api.Projects;

public static class ProjectVisibilities
{
    public const string Normal = "normal";
    public const string Sensitive = "sensitive";

    public static bool IsValid(string visibility)
    {
        return visibility is Normal or Sensitive;
    }
}

public sealed record ProjectDto(
    string ProjectId,
    string Name,
    string Visibility,
    string CreatedAt,
    string UpdatedAt,
    string? OwnerUserId = null,
    string? OwnerUsername = null,
    string? OwnerRole = null);

public sealed record ProjectCreateRequest(string Name, string? Visibility = null);

public sealed record ProjectVisibilityUpdateRequest(string Visibility);
public sealed record ProjectOwnerUpdateRequest(string OwnerUserId);

public sealed record ProjectAllocationRequest(IReadOnlyList<string> UserIds);

public sealed record ProjectAllocationDto(
    string ProjectId,
    string ProjectName,
    string Visibility,
    IReadOnlyList<string> UserIds,
    string? OwnerUserId = null,
    string? OwnerUsername = null,
    string? OwnerRole = null);

public sealed record ProjectUserDto(string UserId, string Email, string Username, string Role, string UserType);

public sealed record ProjectAllocationUpdateResult(bool IsSuccess, string? Error)
{
    public static ProjectAllocationUpdateResult Success() => new(true, null);
    public static ProjectAllocationUpdateResult Failure(string error) => new(false, error);
}

public sealed record ProjectAccessRequestCreateRequest(string? Reason = null);
public sealed record ProjectAccessRequestReviewRequest(string Status, string? ReviewNote = null);
public sealed record ProjectAccessRequestDto(
    string RequestId,
    string ProjectId,
    string RequesterUserId,
    string RequesterUsername,
    string RequesterRole,
    string RequesterUserType,
    string? SourceTicketId,
    string? Reason,
    string Status,
    string? ReviewedByUserId,
    string? ReviewNote,
    string CreatedAt,
    string UpdatedAt,
    string? ReviewedAt);

public sealed record SafeProjectContactDto(string UserId, string Username, string Role);
