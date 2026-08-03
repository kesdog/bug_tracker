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
    public async Task AiAgentLifecycle_AdminIssuesApiKey_AgentCreatesBug_SeniorAllocatesToAgent_AndAgentClosesTicket()
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var requestResponse = await adminClient.PostAsJsonAsync("/api/auth/requests", new
        {
            email = $"agent.lifecycle.{Guid.NewGuid():N}@example.com",
            requestType = "ai_agent"
        });
        var requestBody = await requestResponse.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        var apiKeyResponse = await adminClient.PostAsJsonAsync($"/api/auth/requests/{requestId}/issue-api-key", new { activeDays = 30 });
        var apiKeyBody = await apiKeyResponse.Content.ReadFromJsonAsync<JsonObject>();
        var apiKey = apiKeyBody?["apiKey"]?.GetValue<string>();
        var username = apiKeyBody?["username"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, apiKeyResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        Assert.False(string.IsNullOrWhiteSpace(username));

        var requestsResponse = await adminClient.GetAsync("/api/auth/requests");
        var requestsBody = await requestsResponse.Content.ReadFromJsonAsync<JsonArray>();
        var approvedRequest = requestsBody?
            .FirstOrDefault(item => item?["requestId"]?.GetValue<string>() == requestId)?
            .AsObject();

        Assert.Equal(HttpStatusCode.OK, requestsResponse.StatusCode);
        Assert.NotNull(approvedRequest);
        Assert.Equal("approved", approvedRequest!["status"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(approvedRequest["userId"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(approvedRequest["apiKeyPrefix"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(approvedRequest["apiKeyExpiresAt"]?.GetValue<string>()));

        using var agentLoginClient = _factory.CreateClient();
        var agentLoginResponse = await agentLoginClient.PostAsJsonAsync("/api/auth/agent/login", new { username, oathToken = apiKey });
        var agentLoginBody = await agentLoginResponse.Content.ReadFromJsonAsync<JsonObject>();
        var agentToken = agentLoginBody?["accessToken"]?.GetValue<string>();
        var agentUserId = agentLoginBody?["user"]?["userId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, agentLoginResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(agentToken));
        Assert.False(string.IsNullOrWhiteSpace(agentUserId));

        var projectResponse = await adminClient.PostAsJsonAsync("/api/projects", new { name = $"AI Lifecycle {Guid.NewGuid().ToString("N")[..6]}" });
        var projectBody = await projectResponse.Content.ReadFromJsonAsync<JsonObject>();
        var projectId = projectBody?["projectId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(projectId));

        var allocationResponse = await adminClient.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.DefaultUserId, agentUserId }
        });

        Assert.Equal(HttpStatusCode.OK, allocationResponse.StatusCode);

        using var agentClient = _factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        var createBugResponse = await agentClient.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = "Agent reported production regression",
            description = "AI agent detected a regression during automated validation.",
            bugType = "api",
            projectId,
            severity = "high",
            reportImages = new[]
            {
                new { name = "agent-capture.png", contentType = "image/png", dataUrl = TinyPngDataUrl }
            }
        });
        var createBugBody = await createBugResponse.Content.ReadFromJsonAsync<JsonObject>();
        var bugId = createBugBody?["id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createBugResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(bugId));
        Assert.Equal(agentUserId, createBugBody?["reporterUserId"]?.GetValue<string>());
        Assert.Equal("todo", createBugBody?["status"]?.GetValue<string>());
        Assert.Single(createBugBody?["reportImages"]?.AsArray()!);

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);

        var allocateBugResponse = await seniorClient.PatchAsJsonAsync($"/api/bugs/{bugId}/allocate", new
        {
            assigneeUserId = agentUserId,
            expectedVersion = 1
        });
        var allocateBugBody = await allocateBugResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, allocateBugResponse.StatusCode);
        Assert.Equal(agentUserId, allocateBugBody?["assigneeUserId"]?.GetValue<string>());
        Assert.Equal("open", allocateBugBody?["status"]?.GetValue<string>());

        var allocatedListResponse = await agentClient.GetAsync("/api/bugs/allocated?limit=100&includeReportImages=true");
        var allocatedListBody = await allocatedListResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, allocatedListResponse.StatusCode);
        Assert.NotNull(allocatedListBody);
        Assert.Contains(allocatedListBody!, item => item?["id"]?.GetValue<string>() == bugId);

        var updateReportResponse = await agentClient.PatchAsJsonAsync($"/api/bugs/{bugId}/report", new
        {
            reportText = "Investigated logs and verified the fix [[img:0]]",
            reportImages = new[]
            {
                new { name = "fix-proof.png", contentType = "image/png", dataUrl = TinyPngDataUrl }
            },
            expectedVersion = 2
        });
        var updateReportBody = await updateReportResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, updateReportResponse.StatusCode);
        Assert.Equal("Investigated logs and verified the fix [[img:0]]", updateReportBody?["postResolutionReport"]?.GetValue<string>());
        Assert.Single(updateReportBody?["reportImages"]?.AsArray()!);
        Assert.Single(updateReportBody?["resolutionReportImages"]?.AsArray()!);

        var closeBugResponse = await agentClient.PatchAsJsonAsync($"/api/bugs/{bugId}/close", new
        {
            resolutionNotes = "Patched the handler and confirmed normal responses [[img:0]]",
            reportImages = new[]
            {
                new { name = "resolved.png", contentType = "image/png", dataUrl = TinyPngDataUrl }
            },
            expectedVersion = 3
        });
        var closeBugBody = await closeBugResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, closeBugResponse.StatusCode);
        Assert.Equal("closed", closeBugBody?["status"]?.GetValue<string>());
        Assert.Equal("Patched the handler and confirmed normal responses [[img:0]]", closeBugBody?["resolutionNotes"]?.GetValue<string>());
        Assert.Equal(agentUserId, closeBugBody?["resolvedByUserId"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(closeBugBody?["closeDate"]?.GetValue<string>()));

        var closedListResponse = await seniorClient.GetAsync("/api/bugs?status=closed&limit=100");
        var closedListBody = await closedListResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, closedListResponse.StatusCode);
        Assert.NotNull(closedListBody);
        Assert.Contains(closedListBody!, item => item?["id"]?.GetValue<string>() == bugId && item?["status"]?.GetValue<string>() == "closed");

        var closedDetailResponse = await seniorClient.GetAsync($"/api/bugs/{bugId}");
        var closedDetailBody = await closedDetailResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, closedDetailResponse.StatusCode);
        Assert.Equal(agentUserId, closedDetailBody?["reporterUserId"]?.GetValue<string>());
        Assert.Equal(agentUserId, closedDetailBody?["assigneeUserId"]?.GetValue<string>());
        Assert.Equal(agentUserId, closedDetailBody?["resolvedByUserId"]?.GetValue<string>());
        Assert.Equal("closed", closedDetailBody?["status"]?.GetValue<string>());
        Assert.Equal("Patched the handler and confirmed normal responses [[img:0]]", closedDetailBody?["resolutionNotes"]?.GetValue<string>());
        Assert.Single(closedDetailBody?["reportImages"]?.AsArray()!);
        Assert.Single(closedDetailBody?["resolutionReportImages"]?.AsArray()!);
    }

    [Fact]
    public async Task TicketAttachments_AssignedAgentUploadsImagesAndDownloadsOnDemand()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "agent-attachment-ticket-001",
            IssueTitle: "Agent attachment upload",
            Description: "Assigned agent can attach proof images.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-04-01 10:00:00",
            UpdatedAt: "2026-04-01 10:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.AgentUserId));

        using var agentClient = _factory.CreateClient();
        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        var firstImage = TinyPngBytes();
        var secondImage = TinyPngBytes();
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent("1"), "expectedVersion");
        uploadContent.Add(new StringContent("solution-report"), "purpose");
        uploadContent.Add(ImageContent(firstImage), "files", "proof-one.png");
        uploadContent.Add(ImageContent(secondImage), "files", "proof-two.png");

        var uploadResponse = await agentClient.PostAsync("/api/bugs/agent-attachment-ticket-001/attachments", uploadContent);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonObject>();
        var attachments = uploadBody?["attachments"]?.AsArray();
        var firstAttachmentId = attachments?[0]?["id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        Assert.Equal(2, uploadBody?["version"]?.GetValue<int>());
        Assert.NotNull(attachments);
        Assert.Equal(2, attachments!.Count);
        Assert.Equal("proof-one.png", attachments[0]?["name"]?.GetValue<string>());
        Assert.Equal("image/png", attachments[0]?["contentType"]?.GetValue<string>());
        Assert.Equal("image", attachments[0]?["kind"]?.GetValue<string>());
        Assert.Equal(1, attachments[0]?["width"]?.GetValue<int>());
        Assert.Equal(1, attachments[0]?["height"]?.GetValue<int>());
        Assert.False(string.IsNullOrWhiteSpace(firstAttachmentId));

        var detailResponse = await agentClient.GetAsync("/api/bugs/agent-attachment-ticket-001");
        var detailBody = await detailResponse.Content.ReadFromJsonAsync<JsonObject>();
        var detailAttachments = detailBody?["attachments"]?.AsArray();

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(detailBody?["version"]?.GetValue<int>(), uploadBody?["version"]?.GetValue<int>());
        Assert.NotNull(detailAttachments);
        Assert.Equal(2, detailAttachments!.Count);
        Assert.Equal(firstAttachmentId, detailAttachments[0]?["id"]?.GetValue<string>());
        Assert.Null(detailAttachments[0]?["content"]);

        var downloadResponse = await agentClient.GetAsync($"/api/bugs/agent-attachment-ticket-001/attachments/{firstAttachmentId}");
        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("image/png", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(downloadedBytes);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, downloadedBytes[..4]);
    }

    [Fact]
    public async Task TicketAttachments_MoreThanThreeImages_Returns413PayloadTooLarge()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "agent-attachment-ticket-002",
            IssueTitle: "Agent attachment cap",
            Description: "Assigned agent cannot exceed image cap.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-04-01 11:00:00",
            UpdatedAt: "2026-04-01 11:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.AgentUserId));

        using var agentClient = _factory.CreateClient();
        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent("1"), "expectedVersion");
        uploadContent.Add(new StringContent("solution-report"), "purpose");
        for (var i = 0; i < 4; i++)
        {
            uploadContent.Add(ImageContent(TinyPngBytes()), "files", $"proof-{i}.png");
        }

        var response = await agentClient.PostAsync("/api/bugs/agent-attachment-ticket-002/attachments", uploadContent);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("a maximum of 3 images can be uploaded", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task TicketAttachments_ImageAbove4K_Returns422UnprocessableEntity()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "agent-attachment-ticket-003",
            IssueTitle: "Agent attachment resolution cap",
            Description: "Assigned agent cannot upload above 4K.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-04-01 12:00:00",
            UpdatedAt: "2026-04-01 12:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.AgentUserId));

        using var agentClient = _factory.CreateClient();
        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent("1"), "expectedVersion");
        uploadContent.Add(new StringContent("solution-report"), "purpose");
        uploadContent.Add(ImageContent(CreatePngBytes(3841, 2160)), "files", "too-wide.png");

        var response = await agentClient.PostAsync("/api/bugs/agent-attachment-ticket-003/attachments", uploadContent);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("3840x2160", body?["error"]?.GetValue<string>());
    }

}
