using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using BugTracker.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed class DeliveryHostingIntegrationTests
{
    [Fact]
    public async Task Hosting_RecordsSystemLifecycleStartEvent()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var count = await factory.ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_logs WHERE actor_user_id = 'system' AND action = 'system.started';",
            string.Empty);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task HealthEndpoints_AreAnonymousAndReadinessChecksDatabaseVersion()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var liveResponse = await client.GetAsync("/health/live");
        var readyResponse = await client.GetAsync("/health/ready");
        var readyBody = await readyResponse.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Equal("ready", readyBody?["status"]?.GetValue<string>());
        Assert.Equal("available", readyBody?["database"]?.GetValue<string>());
        Assert.Equal("current", readyBody?["migrations"]?.GetValue<string>());
        Assert.Equal("available", readyBody?["storage"]?.GetValue<string>());
        Assert.Equal("writable", readyBody?["databaseDirectory"]?.GetValue<string>());
        Assert.Equal("sufficient", readyBody?["freeSpace"]?.GetValue<string>());
        Assert.Equal(ReadinessOptions.DefaultMinimumFreeBytes, readyBody?["minimumFreeBytes"]?.GetValue<long>());
        Assert.DoesNotContain("path", readyBody!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        var expectedVersion = readyBody?["expectedMigrationVersion"]?.GetValue<int>() ?? 0;
        Assert.True(expectedVersion > 0);
        Assert.Equal(expectedVersion, readyBody?["appliedMigrationVersion"]?.GetValue<int>());

        await factory.ExecuteSqlAsync(
            "DELETE FROM schema_migrations WHERE version = $version;",
            ("$version", expectedVersion));

        var staleResponse = await client.GetAsync("/health/ready");
        var staleBody = await staleResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, staleResponse.StatusCode);
        Assert.Equal("out_of_date", staleBody?["migrations"]?.GetValue<string>());
        Assert.True(staleBody?["appliedMigrationVersion"]?.GetValue<int>() < expectedVersion);
    }

    [Fact]
    public async Task Readiness_UsesConfiguredMinimumFreeSpace_AndRemovesWriteProbe()
    {
        using var factory = TestApiFactory.WithReadinessMinimum(long.MaxValue);
        using var client = factory.CreateClient();
        var databaseFactory = factory.Services.GetRequiredService<BugTracker.Api.Database.SqliteConnectionFactory>();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("insufficient_space", body?["storage"]?.GetValue<string>());
        Assert.Equal("writable", body?["databaseDirectory"]?.GetValue<string>());
        Assert.Equal("insufficient", body?["freeSpace"]?.GetValue<string>());
        Assert.Equal(long.MaxValue, body?["minimumFreeBytes"]?.GetValue<long>());
        Assert.Empty(Directory.EnumerateFiles(databaseFactory.DatabaseDirectoryPath, ".bug-tracker-readiness-*.tmp"));
    }

    [Theory]
    [InlineData("Demo", "replace-this-with-a-long-random-secret-for-non-dev-env")]
    [InlineData("Demo", "REPLACE_WITH_A_LONG_RANDOM_SECRET")]
    [InlineData("Production", "too-short")]
    public void ProtectedEnvironment_RejectsPlaceholderOrWeakTokenSecret(string environment, string secret)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BugTracker.Api.Auth.AuthConfigurationValidator.ValidateTokenSecret(secret, environment));

        Assert.Contains("strong, deployment-specific secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void NonProtectedEnvironment_PreservesExistingTokenSecretBehavior(string environment)
    {
        BugTracker.Api.Auth.AuthConfigurationValidator.ValidateTokenSecret("weak", environment);
    }

    [Fact]
    public async Task Readiness_IsUnavailableWhileResetMaintenanceLeaseIsHeld()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var maintenance = factory.Services.GetRequiredService<IResetMaintenanceState>();

        using (maintenance.BeginReset())
        {
            var response = await client.GetAsync("/health/ready");
            var body = await response.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("reset_in_progress", body?["maintenance"]?.GetValue<string>());
            Assert.Equal("not_checked", body?["database"]?.GetValue<string>());
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task ResetMaintenance_RejectsNewApiRequests_ButLeavesHealthAvailable()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var maintenance = factory.Services.GetRequiredService<IResetMaintenanceState>();

        using (maintenance.BeginReset())
        {
            var apiResponse = await client.GetAsync("/api/not-a-real-endpoint");
            var body = await apiResponse.Content.ReadFromJsonAsync<JsonObject>();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, apiResponse.StatusCode);
            Assert.Equal("demo_reset_in_progress", body?["errorCode"]?.GetValue<string>());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
    }

    [Fact]
    public async Task MissingWebRootIndex_DoesNotBreakFactoryAndNeverHandlesUnknownApiAsSpa()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/client/route")).StatusCode);

        var token = await factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var apiResponse = await client.GetAsync("/api/not-a-real-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, apiResponse.StatusCode);
        Assert.NotEqual("text/html", apiResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WebRootFilesAndSpaRoutes_AreServedWithoutInterceptingMissingAssets()
    {
        using var factory = new SpaHostingFactory();
        using var client = factory.CreateClient();

        var routeResponse = await client.GetAsync("/tickets/TICKET-42");
        Assert.Equal(HttpStatusCode.OK, routeResponse.StatusCode);
        Assert.Equal("text/html", routeResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("delivery-spa", await routeResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("no-cache", routeResponse.Headers.CacheControl?.ToString());

        var assetResponse = await client.GetAsync("/assets/app-test.js");
        Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
        Assert.Contains("immutable", assetResponse.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        var discoveryResponse = await client.GetAsync("/llms.txt");
        Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
        Assert.Equal("text/plain", discoveryResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("HTTP API instead of the browser GUI", await discoveryResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var missingAssetResponse = await client.GetAsync("/assets/missing.js");
        Assert.Equal(HttpStatusCode.NotFound, missingAssetResponse.StatusCode);
        Assert.NotEqual("text/html", missingAssetResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Responses_IncludeBrowserSecurityHeaders()
    {
        using var factory = new SpaHostingFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/", "/assets/app-test.js", "/health/live", "/api/not-a-real-endpoint" })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var cspValues));
            var csp = Assert.Single(cspValues);
            Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
            Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
            Assert.Contains("base-uri 'self'", csp, StringComparison.Ordinal);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
            Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            Assert.NotEmpty(response.Headers.GetValues("Permissions-Policy"));
        }
    }

    [Fact]
    public async Task DemoEnvironment_ExposesPublicAccountsAndNonDemoDoesNot()
    {
        using var demoFactory = TestApiFactory.CreateDemo();
        using var demoClient = demoFactory.CreateClient();
        var demoResponse = await demoClient.GetAsync("/api/demo/config");
        var demoText = await demoResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, demoResponse.StatusCode);
        Assert.Contains("ava.dev@example.com", demoText, StringComparison.Ordinal);
        Assert.Contains("04:00", demoText, StringComparison.Ordinal);

        using var normalFactory = new TestApiFactory();
        using var normalClient = normalFactory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await normalClient.GetAsync("/api/demo/config")).StatusCode);

        using var privateDemoFactory = TestApiFactory.CreatePrivateDemo();
        using var privateDemoClient = privateDemoFactory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await privateDemoClient.GetAsync("/api/demo/config")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedReads_DoNotCreateTokenUsedEvents_AndPresenceIsDebounced()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var token = await factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var connectionFactory = factory.Services.GetRequiredService<BugTracker.Api.Database.SqliteConnectionFactory>();

        static async Task<string?> ReadLastSeenAsync(BugTracker.Api.Database.SqliteConnectionFactory connections)
        {
            await using var connection = await connections.OpenConnectionAsync(readOnly: true);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT last_seen_at FROM users WHERE user_id = $user_id;";
            command.Parameters.AddWithValue("$user_id", TestApiFactory.DefaultUserId);
            return Convert.ToString(await command.ExecuteScalarAsync());
        }

        var before = await ReadLastSeenAsync(connectionFactory);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var after = await ReadLastSeenAsync(connectionFactory);

        await using var connection = await connectionFactory.OpenConnectionAsync(readOnly: true);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE action = 'token_used';";
        Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync()));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task DemoEnvironment_EnforcesTicketQuotaWithStructured413()
    {
        using var factory = TestApiFactory.CreateDemo(maxTickets: 1);
        using var client = factory.CreateClient();
        var token = await factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = new
        {
            issueTitle = "Bounded demo ticket",
            description = "The second write must be rejected.",
            bugType = "api",
            projectId = "project-general",
            severity = "mid",
            priority = "p2",
            tags = new[] { "back-end" }
        };

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/bugs", request)).StatusCode);
        var rejected = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await rejected.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        Assert.Equal("quota_exceeded", body?["errorCode"]?.GetValue<string>());
        Assert.Equal("no-store", rejected.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AuthenticatedAbuseLimits_ApplyOutsideDemo()
    {
        using var factory = TestApiFactory.WithMaxTickets(1);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateAccessTokenAsync());
        var request = new
        {
            issueTitle = "Bounded ticket",
            description = "The second write must be rejected outside demo too.",
            bugType = "api",
            projectId = "project-general",
            severity = "mid",
            priority = "p2",
            tags = new[] { "back-end" }
        };

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/bugs", request)).StatusCode);
        var rejected = await client.PostAsJsonAsync("/api/bugs", request);
        var body = await rejected.Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        Assert.Equal("quota_exceeded", body?["errorCode"]?.GetValue<string>());
    }

    [Fact]
    public async Task DemoEnvironment_SharedAccountDoesNotConsumeIdentityLockoutBucket()
    {
        using var factory = TestApiFactory.CreateDemo();
        await factory.ExecuteSqlAsync(
            "UPDATE users SET email = 'ava.dev@example.com' WHERE user_id = $user_id;",
            ("$user_id", TestApiFactory.DefaultUserId));
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new { email = "ava.dev@example.com", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var success = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "ava.dev@example.com",
            password = TestApiFactory.DefaultUserPassword
        });
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    [Fact]
    public async Task HumanLogin_LocksOnlyAfterTwentyFailuresForTheAccountAndIp()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 19; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = TestApiFactory.DefaultUserEmail,
                password = "wrong-password"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var locked = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.DefaultUserEmail,
            password = "wrong-password"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        var lockedBody = await locked.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("login_locked", lockedBody?["errorCode"]?.GetValue<string>());
        Assert.True(locked.Headers.Contains("Retry-After"));

        var otherAccount = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.SeniorUserEmail,
            password = TestApiFactory.DefaultUserPassword
        });
        Assert.Equal(HttpStatusCode.OK, otherAccount.StatusCode);
    }

    [Fact]
    public async Task AgentLogin_HasAnIndependentTwentyFailureLockout()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 19; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/agent/login", new
            {
                username = "agent_test",
                oathToken = "wrong-oath-token"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var locked = await client.PostAsJsonAsync("/api/auth/agent/login", new
        {
            username = "agent_test",
            oathToken = "wrong-oath-token"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        var body = await locked.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("login_locked", body?["errorCode"]?.GetValue<string>());
    }

    [Fact]
    public async Task DemoEnvironment_AuthenticatedLimiterReturns429AndRetryAfter()
    {
        using var factory = TestApiFactory.CreateDemo();
        using var client = factory.CreateClient();
        var token = await factory.CreateAccessTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (var request = 0; request < 180; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        }

        var rejected = await client.GetAsync("/api/auth/me");
        var body = await rejected.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("rate_limited", body?["errorCode"]?.GetValue<string>());
        Assert.True(rejected.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task DemoEnvironment_AgentOathTokensAlwaysExpireWithinOneDay()
    {
        using var factory = TestApiFactory.CreateDemo();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateAccessTokenAsync(TestApiFactory.AdminUserId));
        var request = await client.PostAsJsonAsync("/api/auth/requests", new
        {
            email = $"agent.ttl.{Guid.NewGuid():N}@example.com",
            requestType = "ai_agent"
        });
        var requestBody = await request.Content.ReadFromJsonAsync<JsonObject>();
        var requestId = requestBody?["requestId"]?.GetValue<string>();
        var beforeIssue = DateTimeOffset.UtcNow;

        var issued = await client.PostAsJsonAsync($"/api/auth/requests/{requestId}/issue-api-key", new { activeDays = 30 });
        var issuedBody = await issued.Content.ReadFromJsonAsync<JsonObject>();
        var expiresAt = issuedBody?["expiresAt"]?.GetValue<DateTimeOffset>();

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.NotNull(expiresAt);
        Assert.InRange(expiresAt!.Value, beforeIssue.AddDays(1).AddSeconds(-1), beforeIssue.AddDays(1).AddSeconds(2));
    }

    private sealed class SpaHostingFactory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"bug-tracker-delivery-{Guid.NewGuid():N}");
        private string DatabasePath => Path.Combine(_root, "delivery.db");
        private string WebRootPath => Path.Combine(_root, "wwwroot");

        public SpaHostingFactory()
        {
            Directory.CreateDirectory(Path.Combine(WebRootPath, "assets"));
            File.WriteAllText(Path.Combine(WebRootPath, "index.html"), "<!doctype html><title>delivery-spa</title>");
            File.WriteAllText(Path.Combine(WebRootPath, "assets", "app-test.js"), "window.deliverySpa = true;");
            File.WriteAllText(Path.Combine(WebRootPath, "llms.txt"), "Use the HTTP API instead of the browser GUI.");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseWebRoot(WebRootPath);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:Path"] = DatabasePath,
                    ["Auth:TokenSecret"] = TestApiFactory.TestTokenSecret,
                    ["Audit:LogDirectory"] = Path.Combine(_root, "logs")
                }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
