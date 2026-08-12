using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using BugTracker.Api.Bugs;
using BugTracker.Api.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace BugTracker.Api.Auth;

public sealed class AuthenticatedAbuseOptions
{
    public bool Enabled { get; init; } = true;
    public long StorageSafetyReserveBytes { get; init; } = 268_435_456;
    public int MaxProjects { get; init; } = 25;
    public int MaxTickets { get; init; } = 300;
    public int MaxComments { get; init; } = 600;
    public int MaxAttachments { get; init; } = 100;
    public long MaxEvidenceBytes { get; init; } = 268_435_456;
    public int GeneralPermitLimit { get; init; } = 180;
    public int GeneralWindowMinutes { get; init; } = 1;
    public int CreatePermitLimit { get; init; } = 10;
    public int CreateWindowMinutes { get; init; } = 10;
    public int WritePermitLimit { get; init; } = 60;
    public int WriteWindowMinutes { get; init; } = 10;
    public int UploadPermitLimit { get; init; } = 10;
    public int UploadWindowMinutes { get; init; } = 10;
    public int ExportPermitLimit { get; init; } = 10;
    public int ExportWindowMinutes { get; init; } = 10;
    public int WebSocketPermitLimit { get; init; } = 10;
    public int WebSocketWindowMinutes { get; init; } = 5;
}

public sealed class AuthenticatedAbuseProtection : IDisposable
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly AuthenticatedAbuseOptions _options;
    private readonly TicketDataStorageService _ticketDataStorage;
    private readonly IReadOnlyDictionary<string, (int Permits, TimeSpan Window)> _limits;
    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _limiters = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AuthenticatedAbuseProtection(
        SqliteConnectionFactory connectionFactory,
        TicketDataStorageService ticketDataStorage,
        IOptions<AuthenticatedAbuseOptions> options)
    {
        _connectionFactory = connectionFactory;
        _ticketDataStorage = ticketDataStorage;
        _options = options.Value;
        _limits = new Dictionary<string, (int, TimeSpan)>(StringComparer.Ordinal)
        {
            ["general"] = (_options.GeneralPermitLimit, TimeSpan.FromMinutes(_options.GeneralWindowMinutes)),
            ["create"] = (_options.CreatePermitLimit, TimeSpan.FromMinutes(_options.CreateWindowMinutes)),
            ["write"] = (_options.WritePermitLimit, TimeSpan.FromMinutes(_options.WriteWindowMinutes)),
            ["upload"] = (_options.UploadPermitLimit, TimeSpan.FromMinutes(_options.UploadWindowMinutes)),
            ["export"] = (_options.ExportPermitLimit, TimeSpan.FromMinutes(_options.ExportWindowMinutes)),
            ["websocket"] = (_options.WebSocketPermitLimit, TimeSpan.FromMinutes(_options.WebSocketWindowMinutes))
        };
    }

    public RateLimitLease Acquire(string category, string partition, int permitMultiplier = 1)
    {
        var limit = _limits[category];
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

    public async Task<AbuseQuotaFailure?> CheckQuotaAsync(
        string category,
        long incomingBytes,
        bool enforceResourceQuotas,
        CancellationToken ct)
    {
        if (!await _ticketDataStorage.CanGrowAsync(incomingBytes, ct))
        {
            return new AbuseQuotaFailure("ticket_data_capacity_reached", "The ticket data storage capacity has been reached.");
        }

        if (!enforceResourceQuotas)
        {
            return null;
        }

        if (!HasStorageReserve(incomingBytes))
        {
                return new AbuseQuotaFailure("storage_reserve_reached", "The storage safety reserve has been reached.");
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

    private static AbuseQuotaFailure Quota(string resource, long limit) =>
        new("quota_exceeded", $"The {resource} quota of {limit} has been reached.");

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

public sealed record AbuseQuotaFailure(string ErrorCode, string Message);

public sealed class AuthenticatedAbuseMiddleware(IOptions<AuthenticatedAbuseOptions> options, AuthenticatedAbuseProtection protection) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.Items[AuthMiddleware.AuthContextKey] is not AuthenticatedUser principal)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var category = ClassifyRateCategory(context.Request.Method, path);
        var ipPartition = $"ip:{context.Connection.RemoteIpAddress}";
        var userPartition = $"user:{principal.UserId}";
        if (options.Value.Enabled)
        {
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
        }

        if (category.QuotaCategory is null)
        {
            await next(context);
            return;
        }

        using var gate = await protection.EnterWriteGateAsync(context.RequestAborted);
        var incomingBytes = context.Request.ContentLength ?? 0;
        var failure = await protection.CheckQuotaAsync(
            category.QuotaCategory, incomingBytes, options.Value.Enabled, context.RequestAborted);
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
