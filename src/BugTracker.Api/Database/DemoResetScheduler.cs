namespace BugTracker.Api.Database;

public sealed class DemoResetScheduler(
    DemoResetCoordinator coordinator,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<DemoResetOptions> configuredOptions,
    ILogger<DemoResetScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(1);
    private readonly DemoResetOptions _options = configuredOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!coordinator.IsEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await coordinator.RunIfDueAsync(stoppingToken))
                {
                    continue;
                }

                if (await coordinator.HasPendingCleanupAsync(stoppingToken))
                {
                    await Task.Delay(FailureRetryDelay, timeProvider, stoppingToken);
                    continue;
                }

                var delay = DemoResetCoordinator.NextScheduledAt(timeProvider.GetUtcNow(), _options.HourUtc)
                    - timeProvider.GetUtcNow();
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled demo reset failed and will be retried.");
                await Task.Delay(FailureRetryDelay, timeProvider, stoppingToken);
            }
        }
    }
}
