using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    private static async Task<IResult> ListBugsAsync(
        HttpContext context,
        BugRepository repository,
        ProjectRepository projectRepository,
        [FromQuery] string? status,
        [FromQuery] int? limit,
        [FromQuery] string? sort,
        [FromQuery] string? pagination,
        [FromQuery] string? cursor,
        [FromQuery] bool? dashboard,
        [FromQuery] bool? includeReportImages,
        [FromQuery] string? search,
        [FromQuery] string? priority,
        [FromQuery] string? severity,
        [FromQuery] string? tag,
        [FromQuery] string? projectId,
        [FromQuery] string? assigneeUserId,
        [FromQuery] string? reporterUserId,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? "active"
            : status.Trim().ToLowerInvariant();

        var statusFilter = normalizedStatus switch
        {
            "active" => BugStatusFilter.Active,
            "closed" => BugStatusFilter.Closed,
            _ => (BugStatusFilter?)null
        };

        if (statusFilter is null)
        {
            return Results.BadRequest(new { error = "status must be 'active' or 'closed'" });
        }

        if (!string.IsNullOrWhiteSpace(sort) &&
            !sort.Trim().Equals("created_at_desc", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "only sort=created_at_desc is supported" });
        }

        var cursorMode = string.Equals(pagination?.Trim(), "cursor", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(pagination) && !cursorMode)
            return Results.BadRequest(new { error = "pagination must be cursor", errorCode = "invalid_pagination" });
        if (!string.IsNullOrWhiteSpace(cursor) && !cursorMode)
            return Results.BadRequest(new { error = "cursor requires pagination=cursor", errorCode = "invalid_cursor" });
        if (cursorMode && dashboard == true)
            return Results.BadRequest(new { error = "cursor pagination cannot be combined with dashboard", errorCode = "invalid_pagination_combination" });
        BugCursor? decodedCursor = null;
        if (cursorMode && !string.IsNullOrWhiteSpace(cursor) && !TryDecodeCursor(cursor, out decodedCursor))
            return Results.BadRequest(new { error = "cursor is invalid", errorCode = "invalid_cursor" });

        var resolvedLimit = limit ?? (statusFilter == BugStatusFilter.Active ? 10 : 50);
        if (resolvedLimit is <= 0 or > 100)
        {
            return Results.BadRequest(new { error = "limit must be between 1 and 100" });
        }

        var filterResult = ValidateAndBuildListFilters(priority, severity, tag, projectId, assigneeUserId, reporterUserId);
        if (!filterResult.IsValid)
        {
            return Results.BadRequest(new { error = filterResult.Error });
        }

        try
        {
            var preferredProjectIds = dashboard == true && statusFilter == BugStatusFilter.Active && principal.Role == "senior"
                ? await projectRepository.ListAllocatedProjectIdsForUserAsync(principal.UserId, ct)
                : null;

            var requestedLimit = cursorMode ? resolvedLimit + 1 : resolvedLimit;
            var scope = new BugListAccessScope(principal.UserId, principal.Role, principal.UserType);
            var tickets = await repository.ListBugsAsync(
                statusFilter.Value,
                requestedLimit,
                false,
                scope,
                preferredProjectIds,
                search,
                filterResult.Filters,
                decodedCursor,
                ct);

            if (!cursorMode) return Results.Ok(tickets.Select(ToListItem).ToList());
            var hasMore = tickets.Count > resolvedLimit;
            var page = tickets.Take(resolvedLimit).ToList();
            var nextCursor = hasMore && page.Count > 0 ? EncodeCursor(new BugCursor(page[^1].CreatedAt, page[^1].Id)) : null;
            var total = await repository.CountBugsAsync(statusFilter.Value, scope, search, filterResult.Filters, ct);
            return Results.Ok(new { items = page.Select(ToListItem).ToList(), totalCount = total, nextCursor, hasMore });
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    /// <summary>
    /// Returns active tickets allocated to the currently authenticated user.
    /// </summary>
    private static async Task<IResult> ListAllocatedBugsAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        [FromQuery] int? limit,
        [FromQuery] string? pagination,
        [FromQuery] string? cursor,
        [FromQuery] string? sort,
        [FromQuery] bool? includeReportImages,
        [FromQuery] string? search,
        [FromQuery] string? priority,
        [FromQuery] string? severity,
        [FromQuery] string? tag,
        [FromQuery] string? projectId,
        [FromQuery] string? assigneeUserId,
        [FromQuery] string? reporterUserId,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var resolvedLimit = limit ?? 50;
        if (resolvedLimit is <= 0 or > 100)
        {
            return Results.BadRequest(new { error = "limit must be between 1 and 100" });
        }

        if (!string.IsNullOrWhiteSpace(sort) && !sort.Trim().Equals("created_at_desc", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "only sort=created_at_desc is supported" });
        var cursorMode = string.Equals(pagination?.Trim(), "cursor", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(pagination) && !cursorMode)
            return Results.BadRequest(new { error = "pagination must be cursor", errorCode = "invalid_pagination" });
        BugCursor? decodedCursor = null;
        if ((!string.IsNullOrWhiteSpace(cursor) && !cursorMode) || (cursorMode && !string.IsNullOrWhiteSpace(cursor) && !TryDecodeCursor(cursor, out decodedCursor)))
            return Results.BadRequest(new { error = "cursor is invalid", errorCode = "invalid_cursor" });

        var filterResult = ValidateAndBuildListFilters(priority, severity, tag, projectId, assigneeUserId, reporterUserId);
        if (!filterResult.IsValid)
        {
            return Results.BadRequest(new { error = filterResult.Error });
        }

        if (!string.IsNullOrWhiteSpace(filterResult.Filters.AssigneeUserId) &&
            !string.Equals(filterResult.Filters.AssigneeUserId, principal.UserId, StringComparison.Ordinal))
        {
            return cursorMode
                ? Results.Ok(new { items = Array.Empty<BugTicketListItemDto>(), totalCount = 0L, nextCursor = (string?)null, hasMore = false })
                : Results.Ok(Array.Empty<BugTicketDto>());
        }

        var constrainedFilters = filterResult.Filters with { AssigneeUserId = principal.UserId };

        try
        {
            if (cursorMode)
            {
                var scope = new BugListAccessScope(principal.UserId, principal.Role, principal.UserType);
                var ticketsPage = await repository.ListBugsAsync(BugStatusFilter.Active, resolvedLimit + 1, false, scope, null, search, constrainedFilters, decodedCursor, ct);
                var hasMore = ticketsPage.Count > resolvedLimit;
                var page = ticketsPage.Take(resolvedLimit).ToList();
                var nextCursor = hasMore && page.Count > 0 ? EncodeCursor(new BugCursor(page[^1].CreatedAt, page[^1].Id)) : null;
                var total = await repository.CountBugsAsync(BugStatusFilter.Active, scope, search, constrainedFilters, ct);
                return Results.Ok(new { items = page.Select(ToListItem).ToList(), totalCount = total, nextCursor, hasMore });
            }

            var tickets = await repository.ListAllocatedBugsAsync(principal.UserId, resolvedLimit, includeReportImages ?? false, search, constrainedFilters, ct);
            var readableTickets = new List<BugTicketDto>(tickets.Count);
            foreach (var ticket in tickets)
            {
                if (await authorizationService.CanReadTicketAsync(principal, ticket, ct))
                {
                    readableTickets.Add(ticket);
                }
            }

            return Results.Ok(readableTickets.Select(ticket => ToCallerSafeTicket(principal, ticket)).ToList());
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    private static async Task<IResult> GetBugSummaryAsync(HttpContext context, BugRepository repository, ProjectRepository projectRepository, CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null) return Results.Unauthorized();
        var summary = await repository.GetSummaryAsync(new BugListAccessScope(principal.UserId, principal.Role, principal.UserType), ct);
        var visibleProjects = await projectRepository.ListProjectsAsync(principal.UserId, principal.Role, principal.UserType, ct);
        return Results.Ok(summary with { VisibleProjects = visibleProjects.Count });
    }

    private static string EncodeCursor(BugCursor cursor)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string value, out BugCursor? cursor)
    {
        cursor = null;
        try
        {
            var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            cursor = JsonSerializer.Deserialize<BugCursor>(Convert.FromBase64String(normalized));
            return cursor is not null && !string.IsNullOrWhiteSpace(cursor.CreatedAt) && !string.IsNullOrWhiteSpace(cursor.Id) && cursor.Id.Length <= 200 && cursor.CreatedAt.Length <= 40;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return false; }
    }
}
