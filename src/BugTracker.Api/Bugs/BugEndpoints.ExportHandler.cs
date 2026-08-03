using BugTracker.Api.Audit;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    /// <summary>
    /// Returns compact active or closed bug list items for index/table views.
    /// </summary>
    private static async Task<IResult> ExportBugsAsync(
        [FromBody] ExportBugsRequest request,
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        NotificationRepository notificationRepository,
        AuditLogger auditLogger,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (!IsSeniorOrAdmin(principal.Role) || principal.UserType == "agent")
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var format = string.IsNullOrWhiteSpace(request.Format)
            ? string.Empty
            : request.Format.Trim().ToLowerInvariant();
        if (format is not ("json" or "csv"))
        {
            return Results.BadRequest(new { error = "format must be json or csv" });
        }

        if (request.TicketIds is null || request.TicketIds.Count == 0)
        {
            return Results.BadRequest(new { error = "ticketIds is required" });
        }

        var ticketIds = request.TicketIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ticketIds.Count == 0 || ticketIds.Count > 100)
        {
            return Results.BadRequest(new { error = "ticketIds must contain between 1 and 100 ids" });
        }

        var tickets = new List<BugTicketDto>(ticketIds.Count);
        foreach (var ticketId in ticketIds)
        {
            var ticket = await repository.GetBugByIdAsync(ticketId, ct);
            if (ticket is null)
            {
                return Results.NotFound(new { error = $"ticket not found: {ticketId}" });
            }

            if (!await authorizationService.CanReadTicketAsync(principal, ticket, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            tickets.Add(ticket);
        }

        await auditLogger.LogAsync(
            principal,
            "tickets_exported",
            $"Exported {tickets.Count} ticket(s) as {format}.",
            null,
            new { format, ticketIds },
            ct);

        var timestamp = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMddHHmmss");
        if (format == "csv")
        {
            var csv = BuildTicketsCsv(tickets);
            return Results.File(
                Encoding.UTF8.GetBytes(csv),
                "text/csv; charset=utf-8",
                $"bug-tickets-{timestamp}.csv");
        }

        var payload = new
        {
            exportedAt = DateTimeOffset.UtcNow,
            exportedBy = principal.UserId,
            count = tickets.Count,
            tickets = tickets.Select(ToExportJsonTicket).ToList()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return Results.File(bytes, "application/json", $"bug-tickets-{timestamp}.json");
    }
}
