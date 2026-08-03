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
    public async Task ExportBugs_AsSenior_ReturnsJsonWithoutRawEvidenceAndCsvContent()
    {
        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);

        var createResponse = await devClient.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = "  =1+1,\"quoted\"",
            description = "Export should retain details without raw evidence blobs.",
            bugType = "api",
            projectId = "project-general",
            severity = "high",
            priority = "p1",
            tags = new[] { "back-end" },
            reportImages = new[] { new { name = "capture.png", contentType = "image/png", dataUrl = TinyPngDataUrl } },
            textEvidence = new[] { new { name = "server-log.txt", contentType = "text/plain", text = "SECRET RAW TEXT SHOULD NOT EXPORT" } }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var ticketId = created?["id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(ticketId));

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);

        var jsonResponse = await seniorClient.PostAsJsonAsync("/api/bugs/export", new { format = "json", ticketIds = new[] { ticketId } });
        var jsonBody = await jsonResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
        Assert.Equal("application/json", jsonResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", jsonResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("capture.png", jsonBody);
        Assert.Contains("server-log.txt", jsonBody);
        Assert.DoesNotContain("data:image/png;base64", jsonBody);
        Assert.DoesNotContain("SECRET RAW TEXT SHOULD NOT EXPORT", jsonBody);

        var csvResponse = await seniorClient.PostAsJsonAsync("/api/bugs/export", new { format = "csv", ticketIds = new[] { ticketId } });
        var csvBody = await csvResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id,issue_title,bug_type", csvBody);
        Assert.Contains(ticketId!, csvBody);
        Assert.Contains("back-end", csvBody);
        Assert.Contains("\"'=1+1,\"\"quoted\"\"\"", csvBody);

        await _factory.ExecuteSqlAsync(
            "UPDATE projects SET name = '@SUM(A1:A2)' WHERE project_id = 'project-general';");
        var projectCsvResponse = await seniorClient.PostAsJsonAsync("/api/bugs/export", new { format = "csv", ticketIds = new[] { ticketId } });
        Assert.Contains("'@SUM(A1:A2)", await projectCsvResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExportBugs_AsDevOrAgent_Returns403Forbidden()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "export-forbidden-ticket-001",
            IssueTitle: "Export role gate",
            Description: "Only senior/admin humans export.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-03-01 10:00:00",
            UpdatedAt: "2026-03-01 10:00:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        var devResponse = await devClient.PostAsJsonAsync("/api/bugs/export", new { format = "json", ticketIds = new[] { "export-forbidden-ticket-001" } });

        using var agentClient = _factory.CreateClient();
        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);
        var agentResponse = await agentClient.PostAsJsonAsync("/api/bugs/export", new { format = "csv", ticketIds = new[] { "export-forbidden-ticket-001" } });

        Assert.Equal(HttpStatusCode.Forbidden, devResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, agentResponse.StatusCode);
    }

    [Fact]
    public async Task DocsEndpoints_AllowAuthenticatedHumanAndAgentTokens()
    {
        using var humanClient = _factory.CreateClient();
        var humanToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        humanClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", humanToken);
        var openApiResponse = await humanClient.GetAsync("/api/docs/openapi.json");
        var openApiBody = await openApiResponse.Content.ReadFromJsonAsync<JsonObject>();

        using var agentClient = _factory.CreateClient();
        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);
        var examplesResponse = await agentClient.GetAsync("/api/docs/examples");
        var examplesText = await examplesResponse.Content.ReadAsStringAsync();
        var examplesBody = JsonNode.Parse(examplesText)?.AsObject();

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal("3.0.3", openApiBody?["openapi"]?.GetValue<string>());
        Assert.NotNull(openApiBody?["paths"]?["/api/bugs/export"]);
        Assert.Equal(HttpStatusCode.OK, examplesResponse.StatusCode);
        Assert.Contains("placeholder", examplesBody?["note"]?.GetValue<string>());
        Assert.DoesNotContain(TestApiFactory.DefaultUserPassword, examplesText);
    }

    [Fact]
    public async Task AuditLogs_AdminCanFilterSearchAndNonAdminCannotRead()
    {
        using var loginClient = _factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.DefaultUserEmail,
            password = TestApiFactory.DefaultUserPassword
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonObject>();
        var token = loginBody?["accessToken"]?.GetValue<string>();
        loginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logoutResponse = await loginClient.PostAsync("/api/auth/logout", null);
        var revokedTokenResponse = await loginClient.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedTokenResponse.StatusCode);

        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        var forbiddenResponse = await devClient.GetAsync("/api/audit-logs?search=logged");

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var logsResponse = await adminClient.GetAsync($"/api/audit-logs?actorType=human&search={TestApiFactory.DefaultUserId}&limit=50");
        var logs = await logsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        Assert.NotNull(logs);
        Assert.Contains(logs!, item => item?["action"]?.GetValue<string>() == "login" && item?["actorUserId"]?.GetValue<string>() == TestApiFactory.DefaultUserId);
        Assert.Contains(logs!, item => item?["action"]?.GetValue<string>() == "logout" && item?["actorUserId"]?.GetValue<string>() == TestApiFactory.DefaultUserId);
    }

    [Fact]
    public async Task AuditLogs_RecordTicketViewCreateAndEditEvents()
    {
        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);

        var createResponse = await devClient.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = $"Audited ticket {Guid.NewGuid():N}",
            description = "Audit ticket lifecycle.",
            bugType = "api",
            projectId = "project-general",
            severity = "mid"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var ticketId = created?["id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(ticketId));

        var detailResponse = await devClient.GetAsync($"/api/bugs/{ticketId}");
        var editResponse = await devClient.PatchAsJsonAsync($"/api/bugs/{ticketId}/initial-report", new
        {
            reportText = "Audit edited initial report",
            expectedVersion = 1
        });

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var logsResponse = await adminClient.GetAsync($"/api/audit-logs?ticketId={ticketId}&limit=50");
        var logs = await logsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        Assert.NotNull(logs);
        Assert.Contains(logs!, item => item?["action"]?.GetValue<string>() == "ticket_created");
        Assert.Contains(logs!, item => item?["action"]?.GetValue<string>() == "ticket_viewed");
        Assert.Contains(logs!, item => item?["action"]?.GetValue<string>() == "ticket_edited");
    }

}
