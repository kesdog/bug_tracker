using System.Threading.RateLimiting;

namespace BugTracker.Api.Auth;

public static class PublicRateLimitPolicies
{
    public const string HumanLogin = "human-login";
    public const string AgentLogin = "agent-login";
    public const string PasswordSetup = "password-setup";
    public const string AccessRequest = "access-request";
}

public sealed class PublicEndpointRateLimitOptions
{
    public EndpointRateLimitOptions HumanLogin { get; init; } = new(30, 5, 10, 15);
    public EndpointRateLimitOptions AgentLogin { get; init; } = new(30, 5, 10, 15);
    public EndpointRateLimitOptions PasswordSetup { get; init; } = new(20, 15, 5, 60);
    public EndpointRateLimitOptions AccessRequest { get; init; } = new(20, 60, 8, 1440);

    public EndpointRateLimitOptions GetPolicy(string policyName) => policyName switch
    {
        PublicRateLimitPolicies.HumanLogin => HumanLogin,
        PublicRateLimitPolicies.AgentLogin => AgentLogin,
        PublicRateLimitPolicies.PasswordSetup => PasswordSetup,
        PublicRateLimitPolicies.AccessRequest => AccessRequest,
        _ => throw new ArgumentOutOfRangeException(nameof(policyName), policyName, "Unknown public endpoint rate-limit policy.")
    };
}

public sealed record EndpointRateLimitOptions(
    int IpPermitLimit,
    int IpWindowMinutes,
    int IdentityPermitLimit,
    int IdentityWindowMinutes);

public sealed class PublicEndpointIdentityRateLimiter : IDisposable
{
    private static readonly HashSet<string> SharedDemoHumanEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin@example.com",
        "alex.senior@example.com",
        "morgan.senior@example.com",
        "ava.dev@example.com",
        "noah.dev@example.com",
        "mia.dev@example.com",
        "liam.dev@example.com"
    };

    private readonly TokenService _tokenService;
    private readonly ILogger<PublicEndpointIdentityRateLimiter> _logger;
    private readonly IReadOnlyDictionary<string, PartitionedRateLimiter<string>> _limiters;
    private readonly bool _isDemo;

    public PublicEndpointIdentityRateLimiter(
        PublicEndpointRateLimitOptions options,
        TokenService tokenService,
        IHostEnvironment environment,
        ILogger<PublicEndpointIdentityRateLimiter> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
        _isDemo = environment.IsEnvironment("Demo");
        _limiters = new Dictionary<string, PartitionedRateLimiter<string>>
        {
            [PublicRateLimitPolicies.HumanLogin] = CreateLimiter(options.HumanLogin),
            [PublicRateLimitPolicies.AgentLogin] = CreateLimiter(options.AgentLogin),
            [PublicRateLimitPolicies.PasswordSetup] = CreateLimiter(options.PasswordSetup),
            [PublicRateLimitPolicies.AccessRequest] = CreateLimiter(options.AccessRequest)
        };
    }

    public async ValueTask<RateLimitLease> AcquireAsync(string policyName, string normalizedIdentity, CancellationToken ct)
    {
        if (_isDemo && policyName == PublicRateLimitPolicies.HumanLogin && SharedDemoHumanEmails.Contains(normalizedIdentity))
        {
            return new DemoAcquiredLease();
        }

        var partitionKey = _tokenService.HashToken(normalizedIdentity);
        var lease = await _limiters[policyName].AcquireAsync(partitionKey, 1, ct);
        if (!lease.IsAcquired)
        {
            _logger.LogWarning(
                "Public endpoint identity rate limit rejected request for policy {Policy} and identity fingerprint {Fingerprint}.",
                policyName,
                partitionKey[..12]);
        }

        return lease;
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
    }

    private static PartitionedRateLimiter<string> CreateLimiter(EndpointRateLimitOptions options)
    {
        return PartitionedRateLimiter.Create<string, string>(partitionKey =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = options.IdentityPermitLimit,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(options.IdentityWindowMinutes)
                }));
    }

    private sealed class DemoAcquiredLease : RateLimitLease
    {
        public override bool IsAcquired => true;
        public override IEnumerable<string> MetadataNames => [];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}

public static class RateLimitResponses
{
    public static IResult TooManyRequests(HttpContext context, RateLimitLease lease)
    {
        SetRetryAfter(context.Response, lease);
        return Results.Json(
            new { error = "too many requests", errorCode = "rate_limited" },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    public static void SetRetryAfter(HttpResponse response, RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        response.Headers.CacheControl = "no-store";
    }
}
