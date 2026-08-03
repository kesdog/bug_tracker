namespace BugTracker.Api.Auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record UserProfile(string UserId, string Email, string Role, string UserType, IReadOnlyList<string> Projects, string Username);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserProfile User);

public sealed record UserRecord(string UserId, string Email, string PasswordHash, string Role, string UserType, int IsActive, IReadOnlyList<string> Projects);
public sealed record AuthenticatedUser(string UserId, string Email, string Role, string UserType, string TokenHash, DateTimeOffset TokenExpiresAt);
public sealed record AssignableUserRecord(string UserId, string Username, string Email, string Role, string UserType);
public sealed record AuthAuditUserRecord(string UserId, string UserType);
public sealed record AuthTokenAuditRecord(string UserId, string UserType, int IsActive, string? RevokedAt, DateTimeOffset ExpiresAt);
public sealed record AgentLoginRecord(string UserId, string Email, string Role, string UserType, int IsActive, IReadOnlyList<string> Projects, DateTimeOffset OathTokenExpiresAt);
public sealed record UserRoleRecord(
    string UserId,
    string Email,
    string Username,
    string Role,
    string UserType,
    int IsActive,
    IReadOnlyList<string> Projects,
    string? LastSeenAt = null,
    bool IsOnline = false,
    string PresenceStatus = "offline");
public sealed record UserRoleUpdateRequest(string Role);
public sealed record UserUsernameUpdateRequest(string Username);
public sealed record CreateHumanUserRequest(string Email);
public sealed record CreatedHumanUserResponse(string UserId, string Email, string Role, string TemporaryPassword);
public sealed record SetupPasswordRequest(string Email, string Token, string NewPassword);
public sealed record UserRequestRecord(
    string RequestId,
    string RequestType,
    string Email,
    string Username,
    string Status,
    string? UserId,
    string? ApiKeyPrefix,
    string? ApiKeyExpiresAt,
    string CreatedAt,
    string UpdatedAt,
    string Purpose = "access");
public sealed record CreateUserRequest(string Email, string RequestType);
public sealed record CreateCredentialRecoveryRequest(string Email, string RequestType);
public sealed record CredentialRecoveryRecord(
    string RecoveryId,
    string RequestType,
    string Email,
    string UserId,
    string Username,
    string Status,
    string? TokenHash,
    string? TokenExpiresAt,
    string CreatedAt,
    string UpdatedAt);
public sealed record UpdateRequestUsernameRequest(string Username);
public sealed record IssueAgentApiKeyRequest(int? ActiveDays);
public sealed record RequestActionResponse(string Message, string? Link, string? ApiKey, string? Username = null, DateTimeOffset? ExpiresAt = null);
public sealed record AgentLoginRequest(string Username, string OathToken);
