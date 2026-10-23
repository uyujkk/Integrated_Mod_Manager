using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlinePageScannerTests
{
    private static Func<int, CancellationToken, Task<OnlineScanPage<int>>> Pages(int total, Action<int>? onStart = null, int delayMs = 0)
        => async (page, token) =>
        {
            onStart?.Invoke(page);
            if (delayMs > 0) await Task.Delay(delayMs * (total - page + 1) % 23 + delayMs, token);
            return new OnlineScanPage<int>(new[] { page * 10, page * 10 + 1 }, total);
        };

    [Fact]
    public async Task KeepsPageOrderEvenWhenPagesFinishOutOfOrder()
    {
        var items = await OnlinePageScanner.ScanAsync(Pages(6, delayMs: 5), pageLimit: 80, maxConcurrency: 3);
        Assert.Equal(Enumerable.Range(1, 6).SelectMany(page => new[] { page * 10, page * 10 + 1 }), items);
    }

    [Fact]
    public async Task NeverExceedsTheConcurrencyLimit()
    {
        int active = 0, peak = 0;
        async Task<OnlineScanPage<int>> Fetch(int page, CancellationToken token)
        {
            int now = Interlocked.Increment(ref active);
            InterlockedMax(ref peak, now);
            await Task.Delay(20, token);
            Interlocked.Decrement(ref active);
            return new OnlineScanPage<int>(new[] { page }, 12);
        }
        await OnlinePageScanner.ScanAsync(Fetch, pageLimit: 80, maxConcurrency: 3);
        Assert.InRange(peak, 2, 3);
    }

    [Fact]
    public async Task StopsAtThePageLimitAndHandlesSinglePage()
    {
        var requested = new List<int>();
        var limited = await OnlinePageScanner.ScanAsync(Pages(50, page => { lock (requested) requested.Add(page); }), pageLimit: 4, maxConcurrency: 2);
        Assert.Equal(new[] { 1, 2, 3, 4 }, requested.OrderBy(page => page));
        Assert.Equal(8, limited.Count);
        Assert.Equal(new[] { 10, 11 }, await OnlinePageScanner.ScanAsync(Pages(1), pageLimit: 80, maxConcurrency: 3));
    }

    [Fact]
    public async Task AFailedPageSurfacesItsOwnErrorAndStopsTheRest()
    {
        int started = 0;
        async Task<OnlineScanPage<int>> Fetch(int page, CancellationToken token)
        {
            Interlocked.Increment(ref started);
            if (page == 2) throw new HttpRequestException("page 2 failed");
            await Task.Delay(200, token);
            return new OnlineScanPage<int>(new[] { page }, 20);
        }
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => OnlinePageScanner.ScanAsync(Fetch, pageLimit: 80, maxConcurrency: 2));
        Assert.Equal("page 2 failed", error.Message);
        Assert.True(started < 20);
    }

    [Fact]
    public async Task CallerCancellationIsHonoured()
    {
        using var cancellation = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OnlinePageScanner.ScanAsync<int>(async (page, token) =>
        {
            await Task.Delay(1000, token);
            return new OnlineScanPage<int>(Array.Empty<int>(), 5);
        }, pageLimit: 80, maxConcurrency: 3, cancellation.Token));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }
}
