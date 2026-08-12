namespace BugTracker.Api.Health;

public interface IResetMaintenanceState
{
    bool IsResetInProgress { get; }

    IDisposable BeginReset();

    IDisposable? TryBeginApiRequest(CancellationTokenSource? cancellation = null, Action? abort = null);

    Task<IDisposable> BeginResetAndDrainAsync(CancellationToken ct = default);

    Task WaitForDrainAsync(CancellationToken ct = default);

    Task CancelActiveRequestsAndDrainAsync(CancellationToken ct = default);
}

public sealed class ResetMaintenanceState : IResetMaintenanceState
{
    private readonly object _sync = new();
    private int _activeResetLeases;
    private int _activeApiRequests;
    private readonly HashSet<ActiveRequest> _requests = [];
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

    public IDisposable? TryBeginApiRequest(CancellationTokenSource? cancellation = null, Action? abort = null)
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
            var request = new ActiveRequest(cancellation, abort);
            _requests.Add(request);
            return new ApiRequestLease(this, request);
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

    public Task WaitForDrainAsync(CancellationToken ct = default)
    {
        Task drainTask;
        lock (_sync)
        {
            drainTask = _activeApiRequests == 0 ? Task.CompletedTask : _drained.Task;
        }
        return drainTask.WaitAsync(ct);
    }

    public async Task CancelActiveRequestsAndDrainAsync(CancellationToken ct = default)
    {
        ActiveRequest[] requests;
        lock (_sync)
        {
            requests = [.. _requests];
        }
        foreach (var request in requests) request.CancelAndAbort();
        await WaitForDrainAsync(ct);
    }

    private void EndReset()
    {
        lock (_sync)
        {
            _activeResetLeases--;
        }
    }

    private void EndApiRequest(ActiveRequest request)
    {
        lock (_sync)
        {
            _requests.Remove(request);
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


    private sealed class ApiRequestLease(ResetMaintenanceState owner, ActiveRequest request) : IDisposable
    {
        private ResetMaintenanceState? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is null) return;
            current.EndApiRequest(request);
            request.Dispose();
        }
    }

    private sealed class ActiveRequest(CancellationTokenSource? cancellation, Action? abort) : IDisposable
    {
        public void CancelAndAbort()
        {
            try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            try { abort?.Invoke(); } catch { }
        }

        public void Dispose() => cancellation?.Dispose();
    }
}
