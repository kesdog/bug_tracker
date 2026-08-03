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
    public async Task ProjectAuthorization_DevAndAgentOnlyDiscoverAllocatedProjects()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var normalProjectId = await CreateProjectAsync(adminClient, "normal");
        var sensitiveProjectId = await CreateProjectAsync(adminClient, "sensitive");

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        using var agentClient = await CreateAuthorizedClientAsync(TestApiFactory.AgentUserId);
        var devProjects = await (await devClient.GetAsync("/api/projects")).Content.ReadFromJsonAsync<JsonArray>();
        var agentProjects = await (await agentClient.GetAsync("/api/projects")).Content.ReadFromJsonAsync<JsonArray>();

        Assert.DoesNotContain(devProjects!, item => item?["projectId"]?.GetValue<string>() is var id && (id == normalProjectId || id == sensitiveProjectId));
        Assert.DoesNotContain(agentProjects!, item => item?["projectId"]?.GetValue<string>() is var id && (id == normalProjectId || id == sensitiveProjectId));
    }

    [Fact]
    public async Task ProjectAuthorization_HumanAndAgentDevCannotCreateInUnallocatedProject()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(adminClient, "normal");
        var request = new
        {
            issueTitle = "Out of scope creation",
            description = "Must be rejected for unallocated dev callers.",
            bugType = "api",
            projectId,
            severity = "mid"
        };

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        using var agentClient = await CreateAuthorizedClientAsync(TestApiFactory.AgentUserId);
        var devResponse = await devClient.PostAsJsonAsync("/api/bugs", request);
        var agentResponse = await agentClient.PostAsJsonAsync("/api/bugs", request);

        Assert.Equal(HttpStatusCode.Forbidden, devResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, agentResponse.StatusCode);
    }

    [Fact]
    public async Task ProjectAuthorization_SeniorSeesAllNormalButOnlyAllocatedSensitiveProjects()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        await adminClient.PatchAsJsonAsync($"/api/auth/users/{TestApiFactory.SeniorUserId}/role", new { role = "senior" });
        var normalProjectId = await CreateProjectAsync(adminClient, "normal");
        var sensitiveProjectId = await CreateProjectAsync(adminClient, "sensitive");

        using var seniorClient = await CreateAuthorizedClientAsync(TestApiFactory.SeniorUserId);
        var beforeAllocation = await (await seniorClient.GetAsync("/api/projects")).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(beforeAllocation!, item => item?["projectId"]?.GetValue<string>() == normalProjectId);
        Assert.DoesNotContain(beforeAllocation!, item => item?["projectId"]?.GetValue<string>() == sensitiveProjectId);

        var allocationResponse = await adminClient.PatchAsJsonAsync($"/api/projects/{sensitiveProjectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.SeniorUserId }
        });
        var afterAllocation = await (await seniorClient.GetAsync("/api/projects")).Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, allocationResponse.StatusCode);
        Assert.Contains(afterAllocation!, item => item?["projectId"]?.GetValue<string>() == sensitiveProjectId);
    }

    [Fact]
    public async Task ProjectAuthorization_NormalTicketParticipationDoesNotGrantProjectWideAccess()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(adminClient, "normal");
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "normal-participant-ticket-001",
            IssueTitle: "Participant ticket",
            Description: "Exact ticket remains visible.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-06-01 10:00:00",
            UpdatedAt: "2026-06-01 10:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            ReporterUserId: TestApiFactory.AdminUserId,
            ProjectId: projectId));
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "normal-unrelated-ticket-001",
            IssueTitle: "Unrelated ticket",
            Description: "Must remain hidden.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-06-01 11:00:00",
            UpdatedAt: "2026-06-01 11:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ReporterUserId: TestApiFactory.AdminUserId,
            ProjectId: projectId));

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        var projects = await (await devClient.GetAsync("/api/projects")).Content.ReadFromJsonAsync<JsonArray>();
        var list = await (await devClient.GetAsync("/api/bugs?status=active&limit=100")).Content.ReadFromJsonAsync<JsonArray>();
        var participantDetail = await devClient.GetAsync("/api/bugs/normal-participant-ticket-001");
        var unrelatedDetail = await devClient.GetAsync("/api/bugs/normal-unrelated-ticket-001");

        Assert.DoesNotContain(projects!, item => item?["projectId"]?.GetValue<string>() == projectId);
        Assert.Contains(list!, item => item?["id"]?.GetValue<string>() == "normal-participant-ticket-001");
        Assert.DoesNotContain(list!, item => item?["id"]?.GetValue<string>() == "normal-unrelated-ticket-001");
        Assert.Equal(HttpStatusCode.OK, participantDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedDetail.StatusCode);
    }

    [Fact]
    public async Task ProjectAuthorization_SensitiveMembershipRemovalRevokesParticipantAccess()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(adminClient, "sensitive");
        var allocationResponse = await adminClient.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.DefaultUserId }
        });
        Assert.Equal(HttpStatusCode.OK, allocationResponse.StatusCode);

        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "sensitive-revocation-ticket-001",
            IssueTitle: "Sensitive participant ticket",
            Description: "Membership controls all access.",
            BugType: "database",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-06-02 10:00:00",
            UpdatedAt: "2026-06-02 10:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            ReporterUserId: TestApiFactory.DefaultUserId,
            ProjectId: projectId));

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        var beforeRemoval = await devClient.GetAsync("/api/bugs/sensitive-revocation-ticket-001");
        var commentBeforeRemoval = await adminClient.PostAsJsonAsync("/api/bugs/sensitive-revocation-ticket-001/comments", new
        {
            body = "Visible while the assignee is still a member."
        });
        var notificationsBeforeRemoval = await (await devClient.GetAsync("/api/notifications?unreadOnly=true")).Content.ReadFromJsonAsync<JsonArray>();
        var removeResponse = await adminClient.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new { userIds = new[] { TestApiFactory.AdminUserId } });
        var afterRemoval = await devClient.GetAsync("/api/bugs/sensitive-revocation-ticket-001");
        var allocatedAfterRemoval = await (await devClient.GetAsync("/api/bugs/allocated?limit=100")).Content.ReadFromJsonAsync<JsonArray>();
        var commentAfterRemoval = await adminClient.PostAsJsonAsync("/api/bugs/sensitive-revocation-ticket-001/comments", new
        {
            body = "Must not notify the removed assignee."
        });
        var notificationsAfterRemoval = await (await devClient.GetAsync("/api/notifications?unreadOnly=true")).Content.ReadFromJsonAsync<JsonArray>();

        Assert.Equal(HttpStatusCode.OK, beforeRemoval.StatusCode);
        Assert.Equal(HttpStatusCode.Created, commentBeforeRemoval.StatusCode);
        Assert.Contains(notificationsBeforeRemoval!, item => item?["ticketId"]?.GetValue<string>() == "sensitive-revocation-ticket-001");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, afterRemoval.StatusCode);
        Assert.DoesNotContain(allocatedAfterRemoval!, item => item?["id"]?.GetValue<string>() == "sensitive-revocation-ticket-001");
        Assert.Equal(HttpStatusCode.Created, commentAfterRemoval.StatusCode);
        Assert.DoesNotContain(notificationsAfterRemoval!, item => item?["ticketId"]?.GetValue<string>() == "sensitive-revocation-ticket-001");
    }

    [Fact]
    public async Task CreateBug_WithOptionalAssignee_CreatesOpenTicketAndAssignmentSideEffects()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var response = await adminClient.PostAsJsonAsync("/api/bugs", new
        {
            issueTitle = $"Assigned during creation {Guid.NewGuid():N}",
            description = "Admin assigns the ticket as part of creation.",
            bugType = "api",
            projectId = "project-general",
            severity = "high",
            assigneeUserId = TestApiFactory.DefaultUserId
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("open", body?["status"]?.GetValue<string>());
        Assert.Equal(TestApiFactory.DefaultUserId, body?["assigneeUserId"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(body?["assignedAt"]?.GetValue<string>()));
        var activityKinds = body?["activity"]?.AsArray().Select(item => item?["kind"]?.GetValue<string>()).ToList();
        Assert.Contains("created", activityKinds!);
        Assert.Contains("assigned", activityKinds!);

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        var notifications = await (await devClient.GetAsync("/api/notifications?unreadOnly=true")).Content.ReadFromJsonAsync<JsonArray>();
        Assert.Contains(notifications!, item => item?["ticketId"]?.GetValue<string>() == body?["id"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateBug_WithAssignee_AsHumanOrAgentDev_Returns403()
    {
        var request = new
        {
            issueTitle = "Unauthorized create assignment",
            description = "Dev callers cannot assign while creating.",
            bugType = "api",
            projectId = "project-general",
            severity = "mid",
            assigneeUserId = TestApiFactory.AdminUserId
        };

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        using var agentClient = await CreateAuthorizedClientAsync(TestApiFactory.AgentUserId);
        var devResponse = await devClient.PostAsJsonAsync("/api/bugs", request);
        var agentResponse = await agentClient.PostAsJsonAsync("/api/bugs", request);

        Assert.Equal(HttpStatusCode.Forbidden, devResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, agentResponse.StatusCode);
    }

    [Fact]
    public async Task Assignment_PromotedAiAgentStillCannotAssign()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "promoted-agent-assignment-ticket-001",
            IssueTitle: "Promoted agent assignment",
            Description: "Assignment authority remains human-only.",
            BugType: "api",
            Status: "todo",
            Severity: "mid",
            CreatedAt: "2026-06-04 10:00:00",
            UpdatedAt: "2026-06-04 10:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ProjectId: "project-general"));

        try
        {
            var promoteResponse = await adminClient.PatchAsJsonAsync($"/api/auth/users/{TestApiFactory.AgentUserId}/role", new { role = "senior" });
            Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);

            using var agentClient = await CreateAuthorizedClientAsync(TestApiFactory.AgentUserId);
            var allocateResponse = await agentClient.PatchAsJsonAsync("/api/bugs/promoted-agent-assignment-ticket-001/allocate", new
            {
                assigneeUserId = TestApiFactory.DefaultUserId,
                expectedVersion = 1
            });
            var createResponse = await agentClient.PostAsJsonAsync("/api/bugs", new
            {
                issueTitle = "Promoted agent create assignment",
                description = "Promoted agents cannot assign during creation.",
                bugType = "api",
                projectId = "project-general",
                severity = "mid",
                assigneeUserId = TestApiFactory.DefaultUserId
            });

            Assert.Equal(HttpStatusCode.Forbidden, allocateResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        }
        finally
        {
            await adminClient.PatchAsJsonAsync($"/api/auth/users/{TestApiFactory.AgentUserId}/role", new { role = "dev" });
        }
    }

    [Fact]
    public async Task AssignSensitiveTicket_RequiresTargetMembershipAndReturnsHint()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var projectId = await CreateProjectAsync(adminClient, "sensitive");
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "sensitive-assignment-ticket-001",
            IssueTitle: "Sensitive assignment",
            Description: "Target membership is required.",
            BugType: "api",
            Status: "todo",
            Severity: "high",
            CreatedAt: "2026-06-03 10:00:00",
            UpdatedAt: "2026-06-03 10:00:00",
            CloseDate: null,
            AssigneeUserId: null,
            ReporterUserId: TestApiFactory.AdminUserId,
            ProjectId: projectId));

        var rejected = await adminClient.PatchAsJsonAsync("/api/bugs/sensitive-assignment-ticket-001/allocate", new
        {
            assigneeUserId = TestApiFactory.DefaultUserId,
            expectedVersion = 1
        });
        var rejectedBody = await rejected.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("assignee_not_project_member", rejectedBody?["errorCode"]?.GetValue<string>());

        await adminClient.PatchAsJsonAsync($"/api/projects/{projectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.DefaultUserId }
        });
        var accepted = await adminClient.PatchAsJsonAsync("/api/bugs/sensitive-assignment-ticket-001/allocate", new
        {
            assigneeUserId = TestApiFactory.DefaultUserId,
            expectedVersion = 1
        });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task SensitiveVisibility_OnlyAdminCanDeclassifyTicketAndAssigneesMustBeMembersBeforeClassification()
    {
        using var adminClient = await CreateAuthorizedClientAsync(TestApiFactory.AdminUserId);
        var sensitiveProjectId = await CreateProjectAsync(adminClient, "sensitive");
        await adminClient.PatchAsJsonAsync($"/api/projects/{sensitiveProjectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.DefaultUserId }
        });
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "sensitive-declassification-ticket-001",
            IssueTitle: "Sensitive declassification",
            Description: "Only an admin may cross visibility boundaries.",
            BugType: "api",
            Status: "open",
            Severity: "high",
            CreatedAt: "2026-06-05 10:00:00",
            UpdatedAt: "2026-06-05 10:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            ReporterUserId: TestApiFactory.DefaultUserId,
            ProjectId: sensitiveProjectId));

        using var devClient = await CreateAuthorizedClientAsync(TestApiFactory.DefaultUserId);
        var devMove = await devClient.PatchAsJsonAsync("/api/bugs/sensitive-declassification-ticket-001/metadata", new
        {
            projectId = "project-general",
            expectedVersion = 1
        });
        var adminMove = await adminClient.PatchAsJsonAsync("/api/bugs/sensitive-declassification-ticket-001/metadata", new
        {
            projectId = "project-general",
            expectedVersion = 1
        });

        Assert.Equal(HttpStatusCode.Forbidden, devMove.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminMove.StatusCode);

        var normalProjectId = await CreateProjectAsync(adminClient, "normal");
        await _factory.SeedBugAsync(new SeedBugRequest(
            Id: "normal-classification-ticket-001",
            IssueTitle: "Normal classification",
            Description: "Assignee membership must be established first.",
            BugType: "api",
            Status: "open",
            Severity: "mid",
            CreatedAt: "2026-06-05 11:00:00",
            UpdatedAt: "2026-06-05 11:00:00",
            CloseDate: null,
            AssigneeUserId: TestApiFactory.DefaultUserId,
            ReporterUserId: TestApiFactory.AdminUserId,
            ProjectId: normalProjectId));

        var rejectedClassification = await adminClient.PatchAsJsonAsync($"/api/projects/{normalProjectId}/visibility", new { visibility = "sensitive" });
        var rejectedBody = await rejectedClassification.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.BadRequest, rejectedClassification.StatusCode);
        Assert.Equal("sensitive_project_has_nonmember_assignees", rejectedBody?["errorCode"]?.GetValue<string>());

        await adminClient.PatchAsJsonAsync($"/api/projects/{normalProjectId}/allocations", new
        {
            userIds = new[] { TestApiFactory.AdminUserId, TestApiFactory.DefaultUserId }
        });
        var acceptedClassification = await adminClient.PatchAsJsonAsync($"/api/projects/{normalProjectId}/visibility", new { visibility = "sensitive" });
        Assert.Equal(HttpStatusCode.OK, acceptedClassification.StatusCode);
    }

}
