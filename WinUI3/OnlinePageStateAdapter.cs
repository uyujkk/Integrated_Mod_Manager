using IntegratedModManager.Core;

namespace ModFolderCopier.WinUI;

// No XAML dependency: the production window supplies callbacks, and tests compile this exact
// adapter source with controlled asynchronous callbacks. Calls belong to the UI thread.
internal sealed class OnlinePageStateAdapter
{
    private long _revision;
    private CancellationTokenSource? _current;
    public bool IsLoading { get; private set; }

    public void Invalidate()
    {
        _revision++;
        CancellationTokenSource? previous = _current;
        _current = null;
        IsLoading = false;
        previous?.Cancel(); // The request's finally owns disposal, even if its worker ignores cancellation.
    }

    public async Task RunLatestAsync<T>(
        Func<Task> open,
        Func<CancellationToken, Task<T>> load,
        Func<T, CancellationToken, Task<T>> project,
        Action<T, T> publish,
        Func<Exception, Task> reportError,
        Func<bool> contextIsCurrent,
        Action<bool> loadingChanged)
    {
        Invalidate();
        long revision = _revision;
        using var cancellation = new CancellationTokenSource();
        _current = cancellation;
        IsLoading = true;
        bool IsCurrent() => revision == _revision && !cancellation.IsCancellationRequested && contextIsCurrent();
        try
        {
            loadingChanged(true);
            await open();
            if (!IsCurrent()) return;
            T raw = await load(cancellation.Token);
            if (!IsCurrent()) return;
            T display = await project(raw, cancellation.Token);
            if (!IsCurrent()) return;
            publish(raw, display);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrent()) await reportError(ex);
        }
        finally
        {
            if (revision == _revision)
            {
                _current = null;
                IsLoading = false;
                // Context changes may occur without an explicit reset. Never touch another page.
                if (contextIsCurrent()) loadingChanged(false);
            }
        }
    }
}

// One picker/preflight at a time, but different installations may continue after task creation.
// A same-source attempt in the same captured repository stays locked until it finishes.
internal sealed class OnlineDownloadActionAdapter
{
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private Lease? _preparing;
    public bool IsPreparing => _preparing is not null;

    public Lease? TryBegin(OnlineDownloadContext context, string sourceIdentity)
    {
        string key = Key(context, sourceIdentity);
        if (IsPreparing || !_active.Add(key)) return null;
        var lease = new Lease(this, key);
        _preparing = lease;
        return lease;
    }

    public bool IsActive(OnlineDownloadContext context, string sourceIdentity)
        => _active.Contains(Key(context, sourceIdentity));

    private static string Key(OnlineDownloadContext context, string sourceIdentity)
        => System.Text.Json.JsonSerializer.Serialize(new[]
        {
            context.RepositoryId, context.RepositoryPath.TrimEnd('\\', '/'), sourceIdentity
        });

    internal sealed class Lease : IDisposable
    {
        private OnlineDownloadActionAdapter? _owner;
        private readonly string _key;
        private readonly CancellationTokenSource _cancellation = new();
        public CancellationToken Token => _cancellation.Token;
        internal Lease(OnlineDownloadActionAdapter owner, string key) { _owner = owner; _key = key; }
        public void MarkTaskStarted()
        {
            if (_owner is null) throw new ObjectDisposedException(nameof(Lease));
            Token.ThrowIfCancellationRequested();
            if (ReferenceEquals(_owner._preparing, this)) _owner._preparing = null;
        }
        public void Dispose()
        {
            if (_owner is null) return;
            _owner._active.Remove(_key);
            if (ReferenceEquals(_owner._preparing, this)) _owner._preparing = null;
            _owner = null;
            _cancellation.Dispose();
        }
        internal void Cancel() => _cancellation.Cancel();
    }

    public void CancelPreparation() => _preparing?.Cancel();
}
