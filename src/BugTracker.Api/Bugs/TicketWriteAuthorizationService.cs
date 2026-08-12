using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public enum TicketWriteOperation
{
    Create,
    Read,
    Manage,
    Assign
}

public sealed record TicketWriteAuthorizationInput(
    string ActorUserId,
    TicketWriteOperation Operation,
    string? TicketId = null,
    string? TargetProjectId = null,
    string? AssigneeUserId = null);

public sealed record TicketWriteAuthorizationResult(bool IsAllowed, string? ErrorCode = null)
{
    public static TicketWriteAuthorizationResult Allowed() => new(true);
    public static TicketWriteAuthorizationResult Denied(string errorCode) => new(false, errorCode);
}

/// <summary>
/// Re-reads all mutable authorization state on the caller's write connection. Callers invoke this
/// only after BEGIN IMMEDIATE, so membership, role, project and assignee checks cannot become stale
/// before the ticket mutation commits.
/// </summary>
public sealed class TicketWriteAuthorizationService
{
    public async Task<TicketWriteAuthorizationResult> AuthorizeAsync(
        SqliteConnection connection,
        TicketWriteAuthorizationInput input,
        CancellationToken ct)
    {
        var actor = await GetActiveUserAsync(connection, input.ActorUserId, ct);
        if (actor is null) return TicketWriteAuthorizationResult.Denied("forbidden");

        TicketState? ticket = null;
        if (input.Operation != TicketWriteOperation.Create)
        {
            ticket = await GetTicketAsync(connection, input.TicketId!, ct);
            if (ticket is null) return TicketWriteAuthorizationResult.Denied("bug_not_found");
        }

        var currentProjectId = ticket?.ProjectId ?? input.TargetProjectId;
        var project = await GetProjectAsync(connection, currentProjectId!, ct);
        if (project is null) return TicketWriteAuthorizationResult.Denied("project_not_found");

        var canAccessCurrentProject = (actor.UserType == "human" && actor.Role == "admin") ||
            (actor.UserType == "human" && project.Visibility == "normal" && actor.Role == "senior") ||
            await IsMemberAsync(connection, actor.UserId, project.Id, ct);
        var isParticipant = ticket is not null &&
            (ticket.ReporterUserId == actor.UserId || ticket.AssigneeUserId == actor.UserId);

        var allowed = input.Operation switch
        {
            TicketWriteOperation.Create => canAccessCurrentProject,
            TicketWriteOperation.Read => canAccessCurrentProject || (project.Visibility == "normal" && isParticipant),
            TicketWriteOperation.Manage => (actor.UserType == "human" && actor.Role == "admin") ||
                (actor.UserType == "human" && actor.Role == "senior" && canAccessCurrentProject) ||
                (isParticipant && (project.Visibility == "normal" || canAccessCurrentProject)),
            TicketWriteOperation.Assign => actor.UserType == "human" && actor.Role is "senior" or "admin" && canAccessCurrentProject,
            _ => false
        };
        if (!allowed) return TicketWriteAuthorizationResult.Denied("forbidden");

        if (input.TargetProjectId is not null && input.TargetProjectId != project.Id)
        {
            var target = await GetProjectAsync(connection, input.TargetProjectId, ct);
            if (target is null) return TicketWriteAuthorizationResult.Denied("invalid_project");
            var canAccessTarget = (actor.UserType == "human" && actor.Role == "admin") ||
                (actor.UserType == "human" && target.Visibility == "normal" && actor.Role == "senior") ||
                await IsMemberAsync(connection, actor.UserId, target.Id, ct);
            if (!canAccessTarget) return TicketWriteAuthorizationResult.Denied("forbidden");
            if (target.Visibility != project.Visibility && (actor.Role != "admin" || actor.UserType != "human"))
                return TicketWriteAuthorizationResult.Denied("forbidden");
            if (ticket?.AssigneeUserId is not null && target.Visibility == "sensitive" &&
                !await IsMemberAsync(connection, ticket.AssigneeUserId, target.Id, ct))
                return TicketWriteAuthorizationResult.Denied("assignee_not_project_member");
            project = target;
            if (ticket?.AssigneeUserId is not null)
            {
                var targetAssignee = await ValidateAssigneeAsync(connection, ticket.AssigneeUserId, project, ct);
                if (!targetAssignee.IsAllowed) return targetAssignee;
            }
        }

        if (input.AssigneeUserId is not null)
        {
            if (input.Operation == TicketWriteOperation.Create &&
                (actor.UserType != "human" || actor.Role is not ("senior" or "admin")))
                return TicketWriteAuthorizationResult.Denied("forbidden");

            var assigneeResult = await ValidateAssigneeAsync(connection, input.AssigneeUserId, project, ct);
            if (!assigneeResult.IsAllowed) return assigneeResult;
        }

        return TicketWriteAuthorizationResult.Allowed();
    }

    private static async Task<TicketWriteAuthorizationResult> ValidateAssigneeAsync(
        SqliteConnection connection,
        string assigneeUserId,
        ProjectState project,
        CancellationToken ct)
    {
        var assignee = await GetActiveUserAsync(connection, assigneeUserId, ct);
        if (assignee is null) return TicketWriteAuthorizationResult.Denied("invalid_assignee");
        if (project.Visibility == "sensitive" && !await IsMemberAsync(connection, assignee.UserId, project.Id, ct))
            return TicketWriteAuthorizationResult.Denied("assignee_not_project_member");
        if (assignee.UserType == "agent" && !await HasHumanSupervisorAsync(connection, project.Id, ct))
            return TicketWriteAuthorizationResult.Denied("invalid_assignee_for_project");
        return TicketWriteAuthorizationResult.Allowed();
    }

    private static async Task<UserState?> GetActiveUserAsync(SqliteConnection connection, string userId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id, role, COALESCE(user_type, 'human') FROM users WHERE user_id = $id AND is_active = 1;";
        command.Parameters.AddWithValue("$id", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    private static async Task<TicketState?> GetTicketAsync(SqliteConnection connection, string ticketId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT project_id, reporter_user_id, assignee_user_id FROM bug_tickets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", ticketId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    private static async Task<ProjectState?> GetProjectAsync(SqliteConnection connection, string projectId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT project_id, visibility FROM projects WHERE project_id = $id;";
        command.Parameters.AddWithValue("$id", projectId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetString(0), reader.GetString(1)) : null;
    }

    private static async Task<bool> IsMemberAsync(SqliteConnection connection, string userId, string projectId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM project_allocations WHERE user_id = $user AND project_id = $project LIMIT 1;";
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$project", projectId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task<bool> HasHumanSupervisorAsync(SqliteConnection connection, string projectId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM project_allocations pa JOIN users u ON u.user_id = pa.user_id
            WHERE pa.project_id = $project AND u.is_active = 1 AND u.role IN ('dev', 'senior')
              AND COALESCE(u.user_type, 'human') = 'human' LIMIT 1;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private sealed record UserState(string UserId, string Role, string UserType);
    private sealed record TicketState(string ProjectId, string ReporterUserId, string? AssigneeUserId);
    private sealed record ProjectState(string Id, string Visibility);
}
