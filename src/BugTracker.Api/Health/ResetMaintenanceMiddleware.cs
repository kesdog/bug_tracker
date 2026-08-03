namespace BugTracker.Api.Health;

public sealed class ResetMaintenanceMiddleware(IResetMaintenanceState maintenanceState) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var isReadiness = context.Request.Path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase);
        if (!context.Request.Path.StartsWithSegments("/api") && !isReadiness)
        {
            await next(context);
            return;
        }

        var isAgentSocket = context.Request.Path.StartsWithSegments("/api/agent/notifications/ws");
        var requestLease = maintenanceState.TryBeginApiRequest();
        if (requestLease is null)
        {
            if (isReadiness)
            {
                // Readiness has its own reset-specific response contract and performs no database
                // work after observing maintenance.
                await next(context);
                return;
            }

            await RejectAsync(context);
            return;
        }

        using var finiteLease = new FiniteRequestLease(requestLease);
        if (isAgentSocket)
        {
            // The endpoint completes this lease after audit persistence, hub registration, and
            // immediate pre-hello session validation. The established socket is then owned by the hub.
            context.Features.Set<IWebSocketEstablishmentLease>(finiteLease);
        }

        await next(context);
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        context.Response.Headers.RetryAfter = "60";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "service temporarily unavailable during demo reset",
            errorCode = "demo_reset_in_progress"
        }, context.RequestAborted);
    }
}

public interface IWebSocketEstablishmentLease
{
    void CompleteEstablishment();
}

internal sealed class FiniteRequestLease(IDisposable inner) : IDisposable, IWebSocketEstablishmentLease
{
    private IDisposable? _inner = inner;

    public void CompleteEstablishment() => Dispose();

    public void Dispose() => Interlocked.Exchange(ref _inner, null)?.Dispose();
}
