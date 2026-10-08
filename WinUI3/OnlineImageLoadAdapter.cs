namespace ModFolderCopier.WinUI;

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
