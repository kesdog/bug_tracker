namespace BugTracker.Api.Health;

public interface IResetMaintenanceState
{
    bool IsResetInProgress { get; }

    IDisposable BeginReset();

    IDisposable? TryBeginApiRequest();

    Task<IDisposable> BeginResetAndDrainAsync(CancellationToken ct = default);
}

public sealed class ResetMaintenanceState : IResetMaintenanceState
{
    private readonly object _sync = new();
    private int _activeResetLeases;
    private int _activeApiRequests;
    private TaskCompletionSource _drained = CompletedSource();

    public bool IsResetInProgress => Volatile.Read(ref _activeResetLeases) > 0;

    public IDisposable BeginReset()
    {
        lock (_sync)
        {
            _activeResetLeases++;
        }
        return new ResetLease(this);
    }

    public IDisposable? TryBeginApiRequest()
    {
        lock (_sync)
        {
            if (_activeResetLeases > 0)
            {
                return null;
            }

            if (_activeApiRequests++ == 0)
            {
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return new ApiRequestLease(this);
        }
    }

    public async Task<IDisposable> BeginResetAndDrainAsync(CancellationToken ct = default)
    {
        Task drainTask;
        lock (_sync)
        {
            _activeResetLeases++;
            drainTask = _activeApiRequests == 0 ? Task.CompletedTask : _drained.Task;
        }

        try
        {
            await drainTask.WaitAsync(ct);
            return new ResetLease(this);
        }
        catch
        {
            EndReset();
            throw;
        }
    }

    private void EndReset()
    {
        lock (_sync)
        {
            _activeResetLeases--;
        }
    }

    private void EndApiRequest()
    {
        lock (_sync)
        {
            if (--_activeApiRequests == 0)
            {
                _drained.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed class ResetLease(ResetMaintenanceState owner) : IDisposable
    {
        private ResetMaintenanceState? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndReset();
        }
    }


    private sealed class ApiRequestLease(ResetMaintenanceState owner) : IDisposable
    {
        private ResetMaintenanceState? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndApiRequest();
    }
}
