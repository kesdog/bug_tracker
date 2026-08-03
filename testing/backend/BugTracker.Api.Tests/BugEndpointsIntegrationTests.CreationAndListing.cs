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
    public async Task GetBugsWithoutBearerToken_Returns401Unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/bugs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateBugWithAuthorization_Returns201AndTodoTicket()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "User cannot save profile",
            description = "Save button spins forever.",
            bugType = "form_submission",
            projectId = "project-general",
            severity = "high"
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("User cannot save profile", body["issueTitle"]?.GetValue<string>());
        Assert.Equal("Save button spins forever.", body["description"]?.GetValue<string>());
        Assert.Equal("form_submission", body["bugType"]?.GetValue<string>());
        Assert.Equal("high", body["severity"]?.GetValue<string>());
        Assert.Equal("todo", body["status"]?.GetValue<string>());
        Assert.Equal(TestApiFactory.DefaultUserId, body["reporterUserId"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(body["id"]?.GetValue<string>()));
        Assert.Equal($"/api/bugs/{body["id"]?.GetValue<string>()}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetActiveBugs_ReturnsCreatedTicket()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Toolbar crash on click",
            description = "Toolbar causes app crash.",
            bugType = "crash",
            projectId = "project-general",
            severity = "urgent",
            priority = "p0"
        };

        var createResponse = await client.PostAsJsonAsync("/api/bugs", request);
        var createdBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var createdId = createdBody?["id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(createdId));

        var getResponse = await client.GetAsync("/api/bugs?status=active");
        var tickets = await getResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(tickets);
        Assert.Contains(tickets!, x => x?["id"]?.GetValue<string>() == createdId);

        var createdTicket = tickets!.FirstOrDefault(x => x?["id"]?.GetValue<string>() == createdId)?.AsObject();
        Assert.NotNull(createdTicket);
        Assert.Null(createdTicket!["description"]);
        Assert.Null(createdTicket["postResolutionReport"]);
        Assert.Null(createdTicket["reportImages"]);
    }

    [Fact]
    public async Task GetClosedBugs_ReturnsOnlyClosedTickets()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "closed-ticket-001",
            IssueTitle: "Resolved API outage",
            Description: "Was fixed in hotpatch.",
            BugType: "api",
            Status: "closed",
            Severity: "mid",
            CreatedAt: "2026-01-01 10:00:00",
            UpdatedAt: "2026-01-01 11:00:00",
            CloseDate: "2026-01-01 11:00:00",
            AssigneeUserId: null));

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "active-ticket-001",
            IssueTitle: "Still open DB lock",
            Description: "Intermittent lock timeout.",
            BugType: "database",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-01-01 12:00:00",
            UpdatedAt: "2026-01-01 12:15:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/bugs?status=closed");
        var tickets = await response.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(tickets);
        Assert.Contains(tickets!, x => x?["id"]?.GetValue<string>() == "closed-ticket-001");
        Assert.DoesNotContain(tickets!, x => x?["id"]?.GetValue<string>() == "active-ticket-001");
        Assert.All(tickets!, x => Assert.Contains(x?["status"]?.GetValue<string>(), new[] { "closed", "cancelled" }));
    }

    [Theory]
    [InlineData("unknown_type", "high", "invalid bug_type")]
    [InlineData("api", "critical", "invalid severity")]
    public async Task CreateBugWithInvalidEnums_Returns400(
        string bugType,
        string severity,
        string expectedError)
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Invalid enum test",
            description = "Should fail validation.",
            bugType,
            projectId = "project-general",
            severity
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBugWithBothAreaTags_Returns400BadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Dual area direct API attempt",
            description = "Direct callers cannot mark both product areas.",
            bugType = "api",
            projectId = "project-general",
            severity = "high",
            priority = "p1",
            tags = new[] { " front-end ", "BACK-END" }
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("front-end and back-end tags are mutually exclusive", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBugWithUrgentSeverityAndLowPriority_Returns400BadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Urgent ticket with low priority",
            description = "Direct callers must choose p0 or p1 for urgent tickets.",
            bugType = "api",
            projectId = "project-general",
            severity = "urgent",
            priority = "p2",
            tags = new[] { "back-end" }
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("urgent severity requires priority p0 or p1", body?["error"]?.GetValue<string>());
        Assert.Equal("priority must be p0 or p1 when severity is urgent", body?["fieldErrors"]?["priority"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBugWithUrgentSeverityAndMissingPriority_Returns400BadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Urgent ticket without priority",
            description = "The default p2 priority is not valid for urgent severity.",
            bugType = "api",
            projectId = "project-general",
            severity = "urgent",
            tags = new[] { "back-end" }
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("urgent severity requires priority p0 or p1", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBugWithUrgentSeverityAndP1Priority_Returns201WithNormalizedArea()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Urgent frontend outage",
            description = "Valid urgent ticket uses a top priority and one area.",
            bugType = "crash",
            projectId = "project-general",
            severity = "URGENT",
            priority = "P1",
            tags = new[] { " Front-End " }
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("urgent", body?["severity"]?.GetValue<string>());
        Assert.Equal("p1", body?["priority"]?.GetValue<string>());
        var tags = body?["tags"]?.AsArray();
        Assert.NotNull(tags);
        Assert.Single(tags!);
        Assert.Equal("front-end", tags[0]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBugWithReportImages_ReturnsImagesAndListStaysCompact()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Screenshot-backed report",
            description = "Includes image evidence from QA run.",
            bugType = "api",
            projectId = "project-general",
            severity = "mid",
            reportImages = new[]
            {
                new
                {
                    name = "screen 01.png",
                    contentType = "image/png",
                    dataUrl = TinyPngDataUrl
                }
            }
        };

        var createResponse = await client.PostAsJsonAsync("/api/bugs", request);
        var createdBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var createdId = createdBody?["id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createdBody);
        var createdImages = createdBody["reportImages"]?.AsArray();
        Assert.NotNull(createdImages);
        Assert.Single(createdImages!);
        Assert.Equal("screen-01.png", createdImages[0]?["name"]?.GetValue<string>());

        var listResponse = await client.GetAsync("/api/bugs?status=active&includeReportImages=true");
        var tickets = await listResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(tickets);
        var createdTicket = tickets!.FirstOrDefault(x => x?["id"]?.GetValue<string>() == createdId)?.AsObject();
        Assert.NotNull(createdTicket);
        Assert.Null(createdTicket!["description"]);
        Assert.Null(createdTicket["resolutionNotes"]);
        Assert.Null(createdTicket["postResolutionReport"]);
        Assert.Null(createdTicket["reportImages"]);

        var detailResponse = await client.GetAsync($"/api/bugs/{createdId}");
        var detailBody = await detailResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailImages = detailBody?["reportImages"]?.AsArray();
        Assert.NotNull(detailImages);
        Assert.Single(detailImages!);
    }

    [Fact]
    public async Task CreateBugWithStructuredFieldsAndTextEvidence_ReturnsDetailsAndListStaysCompact()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Structured checkout regression",
            description = "Checkout fails after payment confirmation.",
            bugType = "form_submission",
            projectId = "project-general",
            severity = "high",
            priority = "p1",
            tags = new[] { "front-end" },
            environment = "Chrome 126 on Linux",
            expectedBehavior = "Order confirmation page loads.",
            actualBehavior = "User remains on spinner.",
            stepsToReproduce = "1. Add item\n2. Pay\n3. Observe spinner",
            frequency = "frequent",
            textEvidence = new[]
            {
                new { name = "console-log.txt", contentType = "text/plain", text = "POST /checkout 500" }
            }
        };

        var createResponse = await client.PostAsJsonAsync("/api/bugs", request);
        var createdBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var createdId = createdBody?["id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("p1", createdBody?["priority"]?.GetValue<string>());
        Assert.Equal("Chrome 126 on Linux", createdBody?["environment"]?.GetValue<string>());
        Assert.Equal("Order confirmation page loads.", createdBody?["expectedBehavior"]?.GetValue<string>());
        Assert.Equal("User remains on spinner.", createdBody?["actualBehavior"]?.GetValue<string>());
        Assert.Equal("frequent", createdBody?["frequency"]?.GetValue<string>());
        Assert.Single(createdBody?["tags"]?.AsArray()!);
        Assert.Single(createdBody?["textEvidence"]?.AsArray()!);

        var listResponse = await client.GetAsync("/api/bugs?status=active&limit=100");
        var tickets = await listResponse.Content.ReadFromJsonAsync<JsonArray>();
        var listTicket = tickets!.FirstOrDefault(x => x?["id"]?.GetValue<string>() == createdId)?.AsObject();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(listTicket);
        Assert.Equal("p1", listTicket!["priority"]?.GetValue<string>());
        Assert.Null(listTicket["textEvidence"]);

        var detailResponse = await client.GetAsync($"/api/bugs/{createdId}");
        var detailBody = await detailResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Single(detailBody?["textEvidence"]?.AsArray()!);
        Assert.Equal("console-log.txt", detailBody?["textEvidence"]?[0]?["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBugWithNonTxtEvidence_Returns400BadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Bad evidence type",
            description = "Should reject non-text evidence.",
            bugType = "api",
            projectId = "project-general",
            severity = "low",
            textEvidence = new[]
            {
                new { name = "payload.json", contentType = "application/json", text = "{}" }
            }
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text evidence supports text/plain files only", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBugWithTooManyImages_Returns400BadRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var reportImages = Enumerable.Range(1, 4)
            .Select(index => new
            {
                name = $"img-{index}.png",
                contentType = "image/png",
                dataUrl = "data:image/png;base64,aGVsbG8="
            })
            .ToArray();

        var request = new
        {
            issueTitle = "Too many files",
            description = "Should fail max image count validation.",
            bugType = "api",
            projectId = "project-general",
            severity = "low",
            reportImages
        };

        var response = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("a maximum of 3 report images is allowed", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateAndListBugs_AverageCase_PerformanceUnder500Ms()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            issueTitle = "Average path perf",
            description = "Measure round-trip create + list for healthy baseline.",
            bugType = "api",
            projectId = "project-general",
            severity = "low"
        };

        var stopwatch = Stopwatch.StartNew();
        var createResponse = await client.PostAsJsonAsync("/api/bugs", request);
        var listResponse = await client.GetAsync("/api/bugs?status=active&limit=10&sort=created_at_desc");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Expected under 500ms but took {stopwatch.ElapsedMilliseconds}ms");
    }

}
