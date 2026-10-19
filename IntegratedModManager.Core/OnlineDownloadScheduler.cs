namespace IntegratedModManager.Core;

/// <summary>A cancelable FIFO queue. Lowering the limit lets current work finish.</summary>
public sealed class OnlineDownloadScheduler
{
    private readonly object _sync = new();
    private readonly LinkedList<TaskCompletionSource<IDisposable>> _waiting = [];
    private int _active;
    private int _limit;
    public OnlineDownloadScheduler(int limit = 2) => Limit = limit;

    public int Limit
    {
        get { lock (_sync) return _limit; }
        set
        {
            if (value is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(value));
            lock (_sync) { _limit = value; Drain(); }
        }
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        LinkedListNode<TaskCompletionSource<IDisposable>> node;
        lock (_sync)
        {
            node = _waiting.AddLast(completion);
            Drain();
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            lock (_sync)
            {
                if (node.List is null) return; // An acquired lease belongs to the caller.
                _waiting.Remove(node);
                completion.TrySetCanceled(cancellationToken);
            }
        });
        IDisposable lease = await completion.Task.ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) { lease.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
        return lease;
    }

    private void Drain()
    {
        while (_active < _limit && _waiting.First is { } node)
        {
            _waiting.RemoveFirst();
            _active++;
            node.Value.SetResult(new Lease(this));
        }
    }

    private sealed class Lease(OnlineDownloadScheduler owner) : IDisposable
    {
        private OnlineDownloadScheduler? _owner = owner;
        public void Dispose()
        {
            OnlineDownloadScheduler? current = Interlocked.Exchange(ref _owner, null);
            if (current is null) return;
            lock (current._sync) { current._active--; current.Drain(); }
        }
    }
}
