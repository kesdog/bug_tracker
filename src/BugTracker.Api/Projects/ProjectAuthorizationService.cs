using BugTracker.Api.Auth;
using BugTracker.Api.Bugs;

namespace BugTracker.Api.Projects;

public sealed class ProjectAuthorizationService(ProjectRepository repository)
{
    public async Task<bool> CanAccessProjectAsync(
        AuthenticatedUser principal,
        ProjectDto project,
        CancellationToken ct)
    {
        if (principal.Role == "admin")
        {
            return true;
        }

        if (project.Visibility == ProjectVisibilities.Normal && principal.Role == "senior")
        {
            return true;
        }

        return await repository.IsUserAllocatedToProjectAsync(principal.UserId, project.ProjectId, ct);
    }

    public Task<bool> CanCreateTicketInProjectAsync(
        AuthenticatedUser principal,
        ProjectDto project,
        CancellationToken ct)
    {
        return CanAccessProjectAsync(principal, project, ct);
    }

    public async Task<bool> CanReadTicketAsync(
        AuthenticatedUser principal,
        BugTicketDto ticket,
        CancellationToken ct)
    {
        var project = await repository.GetProjectByIdAsync(ticket.ProjectId, ct);
        if (project is null)
        {
            return false;
        }

        if (await CanAccessProjectAsync(principal, project, ct))
        {
            return true;
        }

        return project.Visibility == ProjectVisibilities.Normal && IsParticipant(principal, ticket);
    }

    public async Task<bool> CanUserReadTicketAsync(string userId, BugTicketDto ticket, CancellationToken ct)
    {
        var role = await repository.GetActiveUserRoleAsync(userId, ct);
        if (role is null)
        {
            return false;
        }

        var project = await repository.GetProjectByIdAsync(ticket.ProjectId, ct);
        if (project is null)
        {
            return false;
        }

        if (role == "admin")
        {
            return true;
        }

        if (await repository.IsUserAllocatedToProjectAsync(userId, project.ProjectId, ct))
        {
            return true;
        }

        return project.Visibility == ProjectVisibilities.Normal &&
            (role == "senior" ||
             string.Equals(ticket.ReporterUserId, userId, StringComparison.Ordinal) ||
             string.Equals(ticket.AssigneeUserId, userId, StringComparison.Ordinal));
    }

    public async Task<bool> CanManageTicketAsync(
        AuthenticatedUser principal,
        BugTicketDto ticket,
        CancellationToken ct)
    {
        if (principal.Role == "admin")
        {
            return true;
        }

        var project = await repository.GetProjectByIdAsync(ticket.ProjectId, ct);
        if (project is null)
        {
            return false;
        }

        if (principal.Role == "senior" && await CanAccessProjectAsync(principal, project, ct))
        {
            return true;
        }

        if (project.Visibility == ProjectVisibilities.Sensitive &&
            !await repository.IsUserAllocatedToProjectAsync(principal.UserId, project.ProjectId, ct))
        {
            return false;
        }

        return IsParticipant(principal, ticket);
    }

    public Task<bool> CanCloseTicketAsync(
        AuthenticatedUser principal,
        BugTicketDto ticket,
        CancellationToken ct)
    {
        return CanManageTicketAsync(principal, ticket, ct);
    }

    public async Task<bool> CanAssignTicketAsync(
        AuthenticatedUser principal,
        ProjectDto project,
        CancellationToken ct)
    {
        if (principal.UserType != "human" || principal.Role is not ("senior" or "admin"))
        {
            return false;
        }

        return await CanAccessProjectAsync(principal, project, ct);
    }

    public async Task<bool> CanManageProjectMembershipAsync(
        AuthenticatedUser principal,
        ProjectDto project,
        CancellationToken ct)
    {
        if (principal.UserType != "human")
        {
            return false;
        }

        if (principal.Role == "admin")
        {
            return true;
        }

        return principal.Role == "senior" &&
            project.Visibility == ProjectVisibilities.Normal &&
            await CanAccessProjectAsync(principal, project, ct);
    }

    private static bool IsParticipant(AuthenticatedUser principal, BugTicketDto ticket)
    {
        return string.Equals(ticket.ReporterUserId, principal.UserId, StringComparison.Ordinal) ||
            string.Equals(ticket.AssigneeUserId, principal.UserId, StringComparison.Ordinal);
    }
}
