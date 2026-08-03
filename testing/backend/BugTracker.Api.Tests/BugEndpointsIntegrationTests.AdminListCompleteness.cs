using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed partial class BugEndpointsIntegrationTests
{
    [Fact]
    public async Task AdminLists_IncludeActiveAndClosedTicketsWithoutProjectAllocation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = $"admin-global-{suffix}";
        var activeId = $"admin-global-active-{suffix}";
        var closedId = $"admin-global-closed-{suffix}";
        await _factory.ExecuteSqlAsync("""
            INSERT INTO projects (project_id, name, visibility, owner_user_id, created_at, updated_at)
            VALUES ($project, $name, 'sensitive', $owner, '2026-07-30 10:00:00', '2026-07-30 10:00:00');
            """, ("$project", projectId), ("$name", $"Admin global {suffix}"), ("$owner", TestApiFactory.AdminUserId));
        await _factory.SeedBugAsync(new SeedBugRequest(activeId, "Globally visible active", "Admin must see this without allocation.", "api", "open", "high",
            "2026-07-30 11:00:00", "2026-07-30 11:00:00", null, null, ReporterUserId: TestApiFactory.AdminUserId, ProjectId: projectId));
        await _factory.SeedBugAsync(new SeedBugRequest(closedId, "Globally visible closed", "Admin must see this without allocation.", "api", "closed", "mid",
            "2026-07-30 12:00:00", "2026-07-30 13:00:00", "2026-07-30 13:00:00", TestApiFactory.AdminUserId,
            ReporterUserId: TestApiFactory.AdminUserId, ProjectId: projectId, ResolvedByUserId: TestApiFactory.AdminUserId, AssignedAt: "2026-07-30 12:30:00"));

        using var admin = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var active = await ReadCursorPageAsync(admin, "active", projectId);
        var closed = await ReadCursorPageAsync(admin, "closed", projectId);

        Assert.Contains(active, item => item?["id"]?.GetValue<string>() == activeId);
        Assert.Contains(closed, item => item?["id"]?.GetValue<string>() == closedId);
        Assert.Equal(0, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM project_allocations WHERE project_id = $id AND user_id = 'usr_test_admin_001';", projectId));
    }

    [Fact]
    public async Task DemoAdminLists_ReturnEverySeededActiveAndClosedTicket()
    {
        using var factory = TestApiFactory.CreateEmpty();
        await factory.SeedDemoAsync();
        using var admin = factory.CreateClient();
        var token = await factory.CreateAccessTokenAsync("usr_admin_001");
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var active = await ReadCursorEnvelopeAsync(admin, "active");
        var closed = await ReadCursorEnvelopeAsync(admin, "closed");

        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(45, active.Body?["totalCount"]?.GetValue<long>());
        Assert.Equal(15, closed.Body?["totalCount"]?.GetValue<long>());
        Assert.Equal(45, active.Body?["items"]?.AsArray().Count);
        Assert.Equal(15, closed.Body?["items"]?.AsArray().Count);
        Assert.Equal(5, active.Body?["items"]?.AsArray().Select(item => item?["projectId"]?.GetValue<string>()).Distinct().Count());
        Assert.Equal(5, closed.Body?["items"]?.AsArray().Select(item => item?["projectId"]?.GetValue<string>()).Distinct().Count());
    }

    private static async Task<JsonArray> ReadCursorPageAsync(HttpClient client, string status, string projectId)
    {
        var response = await client.GetAsync($"/api/bugs?status={status}&projectId={Uri.EscapeDataString(projectId)}&pagination=cursor&limit=100");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body?["items"]?.AsArray() ?? [];
    }

    private static async Task<(HttpStatusCode StatusCode, JsonObject? Body)> ReadCursorEnvelopeAsync(HttpClient client, string status)
    {
        var response = await client.GetAsync($"/api/bugs?status={status}&pagination=cursor&limit=100");
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonObject>());
    }
}
