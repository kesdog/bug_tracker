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
    public async Task GetAllocatedBugs_ReturnsOnlyCurrentUserAssignments()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "allocated-ticket-001",
            IssueTitle: "Assigned to dev",
            Description: "Should be visible in allocated list.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-01-01 10:00:00",
            UpdatedAt: "2026-01-01 10:10:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId));

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "allocated-ticket-002",
            IssueTitle: "Assigned to senior",
            Description: "Should not be visible for dev.",
            BugType: "database",
            Status: "reopened",
            Severity: "mid",
            CreatedAt: "2026-01-01 11:00:00",
            UpdatedAt: "2026-01-01 11:15:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.SeniorUserId));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/bugs/allocated");
        var tickets = await response.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(tickets);
        Assert.Contains(tickets!, x => x?["id"]?.GetValue<string>() == "allocated-ticket-001");
        Assert.DoesNotContain(tickets!, x => x?["id"]?.GetValue<string>() == "allocated-ticket-002");
    }

    [Fact]
    public async Task UpdateInitialBugReport_AsAssignee_UpdatesSubmittedReport()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "initial-report-update-001",
            IssueTitle: "Editable submitted report",
            Description: "Original submitted report",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-01-03 07:30:00",
            UpdatedAt: "2026-01-03 08:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            AssignedAt: "2026-01-03 07:45:00"));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/initial-report-update-001/initial-report", new
        {
            reportText = "Updated submitted report [[img:0]]",
            reportImages = new[]
            {
                new { name = "initial-proof.png", contentType = "image/png", dataUrl = TinyPngDataUrl }
            },
            expectedVersion = 1
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated submitted report [[img:0]]", body?["description"]?.GetValue<string>());
        Assert.Single(body?["reportImages"]?.AsArray()!);
        Assert.Empty(body?["resolutionReportImages"]?.AsArray()!);
    }

    [Fact]
    public async Task UpdateBugReport_AsAssignee_ReplacesTextAndImages()
    {
        // Arrange: seed one assigned active ticket.
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "report-update-001",
            IssueTitle: "Editable report",
            Description: "Original description",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-01-03 08:00:00",
            UpdatedAt: "2026-01-03 08:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act: update report text and attach one image block.
        var response = await client.PatchAsJsonAsync("/api/bugs/report-update-001/report", new
        {
            reportText = "Fix summary [[img:0]]",
            reportImages = new[]
            {
                new { name = "result.png", contentType = "image/png", dataUrl = TinyPngDataUrl }
            },
            expectedVersion = 1
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        // Assert: API reflects updated report and returned image metadata.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Fix summary [[img:0]]", body?["postResolutionReport"]?.GetValue<string>());
        Assert.Empty(body?["reportImages"]?.AsArray()!);
        Assert.Single(body?["resolutionReportImages"]?.AsArray()!);

        var replacement = await client.PatchAsJsonAsync("/api/bugs/report-update-001/report", new
        {
            reportText = "Corrected fix summary",
            reportImages = Array.Empty<object>(),
            expectedVersion = 2
        });
        var replacementBody = await replacement.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        Assert.Equal("Corrected fix summary", replacementBody?["postResolutionReport"]?.GetValue<string>());
        Assert.Empty(replacementBody?["resolutionReportImages"]?.AsArray()!);
    }

    [Fact]
    public async Task CloseBug_AsAssignee_TransitionsToClosedWithResolutionData()
    {
        // Arrange: seed one assigned active ticket.
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "close-bug-001",
            IssueTitle: "Ready to close",
            Description: "Closing flow",
            BugType: "database",
            Status: "reopened",
            Severity: "high",
            CreatedAt: "2026-01-04 08:00:00",
            UpdatedAt: "2026-01-04 08:30:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act: close the ticket with a final resolution report.
        var response = await client.PatchAsJsonAsync("/api/bugs/close-bug-001/close", new
        {
            resolutionNotes = "Resolved by rebuilding index [[img:0]]",
            reportImages = new[]
            {
                new { name = "after.png", contentType = "image/png", dataUrl = TinyPngDataUrl }
            },
            expectedVersion = 1
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        // Assert: status and close metadata are persisted by the endpoint.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("closed", body?["status"]?.GetValue<string>());
        Assert.Equal("Resolved by rebuilding index [[img:0]]", body?["resolutionNotes"]?.GetValue<string>());
        Assert.Equal(TestApiFactory.DefaultUserId, body?["resolvedByUserId"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(body?["closeDate"]?.GetValue<string>()));
        Assert.Single(body?["resolutionReportImages"]?.AsArray()!);
    }

    [Fact]
    public async Task CancelBug_WithoutSolution_ArchivesWithCancelledStatusAndReason()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "cancel-bug-001", IssueTitle: "Mistaken ticket", Description: "No solution is needed.", BugType: "api",
            Status: "open", Severity: "low", CreatedAt: "2026-01-04 08:00:00", UpdatedAt: "2026-01-04 08:30:00",
            CloseDate: null, AssigneeUserId: TestApiFactory.DefaultUserId));
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId));

        var response = await client.PatchAsJsonAsync("/api/bugs/cancel-bug-001/cancel", new { reason = "Could not reproduce", expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        var archive = await client.GetAsync("/api/bugs?status=closed");
        var archiveBody = await archive.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("cancelled", body?["status"]?.GetValue<string>());
        Assert.Equal("Could not reproduce", body?["cancellationReason"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Contains(archiveBody!, item => item?["id"]?.GetValue<string>() == "cancel-bug-001" && item?["status"]?.GetValue<string>() == "cancelled");
    }

    [Fact]
    public async Task CloseBug_AsReporter_WhenUnassigned_TransitionsToClosed()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "close-bug-reporter-001",
            IssueTitle: "Reporter closes own ticket",
            Description: "Reporter should be able to close this.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-01-05 09:00:00",
            UpdatedAt: "2026-01-05 09:00:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/close-bug-reporter-001/close", new
        {
            resolutionNotes = "Reporter closed after verification",
            expectedVersion = 1
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("closed", body?["status"]?.GetValue<string>());
        Assert.Equal("Reporter closed after verification", body?["resolutionNotes"]?.GetValue<string>());
        Assert.Equal(TestApiFactory.DefaultUserId, body?["resolvedByUserId"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(body?["closeDate"]?.GetValue<string>()));
    }

    [Fact]
    public async Task CloseBug_AsSenior_WhenProjectAssociated_TransitionsToClosed()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "close-bug-senior-scope-001",
            IssueTitle: "Senior project close",
            Description: "Senior should close project ticket.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-01-05 10:00:00",
            UpdatedAt: "2026-01-05 10:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ProjectId: "project-general"));

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await adminClient.PatchAsJsonAsync($"/api/auth/users/{TestApiFactory.SeniorUserId}/role", new { role = "senior" });

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/close-bug-senior-scope-001/close", new
        {
            resolutionNotes = "Senior closed for assigned project",
            expectedVersion = 1
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("closed", body?["status"]?.GetValue<string>());
        Assert.Equal(TestApiFactory.SeniorUserId, body?["resolvedByUserId"]?.GetValue<string>());
    }

    [Fact]
    public async Task CloseBug_AsSenior_WhenSensitiveProjectOutOfScope_Returns403_ButAdminCanClose()
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await adminClient.PatchAsJsonAsync($"/api/auth/users/{TestApiFactory.SeniorUserId}/role", new { role = "senior" });

        var createResponse = await adminClient.PostAsJsonAsync("/api/projects", new
        {
            name = $"Out Scope {Guid.NewGuid().ToString("N")[..6]}",
            visibility = "sensitive"
        });
        var createdBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var projectId = createdBody?["projectId"]?.GetValue<string>()
            ?? createdBody?["project_id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(projectId));

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "close-bug-senior-out-scope-001",
            IssueTitle: "Senior out of scope close",
            Description: "Senior should not close unrelated project ticket.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-01-05 11:00:00",
            UpdatedAt: "2026-01-05 11:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ProjectId: projectId));

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);

        var forbiddenResponse = await seniorClient.PatchAsJsonAsync("/api/bugs/close-bug-senior-out-scope-001/close", new
        {
            resolutionNotes = "Senior should be blocked",
            expectedVersion = 1
        });

        var adminCloseResponse = await adminClient.PatchAsJsonAsync("/api/bugs/close-bug-senior-out-scope-001/close", new
        {
            resolutionNotes = "Admin can close any ticket",
            expectedVersion = 1
        });
        var adminCloseBody = await adminCloseResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminCloseResponse.StatusCode);
        Assert.Equal("closed", adminCloseBody?["status"]?.GetValue<string>());
        Assert.Equal(TestApiFactory.AdminUserId, adminCloseBody?["resolvedByUserId"]?.GetValue<string>());
    }

    [Fact]
    public async Task UpdateBugMetadata_ValidatesAuthorizationAndPersistsAudit()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "metadata-edit-001",
            IssueTitle: "Old metadata",
            Description: "Metadata update flow.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-04-01 09:00:00",
            UpdatedAt: "2026-04-01 09:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.SeniorUserId,
            Priority: "p2",
            Tags: ["back-end"]));

        using var forbiddenClient = _factory.CreateClient();
        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        forbiddenClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);
        var forbiddenResponse = await forbiddenClient.PatchAsJsonAsync("/api/bugs/metadata-edit-001/metadata", new { issueTitle = "Agent edit", expectedVersion = 1 });

        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        var invalidResponse = await devClient.PatchAsJsonAsync("/api/bugs/metadata-edit-001/metadata", new
        {
            severity = "urgent",
            priority = "p2",
            tags = new[] { "front-end", "back-end" },
            expectedVersion = 1
        });
        var invalidBody = await invalidResponse.Content.ReadFromJsonAsync<JsonObject>();

        var updateResponse = await devClient.PatchAsJsonAsync("/api/bugs/metadata-edit-001/metadata", new
        {
            issueTitle = "New metadata",
            bugType = "form_submission",
            severity = "urgent",
            priority = "p1",
            tags = new[] { "front-end", "security" },
            projectId = "project-general",
            expectedVersion = 1
        });
        var updateBody = await updateResponse.Content.ReadFromJsonAsync<JsonObject>();

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var logsResponse = await adminClient.GetAsync("/api/audit-logs?ticketId=metadata-edit-001&action=ticket_metadata_updated&limit=10");
        var logs = await logsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("urgent severity requires priority p0 or p1", invalidBody?["error"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("New metadata", updateBody?["issueTitle"]?.GetValue<string>());
        Assert.Equal("form_submission", updateBody?["bugType"]?.GetValue<string>());
        Assert.Equal("urgent", updateBody?["severity"]?.GetValue<string>());
        Assert.Equal("p1", updateBody?["priority"]?.GetValue<string>());
        Assert.Contains(updateBody?["tags"]?.AsArray()!, tag => tag?.GetValue<string>() == "security");
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        Assert.Contains(logs!, item => item?["action"]?.GetValue<string>() == "ticket_metadata_updated");
    }

    [Fact]
    public async Task UpdateBugMetadata_ClosedTicketRequiresReopen()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "metadata-closed-001",
            IssueTitle: "Closed metadata",
            Description: "Closed tickets cannot be metadata edited.",
            BugType: "api",
            Status: "closed",
            Severity: "mid",
            CreatedAt: "2026-04-01 10:00:00",
            UpdatedAt: "2026-04-01 10:30:00",
            CloseDate: "2026-04-01 10:30:00",
            AssigneeUserId: TestApiFactory.DefaultUserId,
            ResolvedByUserId: TestApiFactory.DefaultUserId));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/metadata-closed-001/metadata", new { issueTitle = "Should fail", expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("closed tickets cannot be edited; reopen first", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task ReopenBug_ClosedTicketClearsArchiveFieldsAndNotifiesReporter()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "reopen-ticket-001",
            IssueTitle: "Needs reopening",
            Description: "Regression returned.",
            BugType: "api",
            Status: "closed",
            Severity: "high",
            CreatedAt: "2026-04-02 09:00:00",
            UpdatedAt: "2026-04-02 10:00:00",
            CloseDate: "2026-04-02 10:00:00",
            AssigneeUserId: TestApiFactory.SeniorUserId,
            ResolvedByUserId: TestApiFactory.SeniorUserId,
            Priority: "p1"));

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);

        var response = await seniorClient.PatchAsJsonAsync("/api/bugs/reopen-ticket-001/reopen", new { reason = "Regression reproduced", expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        using var reporterClient = _factory.CreateClient();
        var reporterToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        reporterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reporterToken);
        var notificationsResponse = await reporterClient.GetAsync("/api/notifications?unreadOnly=true");
        var notifications = await notificationsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("reopened", body?["status"]?.GetValue<string>());
        Assert.Null(body?["closeDate"]);
        Assert.Null(body?["resolvedByUserId"]);
        Assert.Contains(body?["activity"]?.AsArray()!, item => item?["kind"]?.GetValue<string>() == "reopened");
        Assert.Equal(HttpStatusCode.OK, notificationsResponse.StatusCode);
        Assert.Contains(notifications!, item => item?["kind"]?.GetValue<string>() == "ticket_reopened" && item?["ticketId"]?.GetValue<string>() == "reopen-ticket-001");
    }

}
