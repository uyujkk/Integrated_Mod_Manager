namespace ModFolderCopier.WinUI;

// Hero and thumbnail controls often request the same URL in the same UI turn.
// Hold the same-image lease through decoding: publishing a second download while
// WinRT still reads the first file can fail with access denied during File.Move.
internal sealed class OnlineImageLoadCoordinator
{
    private sealed class Entry
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal int Users;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal async Task<T?> RunAsync<T>(string key, Func<Task<T?>> load, CancellationToken token = default) where T : class
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries.Add(key, entry = new Entry());
            entry.Users++;
        }
        bool acquired = false;
        try
        {
            await entry.Gate.WaitAsync(token);
            acquired = true;
            token.ThrowIfCancellationRequested();
            return await load();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        finally
        {
            if (acquired) entry.Gate.Release();
            lock (_entries)
            {
                if (--entry.Users == 0)
                {
                    _entries.Remove(key);
                    entry.Gate.Dispose();
                }
            }
        }
    }
}

// Decode downloaded files explicitly; a file URI assignment is not load success.
internal static class OnlineImageLoadAdapter
{
    internal static async Task<T?> LoadAsync<T>(string imageUrl,
        Func<string, bool, CancellationToken, Task<Uri?>> resolveCache,
        Func<Uri, CancellationToken, Task<T>> decodeFile,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken = default) where T : class
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Uri? source = await resolveCache(imageUrl, attempt > 0, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // Do not hand a failed download to an opaque second network stack.
                if (source is not { IsFile: true }) return null;
                T decoded = await decodeFile(source, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return decoded;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) { reportFailure(exception); }
        }
        return null;
    }
}
