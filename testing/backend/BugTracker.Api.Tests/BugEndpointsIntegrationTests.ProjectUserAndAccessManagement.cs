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
    public async Task ProjectManagement_AsSenior_CreatesProject()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var uniqueProjectName = $"Mobile App {Guid.NewGuid().ToString("N")[..6]}";
        var createResponse = await client.PostAsJsonAsync("/api/projects", new { name = uniqueProjectName });
        var createdBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var projectId = createdBody?["projectId"]?.GetValue<string>()
            ?? createdBody?["project_id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(projectId));

        var listResponse = await client.GetAsync("/api/projects");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(listBody);

        Assert.Contains(listBody!, x => x?["projectId"]?.GetValue<string>() == projectId);
    }

    [Fact]
    public async Task ProjectManagement_CanAllocateUserWhoAlreadyHasTicketOnlyAccess()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.SeniorUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync("/api/projects", new { name = $"Portal {Guid.NewGuid().ToString("N")[..6]}" });
        var createdBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>();
        var projectId = createdBody?["projectId"]?.GetValue<string>()
            ?? createdBody?["project_id"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(projectId));

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "project-association-ticket-001",
            IssueTitle: "Existing ticket association",
            Description: "User already associated with project via ticket.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-01-11 09:00:00",
            UpdatedAt: "2026-01-11 09:10:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            ProjectId: projectId));

        var allocateResponse = await client.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new { userIds = new[] { TestApiFactory.SeniorUserId, TestApiFactory.DefaultUserId } });

        Assert.Equal(HttpStatusCode.OK, allocateResponse.StatusCode);
    }

    [Fact]
    public async Task UserManagement_AsAdmin_UpdatesUserRole()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PatchAsJsonAsync($"/api/auth/users/{TestApiFactory.SeniorUserId}/role", new { role = "admin" });
        var updateBody = await updateResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("admin", updateBody?["role"]?.GetValue<string>());

        var listResponse = await client.GetAsync("/api/auth/users");
        var users = await listResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(users);
        Assert.Contains(users!, item => item?["userId"]?.GetValue<string>() == TestApiFactory.SeniorUserId && item?["role"]?.GetValue<string>() == "admin");
    }

    [Fact]
    public async Task UserManagement_UpdateUsername_RequiresAdminHuman()
    {
        using var anonymousClient = _factory.CreateClient();
        var anonymousResponse = await anonymousClient.PatchAsJsonAsync(
            $"/api/auth/users/{TestApiFactory.DefaultUserId}/username",
            new { username = "unauthorized_name" });

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        var forbiddenResponse = await devClient.PatchAsJsonAsync(
            $"/api/auth/users/{TestApiFactory.DefaultUserId}/username",
            new { username = "unauthorized_name" });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task UserManagement_UpdateUsername_NormalizesAndPersistsInUserSelectors()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var username = $"Renamed.User-{Guid.NewGuid().ToString("N")[..8]}";
        var expected = username.ToLowerInvariant();

        var updateResponse = await adminClient.PatchAsJsonAsync(
            $"/api/auth/users/{TestApiFactory.DefaultUserId}/username",
            new { username = $"  {username}  " });
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(expected, updated?["username"]?.GetValue<string>());

        var users = await (await adminClient.GetAsync("/api/auth/users")).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(users!, item => item?["userId"]?.GetValue<string>() == TestApiFactory.DefaultUserId
            && item?["username"]?.GetValue<string>() == expected);

        var assignees = await (await adminClient.GetAsync("/api/bugs/assignees")).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(assignees!, item => item?["userId"]?.GetValue<string>() == TestApiFactory.DefaultUserId
            && item?["username"]?.GetValue<string>() == expected
            && item?["email"]?.GetValue<string>() == TestApiFactory.DefaultUserEmail
            && item?["userType"]?.GetValue<string>() == "human");

        var projectUsers = await (await adminClient.GetAsync("/api/projects/allocatable-users")).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(projectUsers!, item => item?["userId"]?.GetValue<string>() == TestApiFactory.DefaultUserId
            && item?["username"]?.GetValue<string>() == expected);
    }

    [Fact]
    public async Task UserManagement_UpdateUsername_RejectsDuplicateAndInvalidValues()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);

        var duplicateResponse = await adminClient.PatchAsJsonAsync(
            $"/api/auth/users/{TestApiFactory.DefaultUserId}/username",
            new { username = "ADMIN_TEST" });
        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("username_taken", duplicate?["errorCode"]?.GetValue<string>());

        var invalidResponse = await adminClient.PatchAsJsonAsync(
            $"/api/auth/users/{TestApiFactory.DefaultUserId}/username",
            new { username = "bad username!" });
        var invalid = await invalidResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("username_invalid", invalid?["errorCode"]?.GetValue<string>());
    }

    [Fact]
    public async Task UserManagement_ListUsers_ReturnsHumanActivityAndAgentWebSocketPresence()
    {
        var devToken = await _factory.CreateAccessTokenAsync(TestApiFactory.DefaultUserId);
        Assert.False(string.IsNullOrWhiteSpace(devToken));

        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var beforeWsResponse = await adminClient.GetAsync("/api/auth/users");
        var beforeWsUsers = await beforeWsResponse.Content.ReadFromJsonAsync<JsonArray>();
        var devBefore = beforeWsUsers?.FirstOrDefault(item => item?["userId"]?.GetValue<string>() == TestApiFactory.DefaultUserId)?.AsObject();
        var agentBefore = beforeWsUsers?.FirstOrDefault(item => item?["userId"]?.GetValue<string>() == TestApiFactory.AgentUserId)?.AsObject();

        Assert.Equal(HttpStatusCode.OK, beforeWsResponse.StatusCode);
        Assert.Equal("active", devBefore?["presenceStatus"]?.GetValue<string>());
        Assert.True(devBefore?["isOnline"]?.GetValue<bool>());
        Assert.Equal("offline", agentBefore?["presenceStatus"]?.GetValue<string>());
        Assert.False(agentBefore?["isOnline"]?.GetValue<bool>());

        var agentToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AgentUserId);
        var webSocketClient = _factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request => request.Headers["Authorization"] = $"Bearer {agentToken}";

        using var socket = await webSocketClient.ConnectAsync(new Uri("ws://localhost/api/agent/notifications/ws"), CancellationToken.None);
        var hello = await ReceiveWebSocketJsonAsync(socket);
        Assert.Equal("hello", hello["type"]?.GetValue<string>());

        var connectedResponse = await adminClient.GetAsync("/api/auth/users");
        var connectedUsers = await connectedResponse.Content.ReadFromJsonAsync<JsonArray>();
        var agentConnected = connectedUsers?.FirstOrDefault(item => item?["userId"]?.GetValue<string>() == TestApiFactory.AgentUserId)?.AsObject();

        Assert.Equal(HttpStatusCode.OK, connectedResponse.StatusCode);
        Assert.Equal("connected", agentConnected?["presenceStatus"]?.GetValue<string>());
        Assert.True(agentConnected?["isOnline"]?.GetValue<bool>());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    [Fact]
    public async Task UserManagement_AsAdmin_CreatesHumanRequest()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/auth/requests", new { email = "new.human@example.com", requestType = "human" });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("new.human@example.com", body?["email"]?.GetValue<string>());
        Assert.Equal("human", body?["requestType"]?.GetValue<string>());

        var requestsResponse = await client.GetAsync("/api/auth/requests");
        var requests = await requestsResponse.Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, requestsResponse.StatusCode);
        Assert.NotNull(requests);
        Assert.Contains(requests!, item => item?["email"]?.GetValue<string>() == "new.human@example.com");
    }

    [Fact]
    public async Task SetupPassword_AcceptsSixCharacterPasswordWithNumberAndSpecialCharacter()
    {
        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId));
        var email = $"setup.{Guid.NewGuid():N}@example.com";

        var requestResponse = await adminClient.PostAsJsonAsync("/api/auth/requests", new { email, requestType = "human" });
        var requestBody = await requestResponse.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();
        var setupLinkResponse = await adminClient.PostAsync($"/api/auth/requests/{requestId}/issue-setup-link", null);
        var setupLinkBody = await setupLinkResponse.Content.ReadFromJsonAsync<JsonObject>();
        var setupLink = setupLinkBody?["link"]?.GetValue<string>();
        var token = Uri.UnescapeDataString(new Uri(setupLink!).Query.Split("token=")[1]);

        var setupResponse = await _factory.CreateClient().PostAsJsonAsync("/api/auth/setup-password", new
        {
            email,
            token,
            newPassword = "lower1!"
        });

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, setupLinkResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
    }

    [Fact]
    public async Task CredentialRecovery_HumanResetLinkExpiresInThirtyMinutesAndCannotBeReused()
    {
        using var publicClient = _factory.CreateClient();
        var requestResponse = await publicClient.PostAsJsonAsync("/api/auth/request-credential-recovery", new
        {
            email = TestApiFactory.DefaultUserEmail,
            requestType = "human"
        });

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId));
        var requests = await adminClient.GetFromJsonAsync<JsonArray>("/api/auth/requests");
        var recovery = requests?.FirstOrDefault(item => item?["purpose"]?.GetValue<string>() == "credential_recovery")?.AsObject();
        var requestId = recovery?["requestId"]?.GetValue<string>()?.Replace("recovery_", string.Empty, StringComparison.Ordinal);
        var beforeIssue = DateTimeOffset.UtcNow;
        var issueResponse = await adminClient.PostAsync($"/api/auth/recovery-requests/{requestId}/issue-password-reset", null);
        var body = await issueResponse.Content.ReadFromJsonAsync<JsonObject>();
        var link = body?["link"]?.GetValue<string>();
        var expiresAt = body?["expiresAt"]?.GetValue<DateTimeOffset>();
        var token = Uri.UnescapeDataString(new Uri(link!).Query.Split("token=")[1]);

        var resetResponse = await publicClient.PostAsJsonAsync("/api/auth/setup-password", new
        {
            email = TestApiFactory.DefaultUserEmail,
            token,
            newPassword = "reset1!"
        });
        var repeatResponse = await publicClient.PostAsJsonAsync("/api/auth/setup-password", new
        {
            email = TestApiFactory.DefaultUserEmail,
            token,
            newPassword = "reset2!"
        });

        Assert.Equal(HttpStatusCode.Accepted, requestResponse.StatusCode);
        Assert.NotNull(recovery);
        Assert.Equal("human", recovery!["requestType"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);
        Assert.NotNull(expiresAt);
        Assert.InRange(expiresAt!.Value, beforeIssue.AddMinutes(30).AddSeconds(-1), beforeIssue.AddMinutes(30).AddSeconds(2));
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, repeatResponse.StatusCode);
    }

    [Fact]
    public async Task CredentialRecovery_AgentRequestAppearsAsAiAndAdminCanReissueOathToken()
    {
        using var publicClient = _factory.CreateClient();
        var requestResponse = await publicClient.PostAsJsonAsync("/api/auth/request-credential-recovery", new
        {
            email = TestApiFactory.AgentUserEmail,
            requestType = "ai_agent"
        });

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId));
        var requests = await adminClient.GetFromJsonAsync<JsonArray>("/api/auth/requests");
        var recovery = requests?.FirstOrDefault(item => item?["purpose"]?.GetValue<string>() == "credential_recovery" && item?["requestType"]?.GetValue<string>() == "ai_agent")?.AsObject();
        var recoveryId = recovery?["requestId"]?.GetValue<string>()?.Replace("recovery_", string.Empty, StringComparison.Ordinal);
        var issueResponse = await adminClient.PostAsJsonAsync($"/api/auth/recovery-requests/{recoveryId}/issue-api-key", new { activeDays = 14 });
        var body = await issueResponse.Content.ReadFromJsonAsync<JsonObject>();
        var oathToken = body?["apiKey"]?.GetValue<string>();
        var username = body?["username"]?.GetValue<string>();

        using var agentClient = _factory.CreateClient();
        var loginResponse = await agentClient.PostAsJsonAsync("/api/auth/agent/login", new { username, oathToken });

        Assert.Equal(HttpStatusCode.Accepted, requestResponse.StatusCode);
        Assert.NotNull(recovery);
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(oathToken));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task RequestAccess_AllowsUnauthenticatedHumanRequestSubmission()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/request-access", new { email = "public.request@example.com", requestType = "human" });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("If eligible, the access request will be reviewed.", body?["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task RequestAccess_InDemo_AcceptsAnyFormatValidEmailWithoutDelivery()
    {
        using var factory = CreateDemoFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/request-access", new
        {
            email = $"visitor.{Guid.NewGuid():N}@company.test",
            requestType = "human"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("No email is sent", body?["message"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("human")]
    [InlineData("ai_agent")]
    public async Task RequestAccess_InDemo_AllowsFormatValidEmailsForBothRequestTypes(string requestType)
    {
        using var factory = CreateDemoFactory();
        using var client = factory.CreateClient();
        var email = $"visitor.{Guid.NewGuid():N}@company.test";

        var response = await client.PostAsJsonAsync("/api/auth/request-access", new { email, requestType });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("No email is sent", body?["message"]?.GetValue<string>());
    }

    private WebApplicationFactory<Program> CreateDemoFactory() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Demo");
            builder.UseSetting("Auth:TokenSecret", TestApiFactory.TestTokenSecret);
        });

    [Fact]
    public async Task RequestAccess_DuplicateSubmissionUsesGenericResponse()
    {
        using var client = _factory.CreateClient();
        var email = $"duplicate.{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync("/api/auth/request-access", new { email, requestType = "human" });
        var duplicate = await client.PostAsJsonAsync("/api/auth/request-access", new { email, requestType = "human" });
        var firstBody = await first.Content.ReadFromJsonAsync<JsonObject>();
        var duplicateBody = await duplicate.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        Assert.Equal(firstBody?["message"]?.GetValue<string>(), duplicateBody?["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task RequestAccess_IdentityLimitReturns429WithRetryAfter()
    {
        using var client = _factory.CreateClient();
        var email = $"limited.{Guid.NewGuid():N}@example.com";
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt < 9; attempt++)
        {
            response = await client.PostAsJsonAsync("/api/auth/request-access", new { email, requestType = "human" });
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("rate_limited", body?["errorCode"]?.GetValue<string>());
    }

    [Fact]
    public async Task AgentLogin_WithInvalidOathToken_Returns401Unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/agent/login", new { username = "usr_missing_agent", oathToken = "invalid-oath-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueAgentOathToken_WithConfiguredLifespan_AllowsUsernameAndTokenLogin()
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var requestResponse = await adminClient.PostAsJsonAsync("/api/auth/requests", new
        {
            email = $"agent.oath.{Guid.NewGuid():N}@example.com",
            requestType = "ai_agent"
        });
        var requestBody = await requestResponse.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        var beforeIssue = DateTimeOffset.UtcNow;
        var apiKeyResponse = await adminClient.PostAsJsonAsync($"/api/auth/requests/{requestId}/issue-api-key", new { activeDays = 30 });
        var afterIssue = DateTimeOffset.UtcNow;
        var apiKeyBody = await apiKeyResponse.Content.ReadFromJsonAsync<JsonObject>();
        var apiKey = apiKeyBody?["apiKey"]?.GetValue<string>();
        var username = apiKeyBody?["username"]?.GetValue<string>();
        var oathTokenExpiresAt = apiKeyBody?["expiresAt"]?.GetValue<DateTimeOffset>();

        Assert.Equal(HttpStatusCode.OK, apiKeyResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.NotNull(oathTokenExpiresAt);
        Assert.True(oathTokenExpiresAt >= beforeIssue.AddDays(30).AddSeconds(-1));
        Assert.True(oathTokenExpiresAt <= afterIssue.AddDays(30).AddSeconds(1));

        var managedUsername = $"managed_agent_{Guid.NewGuid().ToString("N")[..8]}";
        var renameResponse = await adminClient.PatchAsJsonAsync(
            $"/api/auth/users/{username}/username",
            new { username = managedUsername });
        var renamedAgent = await renameResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);
        Assert.Equal(managedUsername, renamedAgent?["username"]?.GetValue<string>());

        using var agentLoginClient = _factory.CreateClient();
        var oldUsernameResponse = await agentLoginClient.PostAsJsonAsync("/api/auth/agent/login", new
        {
            username,
            oathToken = apiKey
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldUsernameResponse.StatusCode);

        var agentLoginResponse = await agentLoginClient.PostAsJsonAsync("/api/auth/agent/login", new
        {
            username = managedUsername,
            oathToken = apiKey
        });
        var agentLoginBody = await agentLoginResponse.Content.ReadFromJsonAsync<JsonObject>();
        var agentToken = agentLoginBody?["accessToken"]?.GetValue<string>();
        var agentTokenExpiresAt = agentLoginBody?["expiresAt"]?.GetValue<DateTimeOffset>();

        Assert.Equal(HttpStatusCode.OK, agentLoginResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(agentToken));
        Assert.NotNull(agentTokenExpiresAt);
        Assert.Equal(oathTokenExpiresAt!.Value.ToUnixTimeSeconds(), agentTokenExpiresAt!.Value.ToUnixTimeSeconds());

        using var agentClient = _factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);
        var pingResponse = await agentClient.GetAsync("/api/auth/me");
        var pingBody = await pingResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, pingResponse.StatusCode);
        Assert.Equal("agent", pingBody?["userType"]?.GetValue<string>());
    }

    [Fact]
    public async Task IssueAgentOathToken_ForExistingAgentUser_ReissuesFreshToken()
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var requestResponse = await adminClient.PostAsJsonAsync("/api/auth/requests", new
        {
            email = $"agent.reissue.{Guid.NewGuid():N}@example.com",
            requestType = "ai_agent"
        });
        var requestBody = await requestResponse.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        var issueResponse = await adminClient.PostAsJsonAsync($"/api/auth/requests/{requestId}/issue-api-key", new { activeDays = 7 });
        var issueBody = await issueResponse.Content.ReadFromJsonAsync<JsonObject>();
        var userId = issueBody?["username"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(userId));

        var reissueResponse = await adminClient.PostAsJsonAsync($"/api/auth/users/{userId}/issue-api-key", new { activeDays = 14 });
        var reissueBody = await reissueResponse.Content.ReadFromJsonAsync<JsonObject>();
        var newOathToken = reissueBody?["apiKey"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, reissueResponse.StatusCode);
        Assert.Equal(userId, reissueBody?["username"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(newOathToken));

        using var agentLoginClient = _factory.CreateClient();
        var loginResponse = await agentLoginClient.PostAsJsonAsync("/api/auth/agent/login", new { username = userId, oathToken = newOathToken });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(loginBody?["accessToken"]?.GetValue<string>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    public async Task IssueAgentOathToken_WithActiveDaysOutsideBounds_Returns400BadRequest(int activeDays)
    {
        using var adminClient = _factory.CreateClient();
        var adminToken = await _factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var requestResponse = await adminClient.PostAsJsonAsync("/api/auth/requests", new
        {
            email = $"agent.oath.invalid.{Guid.NewGuid():N}@example.com",
            requestType = "ai_agent"
        });
        var requestBody = await requestResponse.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        var apiKeyResponse = await adminClient.PostAsJsonAsync($"/api/auth/requests/{requestId}/issue-api-key", new { activeDays });
        var apiKeyBody = await apiKeyResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.BadRequest, apiKeyResponse.StatusCode);
        Assert.Equal("activeDays must be between 1 and 62 days", apiKeyBody?["error"]?.GetValue<string>());
    }

}
