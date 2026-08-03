namespace BugTracker.Api.Audit;

public sealed class SystemLifecycleAuditService(
    AuditRepository repository,
    AuditLogger auditLogger) : IHostedService
{
    private const string SystemUserId = "system";

    public async Task StartAsync(CancellationToken ct)
    {
        await repository.EnsureSystemUserAsync(ct);
        var hasStartedBefore = await repository.HasSystemStartEventAsync(ct);
        var action = hasStartedBefore ? "system.restarted" : "system.started";
        var message = hasStartedBefore ? "Application restarted." : "Application started.";
        await auditLogger.LogAsync(SystemUserId, "system", action, message, null, null, ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await repository.EnsureSystemUserAsync(ct);
        await auditLogger.LogAsync(SystemUserId, "system", "system.shutdown", "Application shut down gracefully.", null, null, ct);
    }
}
