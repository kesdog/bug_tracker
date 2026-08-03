using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using BugTracker.Api.Bugs;
using BugTracker.Api.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace BugTracker.Api.Auth;

public sealed class DemoAbuseOptions
{
    public long StorageSafetyReserveBytes { get; init; } = 268_435_456;
    public int MaxProjects { get; init; } = 25;
    public int MaxTickets { get; init; } = 300;
    public int MaxComments { get; init; } = 600;
    public int MaxAttachments { get; init; } = 100;
    public long MaxEvidenceBytes { get; init; } = 268_435_456;
}

public sealed class DemoAbuseProtection : IDisposable
{
    private static readonly IReadOnlyDictionary<string, (int Permits, TimeSpan Window)> Limits =
        new Dictionary<string, (int, TimeSpan)>(StringComparer.Ordinal)
        {
            ["general"] = (180, TimeSpan.FromMinutes(1)),
            ["create"] = (10, TimeSpan.FromMinutes(10)),
            ["write"] = (60, TimeSpan.FromMinutes(10)),
            ["upload"] = (10, TimeSpan.FromMinutes(10)),
            ["export"] = (10, TimeSpan.FromMinutes(10)),
            ["websocket"] = (10, TimeSpan.FromMinutes(5))
        };

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DemoAbuseOptions _options;
    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _limiters = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public DemoAbuseProtection(SqliteConnectionFactory connectionFactory, IOptions<DemoAbuseOptions> options)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
    }

    public RateLimitLease Acquire(string category, string partition, int permitMultiplier = 1)
    {
        var limit = Limits[category];
        var limiter = _limiters.GetOrAdd($"{category}:{permitMultiplier}:{partition}", _ => new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = checked(limit.Permits * permitMultiplier),
                QueueLimit = 0,
                Window = limit.Window
            }));
        return limiter.AttemptAcquire();
    }

    public async Task<IDisposable> EnterWriteGateAsync(CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        return new GateLease(_writeGate);
    }

    public async Task<DemoQuotaFailure?> CheckQuotaAsync(string category, long incomingBytes, CancellationToken ct)
    {
        if (!HasStorageReserve(incomingBytes))
        {
            return new DemoQuotaFailure("storage_reserve_reached", "The public demo storage reserve has been reached.");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(readOnly: true, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM projects),
                (SELECT COUNT(*) FROM bug_tickets),
                (SELECT COUNT(*) FROM ticket_activity WHERE kind = 'comment'),
                (SELECT COUNT(*) FROM ticket_attachments),
                COALESCE((SELECT SUM(size_bytes) FROM ticket_attachments), 0) +
                COALESCE((SELECT SUM(
                    length(COALESCE(report_images_json, '')) +
                    length(COALESCE(resolution_report_images_json, '')) +
                    length(COALESCE(text_evidence_json, ''))
                ) FROM bug_tickets), 0);
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var projects = reader.GetInt32(0);
        var tickets = reader.GetInt32(1);
        var comments = reader.GetInt32(2);
        var attachments = reader.GetInt32(3);
        var evidenceBytes = reader.GetInt64(4);

        if (category == "project" && projects >= _options.MaxProjects)
            return Quota("projects", _options.MaxProjects);
        if (category == "ticket" && tickets >= _options.MaxTickets)
            return Quota("tickets", _options.MaxTickets);
        if (category == "comment" && comments >= _options.MaxComments)
            return Quota("comments", _options.MaxComments);
        if (category == "attachment" && attachments + BugReportLimits.MaxImagesPerReport > _options.MaxAttachments)
            return Quota("attachments", _options.MaxAttachments);
        if (incomingBytes > 0 && evidenceBytes + incomingBytes > _options.MaxEvidenceBytes)
            return Quota("evidence bytes", _options.MaxEvidenceBytes);

        return null;
    }

    private bool HasStorageReserve(long incomingBytes)
    {
        try
        {
            var root = Path.GetPathRoot(_connectionFactory.DatabaseDirectoryPath);
            if (string.IsNullOrWhiteSpace(root)) return false;
            var available = new DriveInfo(root).AvailableFreeSpace;
            return available - Math.Max(0, incomingBytes) >= _options.StorageSafetyReserveBytes;
        }
        catch
        {
            return false;
        }
    }

    private static DemoQuotaFailure Quota(string resource, long limit) =>
        new("demo_quota_exceeded", $"The public demo {resource} quota of {limit} has been reached.");

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values) limiter.Dispose();
        _writeGate.Dispose();
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

public sealed record DemoQuotaFailure(string ErrorCode, string Message);

public sealed class DemoAbuseMiddleware(IHostEnvironment environment, DemoAbuseProtection protection) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!environment.IsEnvironment("Demo") ||
            !context.Request.Path.StartsWithSegments("/api") ||
            context.Items[AuthMiddleware.AuthContextKey] is not AuthenticatedUser principal)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var category = ClassifyRateCategory(context.Request.Method, path);
        var ipPartition = $"ip:{context.Connection.RemoteIpAddress}";
        var userPartition = $"user:{principal.UserId}";
        using var ipLease = protection.Acquire("general", ipPartition);
        using var userLease = protection.Acquire("general", userPartition, permitMultiplier: 10);
        if (!ipLease.IsAcquired || !userLease.IsAcquired)
        {
            await WriteRateLimitAsync(context, !ipLease.IsAcquired ? ipLease : userLease);
            return;
        }

        if (category.RateCategory != "general")
        {
            using var categoryIpLease = protection.Acquire(category.RateCategory, ipPartition);
            using var categoryUserLease = protection.Acquire(category.RateCategory, userPartition, permitMultiplier: 10);
            if (!categoryIpLease.IsAcquired || !categoryUserLease.IsAcquired)
            {
                await WriteRateLimitAsync(context, !categoryIpLease.IsAcquired ? categoryIpLease : categoryUserLease);
                return;
            }
        }

        if (category.QuotaCategory is null)
        {
            await next(context);
            return;
        }

        using var gate = await protection.EnterWriteGateAsync(context.RequestAborted);
        var incomingBytes = category.QuotaCategory is "attachment" or "ticket" or "evidence"
            ? context.Request.ContentLength ?? 0
            : 0;
        var failure = await protection.CheckQuotaAsync(category.QuotaCategory, incomingBytes, context.RequestAborted);
        if (failure is not null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/json";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { error = failure.Message, errorCode = failure.ErrorCode }, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static (string RateCategory, string? QuotaCategory) ClassifyRateCategory(string method, string path)
    {
        if (path.Equals("/api/bugs/export", StringComparison.OrdinalIgnoreCase)) return ("export", null);
        if (path.EndsWith("/attachments", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Post) return ("upload", "attachment");
        if (path.Equals("/api/bugs", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Post) return ("create", "ticket");
        if (path.Equals("/api/projects", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Post) return ("create", "project");
        if (path.EndsWith("/comments", StringComparison.OrdinalIgnoreCase) && method == HttpMethods.Post) return ("write", "comment");
        if ((path.EndsWith("/initial-report", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith("/report", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith("/close", StringComparison.OrdinalIgnoreCase)) && method == HttpMethods.Patch) return ("write", "evidence");
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method)) return ("write", "write");
        return ("general", null);
    }

    private static async Task WriteRateLimitAsync(HttpContext context, RateLimitLease lease)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        RateLimitResponses.SetRetryAfter(context.Response, lease);
        await context.Response.WriteAsJsonAsync(new { error = "too many requests", errorCode = "rate_limited" }, context.RequestAborted);
    }
}
