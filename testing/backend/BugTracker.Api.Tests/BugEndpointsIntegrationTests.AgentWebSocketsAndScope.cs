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
    public async Task AgentNotificationWebSocket_AssignedAgentReceivesTicketAssignedEvent()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "agent-ws-ticket-001",
            IssueTitle: "Agent websocket assignment",
            Description: "Assigned agent should receive websocket event.",
            BugType: "api",
            Status: "todo",
            Severity: "high",
            CreatedAt: "2026-04-01 13:00:00",
            UpdatedAt: "2026-04-01 13:00:00",
            CloseDate: null,
            AssigneeUserId: null));

        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        var webSocketClient = _factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request => request.Headers["Authorization"] = $"Bearer {agentToken}";

        using var socket = await webSocketClient.ConnectAsync(new Uri("ws://localhost/api/agent/notifications/ws"), CancellationToken.None);
        var hello = await ReceiveWebSocketJsonAsync(socket);

        Assert.Equal("hello", hello["type"]?.GetValue<string>());
        Assert.Equal(TestApiFactory.AgentUserId, hello["userId"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(hello["tokenExpiresAt"]?.GetValue<string>()));
        Assert.True(hello["maxDurationSeconds"]?.GetValue<int>() > 0);
        Assert.Equal(30, hello["heartbeat"]?["intervalSeconds"]?.GetValue<int>());
        Assert.Equal(15, hello["heartbeat"]?["retryIntervalSeconds"]?.GetValue<int>());
        Assert.Equal(5, hello["heartbeat"]?["maxRetries"]?.GetValue<int>());
        Assert.Contains("fetch links.ticket", hello["agentInstructions"]?["requiredWorkflow"]?.GetValue<string>());
        Assert.Contains("markNotificationReadPath", hello["agentInstructions"]?["completionWorkflow"]?.GetValue<string>());
        Assert.Contains("POST a comment", hello["agentInstructions"]?["unableToResolveAction"]?.GetValue<string>());

        await SendWebSocketJsonAsync(socket, "{\"type\":\"ping\"}");
        var pong = await ReceiveWebSocketJsonAsync(socket);
        Assert.Equal("pong", pong["type"]?.GetValue<string>());

        using var seniorClient = _factory.CreateClient();
        var seniorToken = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        seniorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seniorToken);
        var allocateResponse = await seniorClient.PatchAsJsonAsync("/api/bugs/agent-ws-ticket-001/allocate", new { assigneeUserId = TestApiFactory.AgentUserId, expectedVersion = 1 });
        var assigned = await ReceiveWebSocketJsonAsync(socket);

        Assert.Equal(HttpStatusCode.OK, allocateResponse.StatusCode);
        Assert.Equal("ticket.assigned", assigned["type"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(assigned["eventId"]?.GetValue<string>()));
        Assert.Equal(2, assigned["ticketVersion"]?.GetValue<int>());
        Assert.True(assigned["actionRequired"]?.GetValue<bool>());
        Assert.Equal("ticket_assigned", assigned["notification"]?["kind"]?.GetValue<string>());
        Assert.Equal("agent-ws-ticket-001", assigned["notification"]?["ticketId"]?.GetValue<string>());
        Assert.Equal($"/api/bugs/agent-ws-ticket-001", assigned["links"]?["ticket"]?.GetValue<string>());
        Assert.Equal($"/api/bugs/agent-ws-ticket-001", assigned["agentInstructions"]?["ticketDetailPath"]?.GetValue<string>());
        Assert.Equal($"/api/bugs/agent-ws-ticket-001/comments", assigned["agentInstructions"]?["commentPath"]?.GetValue<string>());
        Assert.Equal($"/api/notifications/{assigned["notification"]?["id"]?.GetValue<string>()}/read", assigned["agentInstructions"]?["markNotificationReadPath"]?.GetValue<string>());
        Assert.Contains("mark this notification read", assigned["agentInstructions"]?["completionAction"]?.GetValue<string>());
        Assert.Contains("Do not receive and ignore", assigned["agentInstructions"]?["requiredWorkflow"]?.GetValue<string>());
        Assert.Contains("do not change ticket state", assigned["agentInstructions"]?["safetyNote"]?.GetValue<string>());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task AgentNotificationWebSocket_AssignedAgentReceivesTicketCommentedEvent()
    {
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "agent-ws-comment-ticket-001",
            IssueTitle: "Agent websocket comment",
            Description: "Assigned agent should receive comment websocket event.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-04-01 13:30:00",
            UpdatedAt: "2026-04-01 13:30:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.AgentUserId));

        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        var webSocketClient = _factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request => request.Headers["Authorization"] = $"Bearer {agentToken}";

        using var socket = await webSocketClient.ConnectAsync(new Uri("ws://localhost/api/agent/notifications/ws"), CancellationToken.None);
        var hello = await ReceiveWebSocketJsonAsync(socket);
        Assert.Equal("hello", hello["type"]?.GetValue<string>());

        using var reporterClient = _factory.CreateClient();
        var reporterToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        reporterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reporterToken);
        var commentResponse = await reporterClient.PostAsJsonAsync("/api/bugs/agent-ws-comment-ticket-001/comments", new
        {
            body = "Please inspect the latest reproduction details."
        });
        var commented = await ReceiveWebSocketJsonAsync(socket);

        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);
        Assert.Equal("ticket.commented", commented["type"]?.GetValue<string>());
        Assert.Equal("ticket_commented", commented["notification"]?["kind"]?.GetValue<string>());
        Assert.Equal("agent-ws-comment-ticket-001", commented["notification"]?["ticketId"]?.GetValue<string>());
        Assert.Equal("Ticket agent-ws-comment-ticket-001 has a new comment.", commented["notification"]?["message"]?.GetValue<string>());
        Assert.Equal("/api/bugs/agent-ws-comment-ticket-001", commented["links"]?["ticket"]?.GetValue<string>());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task AgentNotificationWebSocket_HumanTokenReturns403()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/agent/notifications/ws");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AgentNotificationWebSocket_LogoutRevocationClosesEstablishedSessionAtHeartbeat()
    {
        using var factory = TestApiFactory.WithAgentHeartbeatInterval(1);
        var agentToken = await factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request => request.Headers["Authorization"] = $"Bearer {agentToken}";

        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/api/agent/notifications/ws"), CancellationToken.None);
        Assert.Equal("hello", (await ReceiveWebSocketJsonAsync(socket))["type"]?.GetValue<string>());

        using var logoutClient = factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);
        Assert.Equal(HttpStatusCode.NoContent, (await logoutClient.PostAsync("/api/auth/logout", null)).StatusCode);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WebSocketReceiveResult result;
        do
        {
            var buffer = new byte[256];
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var payload = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))?.AsObject();
                if (payload?["type"]?.GetValue<string>() == "ping")
                {
                    await SendWebSocketJsonAsync(socket, "{\"type\":\"pong\"}");
                }
            }
        } while (result.MessageType != WebSocketMessageType.Close);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, result.CloseStatus);
    }

    [Fact]
    public async Task AgentNotificationWebSocket_AuditsSuccessfulConnectionAndDisconnect()
    {
        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        var webSocketClient = _factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request => request.Headers["Authorization"] = $"Bearer {agentToken}";

        using (var socket = await webSocketClient.ConnectAsync(new Uri("ws://localhost/api/agent/notifications/ws"), CancellationToken.None))
        {
            var hello = await ReceiveWebSocketJsonAsync(socket);
            Assert.Equal("hello", hello["type"]?.GetValue<string>());
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
        }

        await Task.Delay(50);

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var connectedResponse = await adminClient.GetAsync($"/api/audit-logs?actorType=agent&action=agent_ws_connected&search={TestApiFactory.AgentUserId}&limit=20");
        var connectedLogs = await connectedResponse.Content.ReadFromJsonAsync<JsonArray>();
        var disconnectedResponse = await adminClient.GetAsync($"/api/audit-logs?actorType=agent&action=agent_ws_disconnected&search={TestApiFactory.AgentUserId}&limit=20");
        var disconnectedLogs = await disconnectedResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, connectedResponse.StatusCode);
        Assert.Contains(connectedLogs!, item => item?["actorUserId"]?.GetValue<string>() == TestApiFactory.AgentUserId);
        Assert.Equal(HttpStatusCode.OK, disconnectedResponse.StatusCode);
        Assert.Contains(disconnectedLogs!, item => item?["actorUserId"]?.GetValue<string>() == TestApiFactory.AgentUserId);
    }

    [Fact]
    public async Task AgentNotificationWebSocket_AuditsMappedTokenButIgnoresCallerSuppliedUserHint()
    {
        var expiredToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId, DateTimeOffset.UtcNow.AddMinutes(-5));

        using var expiredClient = _factory.CreateClient();
        expiredClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
        var expiredResponse = await expiredClient.GetAsync("/api/agent/notifications/ws");

        using var invalidClient = _factory.CreateClient();
        invalidClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        var invalidResponse = await invalidClient.GetAsync($"/api/agent/notifications/ws?userId={TestApiFactory.AgentUserId}");

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var logsResponse = await adminClient.GetAsync($"/api/audit-logs?actorType=agent&action=agent_ws_auth_failed&search={TestApiFactory.AgentUserId}&limit=20");
        var logs = await logsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        Assert.Contains(logs!, item => item?["metadataJson"]?.GetValue<string>()?.Contains("expired_token", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(logs!, item => item?["metadataJson"]?.GetValue<string>()?.Contains("invalid_token", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task AgentScopedAccess_SeesOnlyAllocatedProjectTickets_AndCannotReadOutOfScopeTicket()
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var requestResponse = await adminClient.PostAsJsonAsync("/api/auth/requests", new
        {
            email = $"agent.scope.{Guid.NewGuid():N}@example.com",
            requestType = "ai_agent"
        });
        var requestBody = await requestResponse.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        var issueKeyResponse = await adminClient.PostAsJsonAsync($"/api/auth/requests/{requestId}/issue-api-key", new { activeDays = 30 });
        var issueKeyBody = await issueKeyResponse.Content.ReadFromJsonAsync<JsonObject>();
        var apiKey = issueKeyBody?["apiKey"]?.GetValue<string>();
        var username = issueKeyBody?["username"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, issueKeyResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        Assert.False(string.IsNullOrWhiteSpace(username));

        using var agentLoginClient = _factory.CreateClient();
        var agentLoginResponse = await agentLoginClient.PostAsJsonAsync("/api/auth/agent/login", new { username, oathToken = apiKey });
        var agentLoginBody = await agentLoginResponse.Content.ReadFromJsonAsync<JsonObject>();
        var agentToken = agentLoginBody?["accessToken"]?.GetValue<string>();
        var agentUserId = agentLoginBody?["user"]?["userId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, agentLoginResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(agentToken));
        Assert.False(string.IsNullOrWhiteSpace(agentUserId));

        var inScopeProjectName = $"Agent Scope In {Guid.NewGuid():N}"[..26];
        var outScopeProjectName = $"Agent Scope Out {Guid.NewGuid():N}"[..27];

        var inScopeProjectResponse = await adminClient.PostAsJsonAsync("/api/projects", new { name = inScopeProjectName });
        var inScopeProjectBody = await inScopeProjectResponse.Content.ReadFromJsonAsync<JsonObject>();
        var inScopeProjectId = inScopeProjectBody?["projectId"]?.GetValue<string>();

        var outScopeProjectResponse = await adminClient.PostAsJsonAsync("/api/projects", new { name = outScopeProjectName });
        var outScopeProjectBody = await outScopeProjectResponse.Content.ReadFromJsonAsync<JsonObject>();
        var outScopeProjectId = outScopeProjectBody?["projectId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, inScopeProjectResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, outScopeProjectResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(inScopeProjectId));
        Assert.False(string.IsNullOrWhiteSpace(outScopeProjectId));

        var allocateProjectResponse = await adminClient.PatchAsJsonAsync($"/api/projects/{inScopeProjectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, agentUserId }
        });
        Assert.Equal(HttpStatusCode.OK, allocateProjectResponse.StatusCode);

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "agent-scope-ticket-001",
            IssueTitle: "In-scope ticket",
            Description: "Visible to agent by project allocation.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-02-01 10:00:00",
            UpdatedAt: "2026-02-01 10:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ProjectId: inScopeProjectId));

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "agent-scope-ticket-002",
            IssueTitle: "Out-of-scope ticket",
            Description: "Must not be visible to this agent.",
            BugType: "database",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-02-01 11:00:00",
            UpdatedAt: "2026-02-01 11:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ProjectId: outScopeProjectId));

        using var agentClient = _factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        var listResponse = await agentClient.GetAsync("/api/bugs?status=active&limit=100");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(listBody);
        Assert.Contains(listBody!, item => item?["id"]?.GetValue<string>() == "agent-scope-ticket-001");
        Assert.DoesNotContain(listBody!, item => item?["id"]?.GetValue<string>() == "agent-scope-ticket-002");

        var outOfScopeDetailsResponse = await agentClient.GetAsync("/api/bugs/agent-scope-ticket-002");
        Assert.Equal(HttpStatusCode.Forbidden, outOfScopeDetailsResponse.StatusCode);
    }

}
