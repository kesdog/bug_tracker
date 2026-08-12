using BugTracker.Api.Notifications;

namespace BugTracker.Api.Health;

public sealed class ApplicationShutdownService(
    OutboxDispatchGate outboxDispatchGate,
    OutboxDispatcher outboxDispatcher,
    AgentNotificationSocketHub socketHub,
    ILogger<ApplicationShutdownService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken ct)
    {
        logger.LogInformation("Stopping WebSocket admission and draining background delivery before shutdown.");
        IDisposable? pause = null;
        try
        {
            pause = outboxDispatchGate.Pause();
        }
        catch (InvalidOperationException)
        {
            // A reset already owns the pause. Shutdown still cancels and drains its work.
        }
        using (pause)
        {
        outboxDispatcher.CancelActiveDispatches();

        var socketTask = socketHub.PauseAndCloseAllAsync(ct);
        var outboxTask = outboxDispatchGate.WaitForDrainAsync(ct);
        await Task.WhenAll(socketTask, outboxTask);
        await outboxDispatcher.ReleaseClaimsAsync(ct);
        }
    }
}
