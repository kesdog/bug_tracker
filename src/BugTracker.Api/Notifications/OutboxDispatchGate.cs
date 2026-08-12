namespace BugTracker.Api.Notifications;

public sealed class OutboxDispatchGate
{
    private readonly object _sync = new();
    private bool _paused;
    private int _activeDispatches;
    private TaskCompletionSource _resumed = CompletedSource();
    private TaskCompletionSource _drained = CompletedSource();

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        while (true)
        {
            Task wait;
            lock (_sync)
            {
                if (!_paused)
                {
                    if (_activeDispatches++ == 0)
                    {
                        _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    return new DispatchLease(this);
                }
                wait = _resumed.Task;
            }
            await wait.WaitAsync(ct);
        }
    }

    public async Task<IDisposable> PauseAndDrainAsync(CancellationToken ct)
    {
        var pause = Pause();
        try
        {
            await WaitForDrainAsync(ct);
            return pause;
        }
        catch
        {
            pause.Dispose();
            throw;
        }
    }

    public IDisposable Pause()
    {
        lock (_sync)
        {
            if (_paused)
            {
                throw new InvalidOperationException("Outbox dispatch is already paused.");
            }
            _paused = true;
            _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new PauseLease(this);
        }
    }

    public Task WaitForDrainAsync(CancellationToken ct)
    {
        Task wait;
        lock (_sync)
        {
            wait = _activeDispatches == 0 ? Task.CompletedTask : _drained.Task;
        }
        return wait.WaitAsync(ct);
    }

    private void ExitDispatch()
    {
        lock (_sync)
        {
            if (--_activeDispatches == 0)
            {
                _drained.TrySetResult();
            }
        }
    }

    private void Resume()
    {
        lock (_sync)
        {
            _paused = false;
            _resumed.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed class DispatchLease(OutboxDispatchGate owner) : IDisposable
    {
        private OutboxDispatchGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitDispatch();
    }

    private sealed class PauseLease(OutboxDispatchGate owner) : IDisposable
    {
        private OutboxDispatchGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Resume();
    }
}
