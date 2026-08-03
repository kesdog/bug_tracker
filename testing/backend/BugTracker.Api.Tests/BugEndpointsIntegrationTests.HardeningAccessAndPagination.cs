using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using BugTracker.Api;
using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Bugs;
using BugTracker.Api.Database;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed partial class BugEndpointsIntegrationTests
{
    [Fact]
    public async Task CreateBug_TextBoundaries_AcceptsMaximumAndRejectsOverflowWithoutTruncation()
    {
        using var client = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        var accepted = await client.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = new string('t', BugReportLimits.TitleCharacters),
            description = new string('d', BugReportLimits.InitialReportCharacters),
            bugType = "api",
            projectId = "project-general",
            severity = "low",
            environment = new string('e', BugReportLimits.EnvironmentCharacters)
        });
        var rejected = await client.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = "Structured overflow",
            description = "Must reject instead of truncate.",
            bugType = "api",
            projectId = "project-general",
            severity = "low",
            environment = new string('e', BugReportLimits.EnvironmentCharacters + 1)
        });
        var rejectedBody = await rejected.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("environment must be 500 characters or less", rejectedBody?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBug_TextEvidenceUsesUtf8ByteLimit()
    {
        using var client = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        var response = await client.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = "UTF-8 evidence boundary",
            description = "Multibyte evidence must be measured as bytes.",
            bugType = "api",
            projectId = "project-general",
            severity = "low",
            textEvidence = new[]
            {
                new { name = "unicode.txt", contentType = "text/plain", text = new string('€', 33_334) }
            }
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text evidence file is too large", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBug_MalformedAndSpoofedEmbeddedImages_Return422()
    {
        using var client = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        async Task<HttpResponseMessage> SendAsync(string contentType, string dataUrl) => await client.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = "Rejected image",
            description = "Image validation failure.",
            bugType = "api",
            projectId = "project-general",
            severity = "low",
            reportImages = new[] { new { name = "capture.png", contentType, dataUrl } }
        });

        var malformed = await SendAsync("image/png", "data:image/png;base64,%%%%");
        var spoofed = await SendAsync("image/jpeg", TinyPngDataUrl.Replace("data:image/png", "data:image/jpeg", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, spoofed.StatusCode);
    }

    [Fact]
    public async Task RequestLimits_ReturnStructured413AndPreserveAuth4KiBLimit()
    {
        using var client = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        using var content = new ByteArrayContent(new byte[BugReportLimits.MaxApiRequestBodyBytes + 1]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/api/bugs", content);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        using var authContent = new ByteArrayContent(new byte[BugReportLimits.PublicAuthRequestBodyBytes + 1]);
        authContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var authResponse = await client.PostAsync("/api/auth/login", authContent);
        var authBody = await authResponse.Content.ReadFromJsonAsync<JsonObject>();

        using var trailingSlashContent = new ByteArrayContent(new byte[BugReportLimits.PublicAuthRequestBodyBytes + 1]);
        trailingSlashContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var trailingSlashResponse = await client.PostAsync("/api/auth/login/", trailingSlashContent);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("payload_too_large", body?["errorCode"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, authResponse.StatusCode);
        Assert.Equal("payload_too_large", authBody?["errorCode"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, trailingSlashResponse.StatusCode);
    }

    [Fact]
    public async Task ReportCommentAndReopenTextLimits_RejectOverflow()
    {
        const string activeId = "hardening-active-text-limits";
        const string closedId = "hardening-closed-text-limits";
        await _factory.SeedBugAsync(new SeedBugRequest(activeId, "Active text limits", "Original.", "api", "open", "mid",
            "2026-07-01 10:00:00", "2026-07-01 10:00:00", null, TestApiFactory.DefaultUserId));
        await _factory.SeedBugAsync(new SeedBugRequest(closedId, "Closed text limits", "Original.", "api", "closed", "mid",
            "2026-07-01 10:00:00", "2026-07-01 10:00:00", "2026-07-01 11:00:00", TestApiFactory.DefaultUserId));
        using var client = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);

        var report = await client.PatchAsJsonAsync($"/api/bugs/{activeId}/report", new
        {
            reportText = new string('r', BugReportLimits.SolutionReportCharacters + 1),
            expectedVersion = 1
        });
        var comment = await client.PostAsJsonAsync($"/api/bugs/{activeId}/comments", new
        {
            body = new string('c', BugReportLimits.CommentCharacters + 1)
        });
        var reopen = await client.PatchAsJsonAsync($"/api/bugs/{closedId}/reopen", new
        {
            reason = new string('r', BugReportLimits.ReopenReasonCharacters + 1),
            expectedVersion = 1
        });

        Assert.Equal(HttpStatusCode.BadRequest, report.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, comment.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);
        Assert.Equal(0, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM ticket_activity WHERE ticket_id = $id;", activeId));
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM bug_tickets WHERE id = $id AND status = 'closed';", closedId));
    }

    [Fact]
    public async Task TicketAttachments_SpoofedDeclaredMime_Returns422WithoutMutation()
    {
        const string ticketId = "hardening-multipart-mime";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Multipart MIME", "Reject spoofed content.", "api", "open", "mid",
            "2026-07-01 10:00:00", "2026-07-01 10:00:00", null, TestApiFactory.DefaultUserId));
        using var client = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("1"), "expectedVersion");
        upload.Add(new StringContent("initial-report"), "purpose");
        var image = new ByteArrayContent(TinyPngBytes());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        upload.Add(image, "files", "spoofed.jpg");

        var response = await client.PostAsync($"/api/bugs/{ticketId}/attachments", upload);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("does not match", body?["error"]?.GetValue<string>());
        Assert.Equal(0, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM ticket_attachments WHERE ticket_id = $id;", ticketId));
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT version FROM bug_tickets WHERE id = $id;", ticketId));
    }

    [Fact]
    public async Task ProjectCreator_BecomesOwnerAndAllocated_AndOwnerRemovalIsRejected()
    {
        using var senior = await CreateAuthorizedClientAsync(TestApiFactory.SeniorUserId);
        var create = await senior.PostAsJsonAsync("/api/projects", new { name = $"Owned {Guid.NewGuid():N}"[..20] });
        var project = await create.Content.ReadFromJsonAsync<JsonObject>();
        var projectId = project?["projectId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(TestApiFactory.SeniorUserId, project?["ownerUserId"]?.GetValue<string>());
        Assert.Equal("senior_test", project?["ownerUsername"]?.GetValue<string>());

        var allocations = await (await senior.GetAsync("/api/projects/allocations")).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(allocations!, x => x?["projectId"]?.GetValue<string>() == projectId &&
            x?["userIds"]?.AsArray().Any(id => id?.GetValue<string>() == TestApiFactory.SeniorUserId) == true);

        var removal = await senior.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new { userIds = Array.Empty<string>() });
        var removalBody = await removal.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.BadRequest, removal.StatusCode);
        Assert.Contains("owner", removalBody?["error"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CursorPagination_TraversesMoreThanOneHundredTiedRowsWithoutDuplicates()
    {
        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(admin, "normal");
        for (var i = 0; i < 105; i++)
        {
            await _factory.SeedBugAsync(new SeedBugRequest($"cursor-tied-{i:D3}", $"Cursor {i}", "Cursor test.", "api", "todo", "mid",
                "2026-07-20 10:00:00", "2026-07-20 10:00:00", null, null, ProjectId: projectId));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        long? total = null;
        do
        {
            var path = $"/api/bugs?status=active&projectId={projectId}&pagination=cursor&limit=37" +
                (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await admin.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            total ??= body?["totalCount"]?.GetValue<long>();
            foreach (var item in body?["items"]?.AsArray() ?? []) Assert.True(seen.Add(item?["id"]?.GetValue<string>()!));
            cursor = body?["nextCursor"]?.GetValue<string>();
            if (body?["hasMore"]?.GetValue<bool>() != true) break;
        } while (true);

        Assert.Equal(105, total);
        Assert.Equal(105, seen.Count);
    }

    [Fact]
    public async Task InaccessibleExactTicket_ReturnsStructuredRemediation_AndApprovedRequestGrantsAccess()
    {
        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(admin, "sensitive");
        const string ticketId = "access-request-sensitive-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Secret", "Must not leak.", "api", "open", "high",
            "2026-07-21 10:00:00", "2026-07-21 10:00:00", null, null, ReporterUserId: TestApiFactory.AdminUserId, ProjectId: projectId));

        using var agent = await CreateAuthorizedClientAsync(TestApiFactory.AgentUserId);
        var denied = await agent.GetAsync($"/api/bugs/{ticketId}");
        var deniedBody = await denied.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("ticket_access_denied", deniedBody?["errorCode"]?.GetValue<string>());
        Assert.Equal("project_membership_required", deniedBody?["reason"]?.GetValue<string>());
        Assert.Null(deniedBody?["issueTitle"]);
        Assert.DoesNotContain("Must not leak", await denied.Content.ReadAsStringAsync());

        var request = await agent.PostAsJsonAsync($"/api/bugs/{ticketId}/access-request", new { reason = "Investigating assignment." });
        var requestBody = await request.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        Assert.Equal(requestId, (await (await agent.PostAsJsonAsync($"/api/bugs/{ticketId}/access-request", new { reason = "duplicate" })).Content.ReadFromJsonAsync<JsonObject>())?["requestId"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("/api/projects/access-requests")).StatusCode);

        var approve = await admin.PatchAsJsonAsync($"/api/projects/access-requests/{requestId}", new { status = "approved" });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var allowed = await agent.GetAsync($"/api/bugs/{ticketId}");
        var allowedBody = await allowed.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(ticketId, allowedBody?["id"]?.GetValue<string>());
        Assert.All(allowedBody?["contacts"]?.AsArray() ?? [], contact => Assert.Null(contact?["email"]));
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync("/api/bugs/does-not-exist-access-test")).StatusCode);
    }

    [Fact]
    public async Task TargetedComment_RecordsRecipientIdentityAndOnlyTargetsRelevantContact()
    {
        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(admin, "normal");
        await admin.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new { userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.DefaultUserId } });
        const string ticketId = "targeted-comment-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Target contact", "Contact owner.", "api", "open", "mid",
            "2026-07-22 10:00:00", "2026-07-22 10:00:00", null, TestApiFactory.DefaultUserId, ProjectId: projectId));
        using var dev = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);

        var invalid = await dev.PostAsJsonAsync($"/api/bugs/{ticketId}/comments", new { body = "Invalid target", recipientUserId = TestApiFactory.AgentUserId });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var posted = await dev.PostAsJsonAsync($"/api/bugs/{ticketId}/comments", new { body = "Owner review requested", recipientUserId = TestApiFactory.AdminUserId });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        var detail = await (await dev.GetAsync($"/api/bugs/{ticketId}")).Content.ReadFromJsonAsync<JsonObject>();
        var activity = detail?["activity"]?.AsArray().First(x => x?["body"]?.GetValue<string>() == "Owner review requested");
        Assert.Equal(TestApiFactory.AdminUserId, activity?["subjectUserId"]?.GetValue<string>());
        Assert.Equal("admin_test", activity?["subject"]?["username"]?.GetValue<string>());
        var notifications = await (await admin.GetAsync("/api/notifications?unreadOnly=true")).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(notifications!, x => x?["ticketId"]?.GetValue<string>() == ticketId);
    }

    [Fact]
    public async Task AgentTicketMutationResponse_RedactsAllContactEmails()
    {
        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(admin, "normal");
        await admin.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.AgentUserId }
        });
        const string ticketId = "agent-redacted-mutation-001";
        await _factory.SeedBugAsync(new SeedBugRequest(
            ticketId,
            "Agent mutation privacy",
            "Emails must remain private.",
            "api",
            "open",
            "mid",
            "2026-07-22 11:00:00",
            "2026-07-22 11:00:00",
            null,
            TestApiFactory.AgentUserId,
            ReporterUserId: TestApiFactory.AdminUserId,
            ProjectId: projectId));

        using var agent = await CreateAuthorizedClientAsync(TestApiFactory.AgentUserId);
        var response = await agent.PatchAsJsonAsync($"/api/bugs/{ticketId}/report", new
        {
            reportText = "Investigated by the assigned agent.",
            reportImages = Array.Empty<object>(),
            expectedVersion = 1
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(body?["reporter"]?["email"]);
        Assert.Null(body?["assignee"]?["email"]);
        Assert.All(body?["contacts"]?.AsArray() ?? [], contact => Assert.Null(contact?["email"]));
        Assert.All(body?["activity"]?.AsArray() ?? [], activity =>
        {
            Assert.Null(activity?["actor"]?["email"]);
            Assert.Null(activity?["subject"]?["email"]);
        });
    }

    [Fact]
    public async Task ProjectOwnershipAlone_DoesNotBypassSensitiveTicketAuthorization()
    {
        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(admin, "sensitive");
        await _factory.ExecuteSqlAsync("""
            UPDATE projects SET owner_user_id = $user WHERE project_id = $project;
            DELETE FROM project_allocations WHERE project_id = $project AND user_id = $user;
            """, ("$user", TestApiFactory.SeniorUserId), ("$project", projectId));
        const string ticketId = "owner-only-sensitive-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Owner is not authorization", "Sensitive.", "api", "open", "high",
            "2026-07-23 10:00:00", "2026-07-23 10:00:00", null, null, ReporterUserId: TestApiFactory.AdminUserId, ProjectId: projectId));

        using var senior = await CreateAuthorizedClientAsync(TestApiFactory.SeniorUserId);
        var response = await senior.GetAsync($"/api/bugs/{ticketId}");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ticket_access_denied", body?["errorCode"]?.GetValue<string>());
    }

    [Fact]
    public async Task Summary_IsUncappedAndMatchesScopedCursorResults()
    {
        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);

        async Task<List<JsonObject>> ReadAllAsync(string status)
        {
            var results = new List<JsonObject>();
            string? cursor = null;
            do
            {
                var path = $"/api/bugs?status={status}&pagination=cursor&limit=100" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
                var body = await (await admin.GetAsync(path)).Content.ReadFromJsonAsync<JsonObject>();
                results.AddRange((body?["items"]?.AsArray() ?? []).Select(x => x!.AsObject()));
                if (body?["hasMore"]?.GetValue<bool>() != true) break;
                cursor = body?["nextCursor"]?.GetValue<string>();
            } while (cursor is not null);
            return results;
        }

        var active = await ReadAllAsync("active");
        var closed = await ReadAllAsync("closed");
        var summary = await (await admin.GetAsync("/api/bugs/summary")).Content.ReadFromJsonAsync<JsonObject>();
        var visibleProjects = await (await admin.GetAsync("/api/projects")).Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(active.Count, summary?["activeTotal"]?.GetValue<long>());
        Assert.Equal(active.Count(x => x["assigneeUserId"]?.GetValue<string>() == TestApiFactory.AdminUserId), summary?["allocatedToMe"]?.GetValue<long>());
        Assert.Equal(active.Count(x => x["assigneeUserId"] is null), summary?["unassignedActive"]?.GetValue<long>());
        Assert.Equal(active.Count(x => x["severity"]?.GetValue<string>() == "urgent" || x["priority"]?.GetValue<string>() == "p0"), summary?["urgentActive"]?.GetValue<long>());
        Assert.Equal(visibleProjects?.Count ?? 0, summary?["visibleProjects"]?.GetValue<long>());
        Assert.Equal(closed.Count, summary?["statusCounts"]?["closed"]?.GetValue<long>());
    }

    [Fact]
    public async Task HumanAndAgentCursorFilters_ReturnTheSameScopedTickets()
    {
        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(admin, "normal");
        await admin.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new { userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.AgentUserId } });
        await _factory.SeedBugAsync(new SeedBugRequest("filter-parity-high", "Parity high", "Visible.", "api", "open", "high",
            "2026-07-24 10:00:00", "2026-07-24 10:00:00", null, null, ProjectId: projectId, Priority: "p1", Tags: ["security"]));
        await _factory.SeedBugAsync(new SeedBugRequest("filter-parity-low", "Parity low", "Filtered.", "api", "todo", "low",
            "2026-07-24 10:00:00", "2026-07-24 10:00:00", null, null, ProjectId: projectId, Priority: "p3"));
        using var agent = await CreateAuthorizedClientAsync(TestApiFactory.AgentUserId);
        var path = $"/api/bugs?status=active&pagination=cursor&limit=100&projectId={projectId}&priority=p1&severity=high&tag=security";
        var humanBody = await (await admin.GetAsync(path)).Content.ReadFromJsonAsync<JsonObject>();
        var agentBody = await (await agent.GetAsync(path)).Content.ReadFromJsonAsync<JsonObject>();
        var humanIds = humanBody?["items"]?.AsArray().Select(x => x?["id"]?.GetValue<string>()).ToArray();
        var agentIds = agentBody?["items"]?.AsArray().Select(x => x?["id"]?.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "filter-parity-high" }, humanIds);
        Assert.Equal(humanIds, agentIds);
    }
}
