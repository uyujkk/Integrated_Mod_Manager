namespace IntegratedModManager.Core;

public readonly record struct OnlineScanPage<T>(IReadOnlyList<T> Items, int TotalPages);

/// <summary>
/// Reads every page of a paged listing with bounded concurrency. Page 1 is read first to learn the
/// page count; the rest run at most <c>maxConcurrency</c> at a time. Items keep page order.
/// </summary>
public static class OnlinePageScanner
{
    public static async Task<IReadOnlyList<T>> ScanAsync<T>(
        Func<int, CancellationToken, Task<OnlineScanPage<T>>> fetchPage,
        int pageLimit, int maxConcurrency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetchPage);
        if (pageLimit < 1) throw new ArgumentOutOfRangeException(nameof(pageLimit));
        if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        OnlineScanPage<T> first = await fetchPage(1, cancellationToken).ConfigureAwait(false);
        int totalPages = Math.Clamp(first.TotalPages, 1, pageLimit);
        var pages = new IReadOnlyList<T>[totalPages];
        pages[0] = first.Items;
        if (totalPages > 1)
        {
            using var gate = new SemaphoreSlim(maxConcurrency);
            // A failed page cancels the others instead of letting them run to completion.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task[] tasks = Enumerable.Range(2, totalPages - 1).Select(async page =>
            {
                await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    pages[page - 1] = (await fetchPage(page, linked.Token).ConfigureAwait(false)).Items;
                }
                catch
                {
                    linked.Cancel();
                    throw;
                }
                finally { gate.Release(); }
            }).ToArray();
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Surface the original failure rather than the cancellation it caused.
                Exception? original = tasks.Where(task => task.IsFaulted).Select(task => task.Exception!.InnerException!).FirstOrDefault();
                if (original is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();
                throw;
            }
        }
        return pages.SelectMany(items => items).ToArray();
    }
}
