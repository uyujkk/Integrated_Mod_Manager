using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineTaskPackTests
{
    [Fact]
    public void LegacyMetadata_CanResolveAuthorIdentityWithoutGuessingTheName()
    {
        var metadata = GameBananaMetadataParser.ParseMod("""{"name":"Mod","userid":5878401,"Owner().name":"Author"}""", 42);
        Assert.Equal(5878401, metadata.AuthorId);
        Assert.Equal(0, GameBananaMetadataParser.ParseMod("""{"name":"Old cached mod"}""", 42).AuthorId);
    }
    [Fact]
    public void AuthorUrl_IsServerPaginatedAndCategoryScoped()
    {
        string url = OnlineBrowsePolicy.CategoryPageUrl("42770", 3, 40, 5878401);
        Assert.Contains("Generic_Category%5D=42770", url);
        Assert.Contains("Generic_Submitter%5D=5878401", url);
        Assert.EndsWith("_nPerpage=40&_nPage=3", url);
        Assert.DoesNotContain("Submitter", OnlineBrowsePolicy.CategoryPageUrl("42770", 1, 40));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("42770&other=1")]
    [InlineData("")]
    public void AuthorUrl_RejectsInvalidCategoryIds(string category)
        => Assert.Throws<ArgumentException>(() => OnlineBrowsePolicy.CategoryPageUrl(category, 1, 40));

    [Theory]
    [InlineData("7", new string[] { }, OnlineDuplicateKind.None)]
    [InlineData("7", new[] { "7", "8" }, OnlineDuplicateKind.SameFile)]
    [InlineData("7", new[] { "8" }, OnlineDuplicateKind.OtherFile)]
    [InlineData("7", new[] { "" }, OnlineDuplicateKind.UnknownFile)]
    [InlineData("", new[] { "7" }, OnlineDuplicateKind.UnknownFile)]
    public void DuplicateGuidance_DistinguishesVariantsAndOlderRecords(string file, string[] installed, OnlineDuplicateKind expected)
        => Assert.Equal(expected, OnlineDuplicatePolicy.Classify(file, installed));

    [Fact]
    public async Task Queue_IsBoundedFifo_AndLeasesReleaseOnlyOnce()
    {
        var queue = new OnlineDownloadScheduler(1);
        IDisposable first = await queue.AcquireAsync(CancellationToken.None);
        Task<IDisposable> second = queue.AcquireAsync(CancellationToken.None);
        Task<IDisposable> third = queue.AcquireAsync(CancellationToken.None);
        Assert.False(second.IsCompleted); Assert.False(third.IsCompleted);
        first.Dispose();
        using IDisposable secondLease = await second.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        Assert.False(third.IsCompleted);
        secondLease.Dispose();
        using IDisposable thirdLease = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Queue_CancelingWaiterDoesNotConsumeCapacity()
    {
        var queue = new OnlineDownloadScheduler(1);
        using IDisposable first = await queue.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> canceled = queue.AcquireAsync(cancellation.Token);
        Task<IDisposable> next = queue.AcquireAsync(CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        first.Dispose();
        using IDisposable nextLease = await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Queue_LimitChangesRespectAlreadyRunningWork()
    {
        var queue = new OnlineDownloadScheduler(2);
        using IDisposable first = await queue.AcquireAsync(CancellationToken.None);
        using IDisposable second = await queue.AcquireAsync(CancellationToken.None);
        queue.Limit = 1;
        Task<IDisposable> third = queue.AcquireAsync(CancellationToken.None);
        first.Dispose(); Assert.False(third.IsCompleted);
        second.Dispose(); using IDisposable lease = await third.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Limit = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Limit = 5);
    }

    [Fact]
    public async Task Queue_IncreasingLimitStartsWaitingWork_AndPreCanceledCallIsRejected()
    {
        var queue = new OnlineDownloadScheduler(1);
        using IDisposable first = await queue.AcquireAsync(CancellationToken.None);
        Task<IDisposable> second = queue.AcquireAsync(CancellationToken.None);
        queue.Limit = 2;
        using IDisposable lease = await second.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.AcquireAsync(new CancellationToken(true)));
    }
}
