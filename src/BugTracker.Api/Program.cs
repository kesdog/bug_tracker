using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Bugs;
using BugTracker.Api.Docs;
using BugTracker.Api.Database;
using BugTracker.Api.Health;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = BugReportLimits.MaxApiRequestBodyBytes);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = BugReportLimits.MaxMultipartRequestBodyBytes;
    options.MemoryBufferThreshold = 64 * 1024;
});

var tokenSecret = builder.Configuration["Auth:TokenSecret"];
if (string.IsNullOrWhiteSpace(tokenSecret))
{
    throw new InvalidOperationException("Missing Auth:TokenSecret in configuration.");
}
AuthConfigurationValidator.ValidateTokenSecret(tokenSecret, builder.Environment.EnvironmentName);

var frontendOrigin = builder.Configuration["Frontend:Origin"] ?? "http://127.0.0.1:5173";
var publicRateLimits = builder.Configuration
    .GetSection("RateLimits:PublicEndpoints")
    .Get<PublicEndpointRateLimitOptions>() ?? new PublicEndpointRateLimitOptions();
var allowLocalDevOrigins = builder.Environment.IsDevelopment();
var startupUrls = ResolveStartupUrls(builder.Configuration["urls"], builder.Environment.IsDevelopment());
if (startupUrls.Length > 0)
{
    builder.WebHost.UseUrls(startupUrls);
}

var useForwardedHeaders = ConfigureForwardedHeaders(builder.Services, builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .SetIsOriginAllowed(origin => IsAllowedFrontendOrigin(origin, frontendOrigin, allowLocalDevOrigins))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("BugTracker.Api.PublicEndpointRateLimiting");
        logger.LogWarning(
            "Public endpoint IP rate limit rejected {Method} {Path} from {RemoteIpAddress}.",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress);
        RateLimitResponses.SetRetryAfter(context.HttpContext.Response, context.Lease);
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "too many requests", errorCode = "rate_limited" },
            ct);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        IsLoginEndpoint(context.Request.Path)
            ? RateLimitPartition.GetFixedWindowLimiter($"login-ip:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(5)
            })
            : RateLimitPartition.GetNoLimiter("other"));

    AddIpPolicy(options, PublicRateLimitPolicies.HumanLogin, publicRateLimits.HumanLogin);
    AddIpPolicy(options, PublicRateLimitPolicies.AgentLogin, publicRateLimits.AgentLogin);
    AddIpPolicy(options, PublicRateLimitPolicies.PasswordSetup, publicRateLimits.PasswordSetup);
    AddIpPolicy(options, PublicRateLimitPolicies.AccessRequest, publicRateLimits.AccessRequest);
});

builder.Services.AddSingleton(new PasswordHasherService());
builder.Services.AddSingleton(new TokenService(tokenSecret));
builder.Services.AddSingleton(publicRateLimits);
builder.Services.AddSingleton<PublicEndpointIdentityRateLimiter>();
builder.Services.AddSingleton<LoginSecurityMonitor>();
builder.Services.AddOptions<AuthenticatedAbuseOptions>()
    .Bind(builder.Configuration.GetSection("AuthenticatedAbuse"))
    .Validate(options => options.StorageSafetyReserveBytes >= 0 && options.MaxEvidenceBytes > 0,
        "AuthenticatedAbuse storage limits must be positive.")
    .Validate(options => options.MaxProjects > 0 && options.MaxTickets > 0 && options.MaxComments > 0 && options.MaxAttachments > 0,
        "AuthenticatedAbuse resource quotas must be positive.")
    .Validate(options => options.GeneralPermitLimit > 0 && options.GeneralWindowMinutes > 0
        && options.CreatePermitLimit > 0 && options.CreateWindowMinutes > 0
        && options.WritePermitLimit > 0 && options.WriteWindowMinutes > 0
        && options.UploadPermitLimit > 0 && options.UploadWindowMinutes > 0
        && options.ExportPermitLimit > 0 && options.ExportWindowMinutes > 0
        && options.WebSocketPermitLimit > 0 && options.WebSocketWindowMinutes > 0,
        "AuthenticatedAbuse rate limits must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton<AuthenticatedAbuseProtection>();
builder.Services.AddOptions<TicketDataStorageOptions>()
    .Bind(builder.Configuration.GetSection("TicketDataStorage"))
    .Validate(options => options.MaxBytes > 0, "TicketDataStorage:MaxBytes must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton<TicketDataStorageService>();
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var environment = sp.GetRequiredService<IHostEnvironment>();
    var dbPath = ResolveDatabasePath(configuration["Database:Path"], environment.ContentRootPath);
    return new SqliteConnectionFactory(dbPath);
});
builder.Services.AddSingleton<AuthRepository>();
builder.Services.AddSingleton<FirstRunSetupService>();
builder.Services.AddSingleton<TicketWriteAuthorizationService>();
builder.Services.AddSingleton<ImageValidationService>();
builder.Services.AddSingleton<BugRepository>();
builder.Services.AddSingleton<ProjectRepository>();
builder.Services.AddSingleton<ProjectAuthorizationService>();
builder.Services.AddSingleton<AuditRepository>();
builder.Services.AddSingleton<NotificationRepository>();
builder.Services.AddSingleton<NotificationAuthorizationService>();
builder.Services.AddSingleton<ResetMaintenanceState>();
builder.Services.AddSingleton<IResetMaintenanceState>(sp => sp.GetRequiredService<ResetMaintenanceState>());
builder.Services.AddTransient<ResetMaintenanceMiddleware>();
builder.Services.AddOptions<ReadinessOptions>()
    .Bind(builder.Configuration.GetSection("Readiness"))
    .Validate(options => options.MinimumFreeBytes >= 0, "Readiness:MinimumFreeBytes must not be negative.")
    .ValidateOnStart();
builder.Services.AddSingleton<DeliveryReadinessService>();
builder.Services.AddOptions<AgentWebSocketOptions>()
    .Bind(builder.Configuration.GetSection("AgentWebSocket"))
    .Validate(options => options.MaxConnections is >= 1 and <= 10_000,
        "AgentWebSocket:MaxConnections must be between 1 and 10000.")
    .Validate(options => options.MaxConnectionsPerUser is >= 1 and <= 100
                         && options.MaxConnectionsPerUser <= options.MaxConnections,
        "AgentWebSocket:MaxConnectionsPerUser must be between 1 and 100 and must not exceed MaxConnections.")
    .Validate(options => options.CloseTimeoutSeconds is >= 1 and <= 30,
        "AgentWebSocket:CloseTimeoutSeconds must be between 1 and 30.")
    .Validate(options => options.HeartbeatIntervalSeconds is >= 1 and <= 300,
        "AgentWebSocket:HeartbeatIntervalSeconds must be between 1 and 300.")
    .Validate(options => options.HeartbeatRetryIntervalSeconds is >= 1 and <= 60,
        "AgentWebSocket:HeartbeatRetryIntervalSeconds must be between 1 and 60.")
    .ValidateOnStart();
builder.Services.AddSingleton<AgentNotificationSocketHub>();
builder.Services.AddSingleton<IAgentNotificationPublisher>(sp => sp.GetRequiredService<AgentNotificationSocketHub>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<DemoResetOptions>()
    .Bind(builder.Configuration.GetSection("DemoReset"))
    .Validate(options => options.HourUtc is >= 0 and <= 23,
        "DemoReset:HourUtc must be between 0 and 23.")
    .Validate(options => options.DrainTimeoutSeconds is >= 1 and <= 300,
        "DemoReset:DrainTimeoutSeconds must be between 1 and 300.")
    .ValidateOnStart();
builder.Services.AddSingleton<DemoResetService>();
builder.Services.AddSingleton<DemoResetCoordinator>();
builder.Services.AddHostedService<DemoResetScheduler>();
builder.Services.AddSingleton<OutboxDispatchGate>();
builder.Services.AddSingleton(sp => new AuditFilePublisher(
    ResolveAuditLogDirectory(builder.Configuration["Audit:LogDirectory"], builder.Environment.ContentRootPath)));
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
builder.Services.AddHostedService<ApplicationShutdownService>();
builder.Services.AddSingleton(sp => new AuditLogger(
    sp.GetRequiredService<AuditRepository>(),
    ResolveAuditLogDirectory(builder.Configuration["Audit:LogDirectory"], builder.Environment.ContentRootPath)));
builder.Services.AddHostedService<SystemLifecycleAuditService>();
builder.Services.AddTransient<AuthMiddleware>();
builder.Services.AddTransient<FirstRunSetupMiddleware>();
builder.Services.AddTransient<AuthenticatedAbuseMiddleware>();

var app = builder.Build();
await new SqliteMigrationRunner(app.Services.GetRequiredService<SqliteConnectionFactory>()).MigrateAsync();
await app.Services.GetRequiredService<TicketDataStorageService>().InitializeAsync();
var databaseCommandExitCode = await DatabaseCommandRunner.RunIfRequestedAsync(
    args,
    app.Services.GetRequiredService<SqliteConnectionFactory>(),
    app.Services.GetRequiredService<PasswordHasherService>());
if (databaseCommandExitCode is not null)
{
    Environment.ExitCode = databaseCommandExitCode.Value;
    return;
}

// This catch-up runs before Kestrel starts accepting traffic. Disabled configuration returns
// without entering maintenance or touching demo state.
await app.Services.GetRequiredService<DemoResetCoordinator>().RunIfDueAsync();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BugTracker.Api.Startup");

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    context.Items["csp_nonce"] = nonce;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.ContentSecurityPolicy = string.Join(' ',
            "default-src 'self';",
            $"script-src 'self' 'nonce-{nonce}';",
            $"style-src-elem 'self' 'nonce-{nonce}';",
            "style-src-attr 'unsafe-inline';",
            "img-src 'self' data: blob:;",
            "font-src 'self';",
            "connect-src 'self';",
            "object-src 'none';",
            "base-uri 'self';",
            "form-action 'self';",
            "frame-ancestors 'none';",
            "upgrade-insecure-requests");
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
        return Task.CompletedTask;
    });
    await next(context);
});

app.UseWhen(context =>
    !context.Request.Path.StartsWithSegments("/api") &&
    !context.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase), staticFiles =>
{
    staticFiles.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            var relativePath = context.Context.Request.Path.Value ?? string.Empty;
            context.Context.Response.Headers.CacheControl = relativePath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                ? "public,max-age=31536000,immutable"
                : "no-cache";
        }
    });
});

app.Use(async (context, next) =>
{
    var requestLimit = IsPublicAuthEndpoint(context.Request.Path)
        ? BugReportLimits.PublicAuthRequestBodyBytes
        : context.Request.Method == HttpMethods.Post &&
          context.Request.Path.Value?.TrimEnd('/').EndsWith("/attachments", StringComparison.OrdinalIgnoreCase) == true
            ? BugReportLimits.MaxMultipartRequestBodyBytes
            : BugReportLimits.MaxApiRequestBodyBytes;

    if (context.Request.ContentLength > requestLimit)
    {
        await WritePayloadTooLargeAsync(context);
        return;
    }

    var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (bodySizeFeature is { IsReadOnly: false })
    {
        bodySizeFeature.MaxRequestBodySize = requestLimit;
    }

    try
    {
        await next(context);
    }
    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
    {
        await WritePayloadTooLargeAsync(context);
    }
});
app.UseRouting();
app.UseMiddleware<ResetMaintenanceMiddleware>();
app.UseCors("FrontendDev");
app.UseRateLimiter();
app.UseWebSockets();
app.Map("/api/agent/notifications/ws", agentWs =>
{
    agentWs.Run(AgentNotificationWebSocketEndpoint.HandleAsync);
});
app.UseMiddleware<AuthMiddleware>();
app.UseMiddleware<FirstRunSetupMiddleware>();
app.UseMiddleware<AuthenticatedAbuseMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (DeliveryReadinessService readiness, CancellationToken ct) =>
{
    var result = await readiness.CheckAsync(ct);
    return Results.Json(result, statusCode: result.IsReady
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable);
});
app.MapAuthEndpoints();
app.MapFirstRunSetupEndpoints();
app.MapDemoInfoEndpoints();
app.MapBugEndpoints();
app.MapProjectEndpoints();
app.MapDocsEndpoints();
app.MapAuditEndpoints();
app.MapNotificationEndpoints();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") ||
        (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
        (Path.HasExtension(context.Request.Path.Value) &&
         !context.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexFile = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
    if (!indexFile.Exists)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-cache";
    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await using var stream = indexFile.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = await reader.ReadToEndAsync(context.RequestAborted);
        var nonce = context.Items["csp_nonce"]?.ToString() ?? string.Empty;
        var demoConfig = app.Environment.IsEnvironment("Demo") && app.Configuration.GetValue<bool>("Demo:PublicEnabled")
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                DemoPublicConfiguration.Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            : string.Empty;
        html = html.Replace("__CSP_NONCE__", nonce, StringComparison.Ordinal)
            .Replace("__DEMO_CONFIG__", demoConfig, StringComparison.Ordinal);
        await context.Response.WriteAsync(html, context.RequestAborted);
    }
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var url in app.Urls)
    {
        startupLogger.LogInformation("BugTracker API listening on {Url}", url);
        startupLogger.LogInformation("Agent notification WebSocket listening on {Url}", ToWebSocketUrl(url, "/api/agent/notifications/ws"));
    }
});

app.Run();

static async Task WritePayloadTooLargeAsync(HttpContext context)
{
    context.Response.Clear();
    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(
        new { error = "request body too large", errorCode = "payload_too_large" },
        context.RequestAborted);
}

static void AddIpPolicy(
    RateLimiterOptions rateLimiterOptions,
    string policyName,
    EndpointRateLimitOptions policyOptions)
{
    rateLimiterOptions.AddPolicy(policyName, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = policyOptions.IpPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(policyOptions.IpWindowMinutes)
            }));
}

static bool IsPublicAuthEndpoint(PathString path)
{
    var normalized = path.Value?.TrimEnd('/') ?? string.Empty;
    return normalized.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/api/auth/agent/login", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/api/auth/setup-password", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/api/auth/request-access", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/api/auth/request-credential-recovery", StringComparison.OrdinalIgnoreCase);
}

static bool IsLoginEndpoint(PathString path)
{
    var normalized = path.Value?.TrimEnd('/') ?? string.Empty;
    return normalized.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/api/auth/agent/login", StringComparison.OrdinalIgnoreCase);
}

static bool IsAllowedFrontendOrigin(string? origin, string configuredOrigin, bool allowLocalDevOrigins)
{
    if (string.IsNullOrWhiteSpace(origin))
    {
        return false;
    }

    if (string.Equals(NormalizeOrigin(origin), NormalizeOrigin(configuredOrigin), StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!allowLocalDevOrigins || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1")
        && uri.Port is >= 5173 and <= 5199;
}

static string NormalizeOrigin(string origin)
{
    return origin.Trim().TrimEnd('/');
}

static string ToWebSocketUrl(string baseUrl, string path)
{
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
    {
        return baseUrl.TrimEnd('/') + path;
    }

    var scheme = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
    return new UriBuilder(uri) { Scheme = scheme, Path = path.TrimStart('/'), Query = string.Empty }.Uri.ToString();
}

static string[] ResolveStartupUrls(string? configuredUrls, bool allowDynamicPortFallback)
{
    if (string.IsNullOrWhiteSpace(configuredUrls))
    {
        return [];
    }

    var urls = configuredUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (!allowDynamicPortFallback)
    {
        return urls;
    }

    var reservedPorts = new HashSet<int>();
    var resolvedUrls = new List<string>(urls.Length);

    foreach (var configuredUrl in urls)
    {
        var resolvedUrl = ResolveAvailableUrl(configuredUrl, reservedPorts);
        resolvedUrls.Add(resolvedUrl);
    }

    return [.. resolvedUrls];
}

static bool ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
{
    var section = configuration.GetSection("ForwardedHeaders");
    if (!section.GetValue<bool>("Enabled"))
    {
        return false;
    }

    var knownProxyValues = section.GetSection("KnownProxies").Get<string[]>() ?? [];
    if (knownProxyValues.Length == 0)
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:Enabled requires at least one explicit ForwardedHeaders:KnownProxies IP address.");
    }

    var knownProxies = knownProxyValues.Select(value =>
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains invalid IP address '{value}'.");
        }

        return address;
    }).ToArray();
    var forwardLimit = section.GetValue<int?>("ForwardLimit") ?? 1;
    if (forwardLimit < 1)
    {
        throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be at least 1.");
    }

    services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = forwardLimit;
        options.RequireHeaderSymmetry = true;
        options.KnownProxies.Clear();
        foreach (var knownProxy in knownProxies)
        {
            options.KnownProxies.Add(knownProxy);
        }
    });

    return true;
}

static string ResolveAvailableUrl(string configuredUrl, HashSet<int> reservedPorts)
{
    if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri) || !UsesTcpPort(uri))
    {
        return configuredUrl;
    }

    var availablePort = FindAvailablePort(uri.Host, uri.Port, reservedPorts);
    reservedPorts.Add(availablePort);

    if (availablePort == uri.Port)
    {
        return configuredUrl;
    }

    var resolvedUrl = new UriBuilder(uri) { Port = availablePort }.Uri.ToString().TrimEnd('/');
    Console.WriteLine($"Configured URL {configuredUrl} is already in use; using {resolvedUrl} instead.");
    return resolvedUrl;
}

static bool UsesTcpPort(Uri uri)
{
    return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Port > 0;
}

static int FindAvailablePort(string host, int startPort, HashSet<int> reservedPorts)
{
    for (var port = startPort; port <= IPEndPoint.MaxPort; port++)
    {
        if (!reservedPorts.Contains(port) && IsPortAvailable(host, port))
        {
            return port;
        }
    }

    throw new InvalidOperationException($"No available TCP port found at or above {startPort} for host {host}.");
}

static bool IsPortAvailable(string host, int port)
{
    try
    {
        var listener = new TcpListener(ResolveBindAddress(host), port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
    catch (ArgumentException)
    {
        return false;
    }
}

static IPAddress ResolveBindAddress(string host)
{
    if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return IPAddress.Loopback;
    }

    if (host is "*" or "+" or "0.0.0.0" or "::")
    {
        return IPAddress.Any;
    }

    return IPAddress.TryParse(host, out var address) ? address : IPAddress.Loopback;
}

static string ResolveDatabasePath(string? configuredPath, string contentRootPath)
{
    var path = string.IsNullOrWhiteSpace(configuredPath) ? "../../bug_tracker.db" : configuredPath;
    return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(contentRootPath, path));
}

static string ResolveAuditLogDirectory(string? configuredPath, string contentRootPath)
{
    var path = string.IsNullOrWhiteSpace(configuredPath) ? "logs" : configuredPath;
    return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(contentRootPath, path));
}

public partial class Program
{
}
