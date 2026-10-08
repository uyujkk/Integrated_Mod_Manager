using ModFolderCopier.WinUI;
using Xunit;

namespace IntegratedModManager.Tests;

public sealed class OnlineImageLoadAdapterTests
{
    private static readonly Uri CachedFile = new("G:\\cache\\image.jpg");

    [Fact]
    public async Task DecodesCachedFileInsteadOfReturningUriAsSuccess()
    {
        object expected = new();
        var refreshes = new List<bool>();
        object? actual = await OnlineImageLoadAdapter.LoadAsync("https://example.com/image.jpg",
            (_, refresh, _) => { refreshes.Add(refresh); return Task.FromResult<Uri?>(CachedFile); },
            (uri, _) => { Assert.True(uri.IsFile); return Task.FromResult(expected); },
            _ => Assert.Fail("Valid cache must not fail."));
        Assert.Same(expected, actual);
        Assert.Equal([false], refreshes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/image.jpg")]
    [InlineData("ms-appx:///image.jpg")]
    public async Task FailedDownloadCannotTriggerOpaqueUriLoad(string? source)
    {
        object? result = await OnlineImageLoadAdapter.LoadAsync<object>("url",
            (_, _, _) => Task.FromResult(source is null ? null : new Uri(source)),
            (_, _) => throw new Exception("Decoder must not run."),
            _ => Assert.Fail("Missing downloads are not decode failures."));
        Assert.Null(result);
    }

    [Fact]
    public async Task UnreadableCacheIsRedownloadedOnlyOnce()
    {
        var refreshes = new List<bool>();
        var errors = new List<Exception>();
        int calls = 0;
        object expected = new();
        object? result = await OnlineImageLoadAdapter.LoadAsync("url",
            (_, refresh, _) => { refreshes.Add(refresh); return Task.FromResult<Uri?>(CachedFile); },
            (_, _) => ++calls == 1 ? throw new InvalidDataException("Corrupt cache") : Task.FromResult(expected),
            errors.Add);
        Assert.Same(expected, result);
        Assert.Equal([false, true], refreshes);
        Assert.Single(errors);
    }

    [Fact]
    public async Task UnsupportedImageStopsAfterTwoAttemptsAndReportsBothFailures()
    {
        var errors = new List<Exception>();
        object? result = await OnlineImageLoadAdapter.LoadAsync<object>("url",
            (_, _, _) => Task.FromResult<Uri?>(CachedFile),
            (_, _) => throw new InvalidDataException("Unsupported format"), errors.Add);
        Assert.Null(result);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public async Task RefreshFailureDoesNotPublishUnreadableOldImage()
    {
        object? result = await OnlineImageLoadAdapter.LoadAsync<object>("url",
            (_, refresh, _) => Task.FromResult(refresh ? null : CachedFile),
            (_, _) => throw new IOException("Cache read failed"), _ => { });
        Assert.Null(result);
    }

    [Fact]
    public async Task CacheProviderFailureIsBoundedAndReported()
    {
        var errors = new List<Exception>();
        object? result = await OnlineImageLoadAdapter.LoadAsync<object>("url",
            (_, _, _) => throw new IOException("Cache inaccessible"),
            (_, _) => throw new Exception("Decoder must not run"), errors.Add);
        Assert.Null(result);
        Assert.Equal(2, errors.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationNeverPublishesImageOrRetries(int cancelAt)
    {
        using var cancel = new CancellationTokenSource();
        int resolutions = 0;
        if (cancelAt == 0) cancel.Cancel();
        object? result = await OnlineImageLoadAdapter.LoadAsync("url",
            (_, _, _) =>
            {
                resolutions++;
                if (cancelAt == 1) cancel.Cancel();
                return Task.FromResult<Uri?>(CachedFile);
            },
            (_, _) => { cancel.Cancel(); return Task.FromResult(new object()); },
            _ => Assert.Fail("Cancellation is not a load failure"), cancel.Token);
        Assert.Null(result);
        Assert.Equal(cancelAt == 0 ? 0 : 1, resolutions);
    }

    [Fact]
    public async Task LongUnicodeFilePathIsPassedToStreamDecoderUnchanged()
    {
        string path = "G:\\图片缓存\\" + new string('a', 280) + "\\角色.png";
        var source = new Uri(path);
        await OnlineImageLoadAdapter.LoadAsync("url",
            (_, _, _) => Task.FromResult<Uri?>(source),
            (uri, _) => { Assert.Equal(path, uri.LocalPath); return Task.FromResult(new object()); },
            _ => Assert.Fail("Path should not be rewritten"));
    }
}
