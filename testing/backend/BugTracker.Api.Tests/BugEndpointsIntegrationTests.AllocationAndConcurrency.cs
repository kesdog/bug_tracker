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
    public async Task AllocateBug_AsDevRole_Returns403Forbidden()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "alloc-dev-forbidden-001",
            IssueTitle: "Allocation forbidden for dev",
            Description: "Dev role should not allocate bugs.",
            BugType: "api",
            Status: "todo",
            Severity: "low",
            CreatedAt: "2026-01-01 10:00:00",
            UpdatedAt: "2026-01-01 10:00:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/alloc-dev-forbidden-001/allocate", new { assigneeUserId = TestApiFactory.SeniorUserId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AllocateBug_AsSeniorRole_UpdatesAssignee()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "alloc-senior-ok-001",
            IssueTitle: "Senior can allocate",
            Description: "Needs assignment flow.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-01-01 10:00:00",
            UpdatedAt: "2026-01-01 10:00:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/alloc-senior-ok-001/allocate", new { assigneeUserId = TestApiFactory.DefaultUserId, expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestApiFactory.DefaultUserId, body?["assigneeUserId"]?.GetValue<string>());
    }

    [Fact]
    public async Task AllocateBug_WithStaleExpectedVersion_Returns409AndDoesNotDuplicateSideEffects()
    {
        const string ticketId = "alloc-version-conflict-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Concurrent assignment", "Only one assignment wins.",
            "api", "todo", "mid", "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, null));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId));

        var first = await client.PatchAsJsonAsync($"/api/bugs/{ticketId}/allocate", new
        {
            assigneeUserId = TestApiFactory.DefaultUserId,
            expectedVersion = 1
        });
        var stale = await client.PatchAsJsonAsync($"/api/bugs/{ticketId}/allocate", new
        {
            assigneeUserId = TestApiFactory.AgentUserId,
            expectedVersion = 1
        });
        var conflict = await stale.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("ticket_version_conflict", conflict?["errorCode"]?.GetValue<string>());
        Assert.Equal(1, conflict?["expectedVersion"]?.GetValue<int>());
        Assert.Equal(2, conflict?["currentVersion"]?.GetValue<int>());
        Assert.Contains("assigneeUserId", conflict?["changedFields"]?.AsArray().Select(node => node!.GetValue<string>()) ?? []);
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM ticket_activity WHERE ticket_id = $id AND kind = 'assigned';", ticketId));
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE ticket_id = $id AND action = 'ticket_assigned';", ticketId));
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE aggregate_id = $id AND event_type = 'notification.websocket';", ticketId));
    }

    [Fact]
    public async Task TicketWrites_RejectNonPositiveExpectedVersionForHumanAndAgent()
    {
        const string ticketId = "invalid-expected-version-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Invalid expected version", "Validate before writing.",
            "api", "open", "mid", "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, TestApiFactory.AgentUserId));

        using var human = _factory.CreateClient();
        human.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId));
        var humanResponse = await human.PatchAsJsonAsync($"/api/bugs/{ticketId}/allocate", new
        {
            assigneeUserId = TestApiFactory.DefaultUserId,
            expectedVersion = 0
        });

        using var agent = _factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId));
        var agentResponse = await agent.PatchAsJsonAsync($"/api/bugs/{ticketId}/report", new
        {
            reportText = "Must not write.",
            reportImages = Array.Empty<object>(),
            expectedVersion = -1
        });

        Assert.Equal(HttpStatusCode.BadRequest, humanResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, agentResponse.StatusCode);
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT version FROM bug_tickets WHERE id = $id;", ticketId));
    }

    [Fact]
    public async Task TicketAggregateMutations_MissingExpectedVersion_Return428ForHumansAndAgents()
    {
        const string activeId = "required-version-active-001";
        const string closedId = "required-version-closed-001";
        await _factory.SeedBugAsync(new SeedBugRequest(activeId, "Required version", "Original.", "api", "open", "mid",
            "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, TestApiFactory.AgentUserId));
        await _factory.SeedBugAsync(new SeedBugRequest(closedId, "Required reopen version", "Original.", "api", "closed", "mid",
            "2026-01-01 10:00:00", "2026-01-01 10:00:00", "2026-01-01 11:00:00", TestApiFactory.AgentUserId));

        using var senior = _factory.CreateClient();
        senior.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId));
        using var agent = _factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId));

        var responses = new List<HttpResponseMessage>
        {
            await senior.PatchAsJsonAsync($"/api/bugs/{activeId}/allocate", new { assigneeUserId = TestApiFactory.DefaultUserId }),
            await senior.PatchAsJsonAsync("/api/bugs/bulk-allocate", new { ticketIds = new[] { activeId }, assigneeUserId = TestApiFactory.DefaultUserId }),
            await senior.PatchAsJsonAsync("/api/bugs/bulk-allocate", new { items = new[] { new { ticketId = activeId } }, assigneeUserId = TestApiFactory.DefaultUserId }),
            await agent.PatchAsJsonAsync($"/api/bugs/{activeId}/metadata", new { issueTitle = "No version" }),
            await agent.PatchAsJsonAsync($"/api/bugs/{activeId}/initial-report", new { reportText = "No version" }),
            await agent.PatchAsJsonAsync($"/api/bugs/{activeId}/report", new { reportText = "No version" }),
            await agent.PatchAsJsonAsync($"/api/bugs/{activeId}/close", new { resolutionNotes = "No version" }),
            await agent.PatchAsJsonAsync($"/api/bugs/{closedId}/reopen", new { reason = "No version" })
        };

        using var attachment = new MultipartFormDataContent();
        attachment.Add(new StringContent("solution-report"), "purpose");
        attachment.Add(ImageContent(TinyPngBytes()), "files", "missing-version.png");
        responses.Add(await agent.PostAsync($"/api/bugs/{activeId}/attachments", attachment));

        foreach (var response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
            Assert.Equal("ticket_version_required", body?["errorCode"]?.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(body?["error"]?.GetValue<string>()));
            Assert.Contains("Fetch", body?["recovery"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT version FROM bug_tickets WHERE id = $id;", activeId));
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT version FROM bug_tickets WHERE id = $id;", closedId));
    }

    [Fact]
    public async Task AgentWrite_WithStaleExpectedVersion_UsesSame409Contract()
    {
        const string ticketId = "agent-version-conflict-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Agent conflict", "Original.", "api", "open", "mid",
            "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, TestApiFactory.AgentUserId));
        using var agent = _factory.CreateClient();
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId));

        var first = await agent.PatchAsJsonAsync($"/api/bugs/{ticketId}/report", new
        {
            reportText = "Committed agent update.", reportImages = Array.Empty<object>(), expectedVersion = 1
        });
        var stale = await agent.PatchAsJsonAsync($"/api/bugs/{ticketId}/report", new
        {
            reportText = "Stale agent update.", reportImages = Array.Empty<object>(), expectedVersion = 1
        });
        var conflict = await stale.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("ticket_version_conflict", conflict?["errorCode"]?.GetValue<string>());
        Assert.Equal(1, conflict?["expectedVersion"]?.GetValue<int>());
        Assert.Equal(2, conflict?["currentVersion"]?.GetValue<int>());
    }

    [Fact]
    public async Task RepositoryWrite_RevalidatesSensitiveMembershipInsideTransaction()
    {
        var connectionFactory = _factory.Services.GetRequiredService<SqliteConnectionFactory>();
        var repository = _factory.Services.GetRequiredService<BugRepository>();
        var projectId = $"project-sensitive-race-{Guid.NewGuid():N}";
        await using (var connection = await connectionFactory.OpenConnectionAsync(readOnly: false))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO projects (project_id, name, visibility)
                VALUES ($project, $name, 'sensitive');
                INSERT INTO project_allocations (project_id, user_id) VALUES ($project, $user);
                """;
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$name", projectId);
            command.Parameters.AddWithValue("$user", TestApiFactory.DefaultUserId);
            await command.ExecuteNonQueryAsync();
        }

        const string ticketId = "sensitive-membership-race-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Membership race", "Original.", "api", "open", "mid",
            "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, TestApiFactory.DefaultUserId, ProjectId: projectId));

        await using (var connection = await connectionFactory.OpenConnectionAsync(readOnly: false))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM project_allocations WHERE project_id = $project AND user_id = $user;";
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$user", TestApiFactory.DefaultUserId);
            await command.ExecuteNonQueryAsync();
        }

        var result = await repository.UpdateInitialBugReportAsync(ticketId, "Unauthorized stale write.", [],
            TestApiFactory.DefaultUserId, "human", 1, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Null(result.Value);
        Assert.Equal("forbidden", result.ErrorCode);
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT version FROM bug_tickets WHERE id = $id;", ticketId));
    }

    [Fact]
    public async Task Reassignment_MarksOldAssignmentNotificationObsolete()
    {
        const string ticketId = "obsolete-assignment-notification-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Reassignment", "Only current assignee should be notified.",
            "api", "todo", "mid", "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, null));
        using var senior = _factory.CreateClient();
        senior.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId));

        Assert.Equal(HttpStatusCode.OK, (await senior.PatchAsJsonAsync($"/api/bugs/{ticketId}/allocate", new { assigneeUserId = TestApiFactory.DefaultUserId, expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await senior.PatchAsJsonAsync($"/api/bugs/{ticketId}/allocate", new { assigneeUserId = TestApiFactory.AgentUserId, expectedVersion = 2 })).StatusCode);

        Assert.Equal(0, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM notifications WHERE ticket_id = $id AND user_id = 'usr_test_dev_001' AND kind = 'ticket_assigned' AND is_read = 0;", ticketId));
        Assert.Equal(1, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM notifications WHERE ticket_id = $id AND user_id = 'usr_test_agent_001' AND kind = 'ticket_assigned' AND is_read = 0;", ticketId));
    }

    [Fact]
    public async Task EditThenStaleClose_Returns409WithAccurateChangedFields()
    {
        const string ticketId = "stale-close-after-edit-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Concurrent close", "Original report.", "api", "open", "mid",
            "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, TestApiFactory.DefaultUserId));
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId));

        var edit = await client.PatchAsJsonAsync($"/api/bugs/{ticketId}/initial-report", new
        {
            reportText = "A newer report.",
            reportImages = Array.Empty<object>(),
            expectedVersion = 1
        });
        var staleEdit = await client.PatchAsJsonAsync($"/api/bugs/{ticketId}/initial-report", new
        {
            reportText = "A stale overwrite.",
            reportImages = Array.Empty<object>(),
            expectedVersion = 1
        });
        var close = await client.PatchAsJsonAsync($"/api/bugs/{ticketId}/close", new
        {
            resolutionNotes = "Stale resolution.",
            expectedVersion = 1
        });
        var conflict = await close.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
        Assert.Equal("open", conflict?["currentStatus"]?.GetValue<string>());
        Assert.Contains("description", conflict?["changedFields"]?.AsArray().Select(node => node!.GetValue<string>()) ?? []);
        Assert.Equal(0, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM ticket_activity WHERE ticket_id = $id AND kind = 'closed';", ticketId));
    }

    [Fact]
    public async Task AttachmentUpload_WithStaleVersion_Returns409AndCreatesNoAttachment()
    {
        const string ticketId = "stale-attachment-001";
        await _factory.SeedBugAsync(new SeedBugRequest(ticketId, "Concurrent attachment", "Attachment race.", "api", "open", "mid",
            "2026-01-01 10:00:00", "2026-01-01 10:00:00", null, TestApiFactory.DefaultUserId));
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId));
        var edit = await client.PatchAsJsonAsync($"/api/bugs/{ticketId}/initial-report", new
        {
            reportText = "Version two.", reportImages = Array.Empty<object>(), expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("initial-report"), "purpose");
        content.Add(new StringContent("1"), "expectedVersion");
        content.Add(new ByteArrayContent(TinyPngBytes()) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "files", "stale.png");
        var response = await client.PostAsync($"/api/bugs/{ticketId}/attachments", content);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await _factory.ScalarLongAsync("SELECT COUNT(*) FROM ticket_attachments WHERE ticket_id = $id;", ticketId));
    }

    [Fact]
    public async Task AllocateBug_ToAiAgent_AllowsHumanSeniorAsProjectSupervisor()
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var projectResponse = await adminClient.PostAsJsonAsync("/api/projects", new { name = $"Senior AI Supervisor {Guid.NewGuid().ToString("N")[..6]}" });
        var projectBody = await projectResponse.Content.ReadFromJsonAsync<JsonObject>();
        var projectId = projectBody?["projectId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(projectId));

        var allocationResponse = await adminClient.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.SeniorUserId }
        });
        Assert.Equal(HttpStatusCode.OK, allocationResponse.StatusCode);

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "alloc-ai-senior-supervised-001",
            IssueTitle: "AI assignment with senior supervisor",
            Description: "Senior developers should count as human supervisors for AI work.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-01-08 10:00:00",
            UpdatedAt: "2026-01-08 10:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ProjectId: projectId));

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);

        var response = await seniorClient.PatchAsJsonAsync("/api/bugs/alloc-ai-senior-supervised-001/allocate", new { assigneeUserId = TestApiFactory.AgentUserId, expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestApiFactory.AgentUserId, body?["assigneeUserId"]?.GetValue<string>());
    }

    [Fact]
    public async Task AllocateBug_ToAiAgentWithoutHumanDeveloperOnProject_ReturnsSpecificError()
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var projectResponse = await adminClient.PostAsJsonAsync("/api/projects", new { name = $"Admin Only AI {Guid.NewGuid().ToString("N")[..6]}" });
        var projectBody = await projectResponse.Content.ReadFromJsonAsync<JsonObject>();
        var projectId = projectBody?["projectId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(projectId));

        var allocationResponse = await adminClient.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId }
        });
        Assert.Equal(HttpStatusCode.OK, allocationResponse.StatusCode);

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "alloc-ai-no-human-dev-001",
            IssueTitle: "AI assignment without human developer",
            Description: "AI assignment should explain missing human supervision.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-01-08 11:00:00",
            UpdatedAt: "2026-01-08 11:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ProjectId: projectId));

        var response = await adminClient.PatchAsJsonAsync("/api/bugs/alloc-ai-no-human-dev-001/allocate", new { assigneeUserId = TestApiFactory.AgentUserId, expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("AI agent assignment requires an active human dev or senior on the ticket project", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task AllocateBug_TodoTicket_TransitionsStatusToOpen()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "alloc-opens-ticket-001",
            IssueTitle: "Allocation opens ticket",
            Description: "Todo ticket should move to open when assigned.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-01-08 10:00:00",
            UpdatedAt: "2026-01-08 10:00:00",
            CloseDate: null,
            AssigneeUserId: null));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/alloc-opens-ticket-001/allocate", new { assigneeUserId = TestApiFactory.DefaultUserId, expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestApiFactory.DefaultUserId, body?["assigneeUserId"]?.GetValue<string>());
        Assert.Equal("open", body?["status"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(body?["assignedAt"]?.GetValue<string>()));
    }

    [Fact]
    public async Task AllocateBug_ClosedTicket_Returns404AndDoesNotReassign()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "alloc-closed-ticket-001",
            IssueTitle: "Closed ticket cannot be allocated",
            Description: "Allocation should be blocked for closed tickets.",
            BugType: "api",
            Status: "closed",
            Severity: "mid",
            CreatedAt: "2026-01-08 12:00:00",
            UpdatedAt: "2026-01-08 12:30:00",
            CloseDate: "2026-01-08 12:30:00",
            AssigneeUserId: TestApiFactory.SeniorUserId));

        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PatchAsJsonAsync("/api/bugs/alloc-closed-ticket-001/allocate", new { assigneeUserId = TestApiFactory.DefaultUserId, expectedVersion = 1 });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ticket not found", body?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task GetAssignableUsers_AsSeniorRole_ReturnsUserRoleList()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/bugs/assignees");
        var body = await response.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Contains(body!, item => item?["userId"]?.GetValue<string>() == TestApiFactory.DefaultUserId && item?["role"]?.GetValue<string>() == "dev");
        Assert.Contains(body!, item => item?["userId"]?.GetValue<string>() == TestApiFactory.SeniorUserId && item?["role"]?.GetValue<string>() == "senior");
    }

}
