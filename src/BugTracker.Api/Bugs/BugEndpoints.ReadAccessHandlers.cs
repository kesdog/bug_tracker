using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    /// <summary>
    /// Fetches one bug ticket by id for details/report views.
    /// </summary>
    private static async Task<IResult> GetBugByIdAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        ProjectRepository projectRepository,
        AuditLogger auditLogger,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { error = "id is required" });
        }

        try
        {
            var ticket = await repository.GetBugByIdAsync(id.Trim(), ct);
            if (ticket is null)
            {
                return Results.NotFound(new { error = "ticket not found" });
            }

            if (!await authorizationService.CanReadTicketAsync(principal, ticket, ct))
            {
                return await TicketAccessDeniedAsync(ticket, projectRepository, ct);
            }

            await auditLogger.LogAsync(
                principal,
                "ticket_viewed",
                $"Ticket {ticket.Id} viewed.",
                ticket.Id,
                new { ticket.ProjectId, ticket.Status },
                ct);

            return Results.Ok(ToCallerSafeTicket(principal, ticket));
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    private static BugTicketDto RemoveEmails(BugTicketDto ticket)
    {
        UserIdentityDto? Safe(UserIdentityDto? identity) => identity is null ? null : identity with { Email = null };
        return ticket with
        {
            Reporter = Safe(ticket.Reporter), Assignee = Safe(ticket.Assignee), Resolver = Safe(ticket.Resolver),
            Contacts = ticket.Contacts?.Select(x => x with { Email = null }).ToArray(),
            Activity = ticket.Activity.Select(x => x with { Actor = Safe(x.Actor), Subject = Safe(x.Subject) }).ToArray()
        };
    }

    private static BugTicketDto ToCallerSafeTicket(AuthenticatedUser principal, BugTicketDto ticket)
    {
        return principal.UserType == "agent" ? RemoveEmails(ticket) : ticket;
    }

    private static async Task<IResult> TicketAccessDeniedAsync(BugTicketDto ticket, ProjectRepository projectRepository, CancellationToken ct)
    {
        var contacts = await projectRepository.ListSafeReviewContactsAsync(ticket.ProjectId, ct);
        return Results.Json(new
        {
            error = "Ticket access denied.",
            errorCode = "ticket_access_denied",
            reason = "project_membership_required",
            message = "Request project membership from an owner or reviewer, then retry the canonical ticket detail endpoint.",
            steps = new[] { "POST the access request path.", "Wait for an authorized human reviewer to approve it.", "Retry GET /api/bugs/{id}." },
            contacts,
            requestAccessPath = $"/api/bugs/{ticket.Id}/access-request"
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> RequestTicketAccessAsync(
        HttpContext context,
        BugRepository repository,
        ProjectRepository projectRepository,
        ProjectAuthorizationService authorizationService,
        [FromRoute] string id,
        [FromBody] ProjectAccessRequestCreateRequest request,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(id)) return Results.BadRequest(new { error = "id is required" });
        if (request.Reason?.Length > 1000) return Results.BadRequest(new { error = "reason must be 1000 characters or less" });
        var ticket = await repository.GetBugByIdAsync(id.Trim(), ct);
        if (ticket is null) return Results.NotFound(new { error = "ticket not found" });
        if (await authorizationService.CanReadTicketAsync(principal, ticket, ct))
            return Results.BadRequest(new { error = "ticket is already accessible", errorCode = "access_already_granted" });
        var created = await projectRepository.CreateAccessRequestAsync(ticket.ProjectId, principal.UserId, ticket.Id, request.Reason?.Trim(), DateTimeOffset.UtcNow, ct);
        return created is null ? Results.BadRequest(new { error = "unable to create access request" }) : Results.Ok(created);
    }

    /// <summary>
    /// Lists assignable active users and roles; only senior/admin can call this.
    /// </summary>
    private static async Task<IResult> ListAssignableUsersAsync(
        HttpContext context,
        AuthRepository authRepository,
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

        var users = await authRepository.ListAssignableUsersAsync(ct);
        var payload = users
            .Select(user => new AssignableUserDto(user.UserId, user.Username, user.Email, user.Role, user.UserType))
            .ToList();

        return Results.Ok(payload);
    }
}
