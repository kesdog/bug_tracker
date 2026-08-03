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
    public async Task BulkAllocate_AsSeniorAssignsActiveTicketsAndReportsFailures()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "bulk-assign-active-001",
            IssueTitle: "Bulk active",
            Description: "Should assign.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-04-03 09:00:00",
            UpdatedAt: "2026-04-03 09:00:00",
            CloseDate: null,
            AssigneeUserId: null));
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "bulk-assign-closed-001",
            IssueTitle: "Bulk closed",
            Description: "Should fail.",
            BugType: "api",
            Status: "closed",
            Severity: "mid",
            CreatedAt: "2026-04-03 10:00:00",
            UpdatedAt: "2026-04-03 10:00:00",
            CloseDate: "2026-04-03 10:00:00",
            AssigneeUserId: null));

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);

        var response = await seniorClient.PatchAsJsonAsync("/api/bugs/bulk-allocate", new
        {
            items = new[] { new { ticketId = "bulk-assign-active-001", expectedVersion = 1 }, new { ticketId = "bulk-assign-closed-001", expectedVersion = 1 } },
            assigneeUserId = TestApiFactory.DefaultUserId
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        var notificationsResponse = await devClient.GetAsync("/api/notifications?unreadOnly=true");
        var notifications = await notificationsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(body?["updated"]?.AsArray()!, item => item?["id"]?.GetValue<string>() == "bulk-assign-active-001" && item?["assigneeUserId"]?.GetValue<string>() == TestApiFactory.DefaultUserId);
        Assert.Contains(body?["failed"]?.AsArray()!, item => item?["ticketId"]?.GetValue<string>() == "bulk-assign-closed-001" && item?["error"]?.GetValue<string>() == "ticket_not_active");
        Assert.Contains(notifications!, item => item?["kind"]?.GetValue<string>() == "ticket_assigned" && item?["ticketId"]?.GetValue<string>() == "bulk-assign-active-001");
        Assert.Contains(notifications!, item => item?["kind"]?.GetValue<string>() == "ticket_assigned"
            && item?["agentInstructions"]?["actionRequired"]?.GetValue<bool>() == true
            && item?["agentInstructions"]?["ticketDetailPath"]?.GetValue<string>() == "/api/bugs/bulk-assign-active-001"
            && item?["agentInstructions"]?["commentPath"]?.GetValue<string>() == "/api/bugs/bulk-assign-active-001/comments"
            && item?["agentInstructions"]?["markNotificationReadPath"]?.GetValue<string>() == $"/api/notifications/{item?["id"]?.GetValue<string>()}/read"
            && item?["agentInstructions"]?["completionAction"]?.GetValue<string>().Contains("consumed", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task ListBugs_AppliesServerSideMetadataFiltersAndRejectsInvalidEnums()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "filter-match-001",
            IssueTitle: "Filter match",
            Description: "Matches all filters.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-04-04 09:00:00",
            UpdatedAt: "2026-04-04 09:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            Priority: "p1",
            Tags: ["security", "back-end"]));
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "filter-miss-001",
            IssueTitle: "Filter miss",
            Description: "Different priority.",
            BugType: "database",
            Status: "open",
            Severity: "low",
            CreatedAt: "2026-04-04 10:00:00",
            UpdatedAt: "2026-04-04 10:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.SeniorUserId,
            Priority: "p3",
            Tags: ["performance"]));
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "filter-reporter-miss-001",
            IssueTitle: "Filter reporter miss",
            Description: "Matches metadata filters but has a different reporter.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-04-04 11:00:00",
            UpdatedAt: "2026-04-04 11:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            ReporterUserId: TestApiFactory.SeniorUserId,
            Priority: "p1",
            Tags: ["security", "back-end"]));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/bugs?status=active&limit=100&priority=p1&severity=high&tag=security&projectId=project-general&assigneeUserId={TestApiFactory.DefaultUserId}&reporterUserId={TestApiFactory.DefaultUserId}");
        var tickets = await response.Content.ReadFromJsonAsync<JsonArray>();
        var invalidResponse = await client.GetAsync("/api/bugs?status=active&priority=p9");
        var invalidBody = await invalidResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(tickets!, item => item?["id"]?.GetValue<string>() == "filter-match-001");
        Assert.DoesNotContain(tickets!, item => item?["id"]?.GetValue<string>() == "filter-miss-001");
        Assert.DoesNotContain(tickets!, item => item?["id"]?.GetValue<string>() == "filter-reporter-miss-001");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("invalid priority", invalidBody?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task Notifications_CanBeListedUnreadAndMarkedReadByOwner()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "notify-assignment-001",
            IssueTitle: "Notify assignment",
            Description: "Assignment should notify assignee.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-04-05 09:00:00",
            UpdatedAt: "2026-04-05 09:00:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);
        var assignResponse = await seniorClient.PatchAsJsonAsync("/api/bugs/notify-assignment-001/allocate", new { assigneeUserId = TestApiFactory.DefaultUserId, expectedVersion = 1 });

        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        var listResponse = await devClient.GetAsync("/api/notifications?unreadOnly=true");
        var notifications = await listResponse.Content.ReadFromJsonAsync<JsonArray>();
        var notificationId = notifications?
            .FirstOrDefault(item => item?["ticketId"]?.GetValue<string>() == "notify-assignment-001")?["id"]?.GetValue<string>();

        var readResponse = await devClient.PatchAsync($"/api/notifications/{notificationId}/read", null);
        var readBody = await readResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(notificationId));
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.True(readBody?["isRead"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Notifications_CommentsNotifyAssigneeWhenReporterComments()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "notify-comment-001",
            IssueTitle: "Notify comment",
            Description: "Reporter comments should notify assignee.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-04-05 10:00:00",
            UpdatedAt: "2026-04-05 10:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.SeniorUserId));

        using var reporterClient = _factory.CreateClient();
        var reporterToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        reporterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reporterToken);
        var commentResponse = await reporterClient.PostAsJsonAsync("/api/bugs/notify-comment-001/comments", new { body = "I can still reproduce this." });

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);
        var assigneeNotificationsResponse = await seniorClient.GetAsync("/api/notifications?unreadOnly=true");
        var assigneeNotifications = await assigneeNotificationsResponse.Content.ReadFromJsonAsync<JsonArray>();

        var reporterNotificationsResponse = await reporterClient.GetAsync("/api/notifications?unreadOnly=true");
        var reporterNotifications = await reporterNotificationsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, assigneeNotificationsResponse.StatusCode);
        Assert.Contains(assigneeNotifications!, item => item?["kind"]?.GetValue<string>() == "ticket_commented" && item?["ticketId"]?.GetValue<string>() == "notify-comment-001");
        Assert.DoesNotContain(reporterNotifications!, item => item?["kind"]?.GetValue<string>() == "ticket_commented" && item?["ticketId"]?.GetValue<string>() == "notify-comment-001");
    }

    [Fact]
    public async Task Notifications_CloseByAdminNotifiesReporterAndAssignee()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "notify-close-001",
            IssueTitle: "Notify close",
            Description: "Admin close should notify ticket participants.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-04-05 11:00:00",
            UpdatedAt: "2026-04-05 11:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.SeniorUserId));

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var closeResponse = await adminClient.PatchAsJsonAsync("/api/bugs/notify-close-001/close", new { resolutionNotes = "Closed by admin after verification.", expectedVersion = 1 });

        using var reporterClient = _factory.CreateClient();
        var reporterToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        reporterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reporterToken);
        var reporterNotificationsResponse = await reporterClient.GetAsync("/api/notifications?unreadOnly=true");
        var reporterNotifications = await reporterNotificationsResponse.Content.ReadFromJsonAsync<JsonArray>();

        using var assigneeClient = _factory.CreateClient();
        var assigneeToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        assigneeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", assigneeToken);
        var assigneeNotificationsResponse = await assigneeClient.GetAsync("/api/notifications?unreadOnly=true");
        var assigneeNotifications = await assigneeNotificationsResponse.Content.ReadFromJsonAsync<JsonArray>();

        var adminNotificationsResponse = await adminClient.GetAsync("/api/notifications?unreadOnly=true");
        var adminNotifications = await adminNotificationsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.Contains(reporterNotifications!, item => item?["kind"]?.GetValue<string>() == "ticket_closed" && item?["ticketId"]?.GetValue<string>() == "notify-close-001");
        Assert.Contains(assigneeNotifications!, item => item?["kind"]?.GetValue<string>() == "ticket_closed" && item?["ticketId"]?.GetValue<string>() == "notify-close-001");
        Assert.DoesNotContain(adminNotifications!, item => item?["kind"]?.GetValue<string>() == "ticket_closed" && item?["ticketId"]?.GetValue<string>() == "notify-close-001");
    }

    [Fact]
    public async Task Notifications_UnreadCountMarkAllAndOwnerIsolation()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "notify-count-001",
            IssueTitle: "Notify count one",
            Description: "Assignment should notify assignee.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-04-05 12:00:00",
            UpdatedAt: "2026-04-05 12:00:00",
            CloseDate: null,
            AssigneeUserId: null));
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "notify-count-002",
            IssueTitle: "Notify count two",
            Description: "Assignment should notify assignee.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-04-05 12:05:00",
            UpdatedAt: "2026-04-05 12:05:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);
        var bulkResponse = await seniorClient.PatchAsJsonAsync("/api/bugs/bulk-allocate", new
        {
            items = new[] { new { ticketId = "notify-count-001", expectedVersion = 1 }, new { ticketId = "notify-count-002", expectedVersion = 1 } },
            assigneeUserId = TestApiFactory.DefaultUserId
        });

        using var devClient = _factory.CreateClient();
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        devClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        var countResponse = await devClient.GetAsync("/api/notifications/unread-count");
        var countBody = await countResponse.Content.ReadFromJsonAsync<JsonObject>();
        var listResponse = await devClient.GetAsync("/api/notifications?unreadOnly=true");
        var notifications = await listResponse.Content.ReadFromJsonAsync<JsonArray>();
        var notificationId = notifications?.FirstOrDefault()?["id"]?.GetValue<string>();

        var forbiddenReadResponse = await seniorClient.PatchAsync($"/api/notifications/{notificationId}/read", null);
        var markAllResponse = await devClient.PatchAsync("/api/notifications/read-all", null);
        var markAllBody = await markAllResponse.Content.ReadFromJsonAsync<JsonObject>();
        var afterCountResponse = await devClient.GetAsync("/api/notifications/unread-count");
        var afterCountBody = await afterCountResponse.Content.ReadFromJsonAsync<JsonObject>();
        var allResponse = await devClient.GetAsync("/api/notifications?unreadOnly=false");
        var allNotifications = await allResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, bulkResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);
        Assert.True(countBody?["count"]?.GetValue<int>() >= 2);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(notifications!, item => item?["ticketId"]?.GetValue<string>() == "notify-count-001");
        Assert.Contains(notifications!, item => item?["ticketId"]?.GetValue<string>() == "notify-count-002");
        Assert.False(string.IsNullOrWhiteSpace(notificationId));
        Assert.Equal(HttpStatusCode.NotFound, forbiddenReadResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, markAllResponse.StatusCode);
        Assert.True(markAllBody?["updated"]?.GetValue<int>() >= 2);
        Assert.Equal(0, afterCountBody?["count"]?.GetValue<int>());
        Assert.All(allNotifications!, item => Assert.True(item?["isRead"]?.GetValue<bool>()));
    }

}
