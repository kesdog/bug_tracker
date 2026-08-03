using BugTracker.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace BugTracker.Api.Projects;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects", ListProjectsAsync);
        app.MapPost("/api/projects", CreateProjectAsync);
        app.MapPatch("/api/projects/{projectId}/visibility", UpdateProjectVisibilityAsync);
        app.MapPatch("/api/projects/{projectId}/owner", TransferProjectOwnerAsync);
        app.MapGet("/api/projects/allocations", ListProjectAllocationsAsync);
        app.MapPatch("/api/projects/{projectId}/allocations", ReplaceProjectAllocationsAsync);
        app.MapGet("/api/projects/allocatable-users", ListAllocatableUsersAsync);
        app.MapGet("/api/projects/access-requests", ListAccessRequestsAsync);
        app.MapPatch("/api/projects/access-requests/{requestId}", ReviewAccessRequestAsync);
        return app;
    }

    private static async Task<IResult> ListProjectsAsync(HttpContext context, ProjectRepository repository, CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var projects = await repository.ListProjectsAsync(principal.UserId, principal.Role, ct);
        return Results.Ok(projects);
    }

    private static async Task<IResult> CreateProjectAsync(
        HttpContext context,
        [FromBody] ProjectCreateRequest request,
        ProjectRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsSeniorOrAdmin(principal.Role))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "project name is required" });
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 50)
        {
            return Results.BadRequest(new { error = "project name must be 50 characters or less" });
        }

        var visibility = string.IsNullOrWhiteSpace(request.Visibility)
            ? ProjectVisibilities.Normal
            : request.Visibility.Trim().ToLowerInvariant();
        if (!ProjectVisibilities.IsValid(visibility))
        {
            return Results.BadRequest(new { error = "visibility must be normal or sensitive" });
        }

        if (visibility == ProjectVisibilities.Sensitive &&
            (principal.Role != "admin" || principal.UserType != "human"))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var created = await repository.CreateProjectAsync(trimmedName, visibility, principal.UserId, DateTimeOffset.UtcNow, ct);
        if (created is null)
        {
            return Results.BadRequest(new { error = "project name already exists" });
        }

        return Results.Created($"/api/projects/{created.ProjectId}", created);
    }

    private static async Task<IResult> TransferProjectOwnerAsync(
        HttpContext context,
        [FromRoute] string projectId,
        [FromBody] ProjectOwnerUpdateRequest request,
        ProjectRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null) return Results.Unauthorized();
        if (principal.UserType != "human" || string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(request.OwnerUserId))
            return principal.UserType != "human" ? Results.StatusCode(StatusCodes.Status403Forbidden) : Results.BadRequest(new { error = "projectId and ownerUserId are required" });

        var project = await repository.GetProjectByIdAsync(projectId.Trim(), ct);
        if (project is null) return Results.NotFound(new { error = "project not found" });
        var allowed = principal.Role == "admin" ||
            (principal.Role == "senior" && project.Visibility == ProjectVisibilities.Normal && project.OwnerUserId == principal.UserId);
        if (!allowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var updated = await repository.TransferOwnerAsync(project.ProjectId, request.OwnerUserId.Trim(), DateTimeOffset.UtcNow, ct);
        return updated is null
            ? Results.BadRequest(new { error = "owner must be an active human senior or admin" })
            : Results.Ok(updated);
    }

    private static async Task<IResult> UpdateProjectVisibilityAsync(
        HttpContext context,
        [FromRoute] string projectId,
        [FromBody] ProjectVisibilityUpdateRequest request,
        ProjectRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (principal.Role != "admin" || principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(request.Visibility))
        {
            return Results.BadRequest(new { error = "projectId and visibility are required" });
        }

        var visibility = request.Visibility.Trim().ToLowerInvariant();
        if (!ProjectVisibilities.IsValid(visibility))
        {
            return Results.BadRequest(new { error = "visibility must be normal or sensitive" });
        }

        if (visibility == ProjectVisibilities.Sensitive &&
            await repository.HasAssigneeOutsideProjectMembershipAsync(projectId.Trim(), ct))
        {
            return Results.BadRequest(new
            {
                error = "all assigned users must be added to the project before it can become sensitive",
                errorCode = "sensitive_project_has_nonmember_assignees"
            });
        }

        var updated = await repository.UpdateProjectVisibilityAsync(projectId.Trim(), visibility, DateTimeOffset.UtcNow, ct);
        return updated is null
            ? Results.NotFound(new { error = "project not found" })
            : Results.Ok(updated);
    }

    private static async Task<IResult> ListProjectAllocationsAsync(
        HttpContext context,
        ProjectRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsSeniorOrAdmin(principal.Role) || principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var allocations = await repository.ListProjectAllocationsAsync(principal.UserId, principal.Role, ct);
        return Results.Ok(allocations);
    }

    private static async Task<IResult> ReplaceProjectAllocationsAsync(
        HttpContext context,
        [FromRoute] string projectId,
        [FromBody] ProjectAllocationRequest request,
        ProjectRepository repository,
        ProjectAuthorizationService authorizationService,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsSeniorOrAdmin(principal.Role) || principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            return Results.BadRequest(new { error = "projectId is required" });
        }

        var normalizedProjectId = projectId.Trim();
        var project = await repository.GetProjectByIdAsync(normalizedProjectId, ct);
        if (project is null)
        {
            return Results.NotFound(new { error = "project not found" });
        }

        if (!await authorizationService.CanManageProjectMembershipAsync(principal, project, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var userIds = request.UserIds ?? [];
        var replaced = await repository.ReplaceProjectAllocationsAsync(normalizedProjectId, userIds, DateTimeOffset.UtcNow, ct);
        if (!replaced.IsSuccess)
        {
            return Results.BadRequest(new { error = replaced.Error ?? "invalid project or user allocation data" });
        }

        return Results.Ok();
    }

    private static async Task<IResult> ListAllocatableUsersAsync(
        HttpContext context,
        ProjectRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsSeniorOrAdmin(principal.Role) || principal.UserType != "human")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var users = await repository.ListAllocatableUsersAsync(ct);
        return Results.Ok(users);
    }

    private static async Task<IResult> ListAccessRequestsAsync(HttpContext context, ProjectRepository repository, CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null) return Results.Unauthorized();
        if (principal.UserType != "human" || !IsSeniorOrAdmin(principal.Role)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        return Results.Ok(await repository.ListAccessRequestsAsync(principal.UserId, principal.Role, ct));
    }

    private static async Task<IResult> ReviewAccessRequestAsync(
        HttpContext context,
        [FromRoute] string requestId,
        [FromBody] ProjectAccessRequestReviewRequest request,
        ProjectRepository repository,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null) return Results.Unauthorized();
        if (principal.UserType != "human" || !IsSeniorOrAdmin(principal.Role)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("approved" or "denied")) return Results.BadRequest(new { error = "status must be approved or denied" });
        if (request.ReviewNote?.Length > 1000) return Results.BadRequest(new { error = "reviewNote must be 1000 characters or less" });
        var reviewed = await repository.ReviewAccessRequestAsync(requestId.Trim(), principal.UserId, principal.Role, status, request.ReviewNote?.Trim(), DateTimeOffset.UtcNow, ct);
        return reviewed is null ? Results.NotFound(new { error = "pending access request not found or not reviewable" }) : Results.Ok(reviewed);
    }

    private static bool IsSeniorOrAdmin(string role)
    {
        return role is "senior" or "admin";
    }

    private static AuthenticatedUser? GetPrincipal(HttpContext context)
    {
        return context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
    }
}
