using BugTracker.Api.Health;
using BugTracker.Api.Notifications;
using Microsoft.Extensions.Options;

namespace BugTracker.Api.Database;

public sealed class DemoResetCoordinator(
    DemoResetService resetService,
    IOptions<DemoResetOptions> configuredOptions,
    IHostEnvironment environment,
    IResetMaintenanceState maintenanceState,
    OutboxDispatchGate outboxDispatchGate,
    AgentNotificationSocketHub socketHub,
    AuditFilePublisher auditFilePublisher,
    TimeProvider timeProvider,
    ILogger<DemoResetCoordinator> logger)
{
    private readonly SemaphoreSlim _resetLock = new(1, 1);
    private readonly DemoResetOptions _options = configuredOptions.Value;

    public bool IsEnabled => _options.Enabled;

    public static bool IsDue(DateTimeOffset? lastResetAt, DateTimeOffset now, int hourUtc)
    {
        if (hourUtc is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hourUtc));
        }
        if (lastResetAt is null)
        {
            return true;
        }

        var utcNow = now.ToUniversalTime();
        var lastResetDate = lastResetAt.Value.ToUniversalTime().Date;
        if (lastResetDate >= utcNow.Date)
        {
            return false;
        }
        var scheduledToday = new DateTimeOffset(
            utcNow.Year, utcNow.Month, utcNow.Day, hourUtc, 0, 0, TimeSpan.Zero);
        return utcNow >= scheduledToday || lastResetDate < utcNow.Date.AddDays(-1);
    }

    public static DateTimeOffset NextScheduledAt(DateTimeOffset now, int hourUtc)
    {
        var utcNow = now.ToUniversalTime();
        var today = new DateTimeOffset(utcNow.Year, utcNow.Month, utcNow.Day, hourUtc, 0, 0, TimeSpan.Zero);
        return utcNow < today ? today : today.AddDays(1);
    }

    public async Task<bool> RunIfDueAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        _options.AssertAllowed(environment.EnvironmentName);
        await _resetLock.WaitAsync(ct);
        try
        {
            var now = timeProvider.GetUtcNow();
            var state = await resetService.GetStateAsync(ct);
            var resetDue = IsDue(state.LastResetAt, now, _options.HourUtc);
            if (!state.CleanupPending && !resetDue)
            {
                return false;
            }

            using var maintenanceLease = await DrainWithTimeoutAsync(
                token => maintenanceState.BeginResetAndDrainAsync(token), "API requests", ct);
            using var outboxLease = await DrainWithTimeoutAsync(
                token => outboxDispatchGate.PauseAndDrainAsync(token), "outbox dispatch", ct);
            try
            {
                await DrainTaskWithTimeoutAsync(
                    token => socketHub.PauseAndCloseAllAsync(token), "agent WebSocket handlers", ct);

                if (state.CleanupPending)
                {
                    var cleanupCompleted = await TryCompletePendingCleanupAsync(state, ct);
                    if (!cleanupCompleted)
                    {
                        return false;
                    }

                    state = await resetService.GetStateAsync(ct);
                    resetDue = IsDue(state.LastResetAt, timeProvider.GetUtcNow(), _options.HourUtc);
                    if (!resetDue)
                    {
                        return false;
                    }
                }

                var result = await resetService.ResetAsync(_options, environment.EnvironmentName, ct);
                var cleanupCompletedAfterReset = false;
                try
                {
                    var postCommitState = new DemoResetState(
                        result.Generation, result.ResetAt, true, false, false);
                    cleanupCompletedAfterReset = await TryCompletePendingCleanupAsync(postCommitState, ct);
                }
                catch (Exception error)
                {
                    // ResetAsync only returns after the canonical transaction commits. Cleanup is
                    // durable pending work and must never make that committed reset appear to fail.
                    logger.LogWarning(error, "Demo reset committed; post-commit cleanup remains pending and will be retried.");
                }
                logger.LogInformation(
                    "Atomic demo reset generation {Generation} committed at {ResetAt}; cleanup completed: {CleanupCompleted}.",
                    result.Generation,
                    result.ResetAt,
                    cleanupCompletedAfterReset);
                return true;
            }
            finally
            {
                socketHub.ResumeConnections();
            }
        }
        finally
        {
            _resetLock.Release();
        }
    }

    public async Task<bool> HasPendingCleanupAsync(CancellationToken ct = default) =>
        IsEnabled && (await resetService.GetStateAsync(ct)).CleanupPending;

    private async Task<bool> TryCompletePendingCleanupAsync(DemoResetState state, CancellationToken ct)
    {
        var walCompleted = state.WalCheckpointCompleted;
        var auditCompleted = state.AuditFileCleanupCompleted;

        if (!walCompleted)
        {
            walCompleted = await TryCleanupStepAsync(async token =>
            {
                await resetService.CheckpointWalAsync(token);
                await resetService.MarkWalCheckpointCompletedAsync(token);
            }, "WAL", ct);
        }

        // This is deliberately independent of checkpoint success. Old JSONL identity data must be
        // removed even when a SQLite reader temporarily prevents WAL truncation.
        if (!auditCompleted)
        {
            auditCompleted = await TryCleanupStepAsync(async token =>
            {
                await auditFilePublisher.ClearAsync(token);
                await resetService.MarkAuditFileCleanupCompletedAsync(token);
            }, "audit file", ct);
        }

        if (!walCompleted || !auditCompleted)
        {
            return false;
        }

        await resetService.CompleteCleanupAsync(ct);
        return !(await resetService.GetStateAsync(ct)).CleanupPending;
    }

    private async Task<bool> TryCleanupStepAsync(
        Func<CancellationToken, Task> cleanup,
        string step,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.DrainTimeoutSeconds));
        try
        {
            await cleanup(timeout.Token);
            return true;
        }
        catch (OperationCanceledException error) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(error, "Demo reset {CleanupStep} cleanup exceeded its bound and will be retried.", step);
            return false;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Demo reset {CleanupStep} cleanup remains pending and will be retried.", step);
            return false;
        }
    }

    private async Task<IDisposable> DrainWithTimeoutAsync(
        Func<CancellationToken, Task<IDisposable>> drain,
        string operation,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.DrainTimeoutSeconds));
        try
        {
            return await drain(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Demo reset aborted because {Operation} did not drain within {TimeoutSeconds} seconds.",
                operation,
                _options.DrainTimeoutSeconds);
            throw new TimeoutException(
                $"Demo reset aborted because {operation} did not drain within {_options.DrainTimeoutSeconds} seconds.");
        }
    }

    private async Task DrainTaskWithTimeoutAsync(
        Func<CancellationToken, Task> drain,
        string operation,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.DrainTimeoutSeconds));
        try
        {
            await drain(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Demo reset aborted because {Operation} did not drain within {TimeoutSeconds} seconds.",
                operation,
                _options.DrainTimeoutSeconds);
            throw new TimeoutException(
                $"Demo reset aborted because {operation} did not drain within {_options.DrainTimeoutSeconds} seconds.");
        }
    }
}
