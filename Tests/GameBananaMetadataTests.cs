using System.Net;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class GameBananaMetadataTests
{
    private const string Details = """{"description":"<p>Hello &amp; 世界</p>","text":"F8: Toggle hair<br>Second line","screenshots":[{"_sFile800":"large.jpg","_sFile530":"small.jpg"}],"Preview().sSubFeedImageUrl()":"https://example.invalid/preview.jpg"}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetailsParserPreservesTextAndScreenshotPriority(bool wrapped)
    {
        var details = GameBananaMetadataParser.ParseDetails(wrapped ? "{\"value\":" + Details + "}" : Details);
        Assert.Equal("Hello & 世界", details.Summary);
        Assert.Equal("F8: Toggle hair\nSecond line", details.Description);
        Assert.Equal(["https://example.invalid/preview.jpg", "https://images.gamebanana.com/img/ss/mods/large.jpg"], details.ImageUrls);
        Assert.True(details.IsValid);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"value\":null}")]
    [InlineData("{\"error\":\"rate limit\"}")]
    public void ParserRejectsNonMetadataRoots(string json)
    {
        Assert.Throws<InvalidDataException>(() => GameBananaMetadataParser.ParseDetails(json));
        Assert.Throws<InvalidDataException>(() => GameBananaMetadataParser.ParseMod(json, 42));
    }

    [Theory]
    [InlineData("{broken", true)]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    public void OptionalMalformedScreenshotsRetainDescription(string screenshots, bool encodeString)
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["text"] = "description", ["screenshots"] = encodeString ? screenshots : JsonSerializer.Deserialize<JsonElement>(screenshots)
        });
        var details = GameBananaMetadataParser.ParseDetails(json);
        Assert.Equal("description", details.Description);
        Assert.Empty(details.ImageUrls);
    }

    [Fact]
    public void ScreenshotsAcceptEncodedArraysAndHttpUrlsButNotUnsafePaths()
    {
        string screenshots = """[{"_sFile800":"https://example.invalid/full.jpg"},{"_sFile530":"中 文.png"},{"_sFile":"small.jpg"},{"_sFile":"small.jpg"},{"_sFile":"../escape.jpg"},{"_sFile":"javascript:alert(1)"},{"_sFile":".."},null]""";
        var details = GameBananaMetadataParser.ParseDetails(JsonSerializer.Serialize(new { text = "text", screenshots }));
        Assert.Equal(3, details.ImageUrls.Length);
        Assert.Contains("https://example.invalid/full.jpg", details.ImageUrls);
        Assert.Contains("%E4%B8%AD%20%E6%96%87.png", details.ImageUrls[1]);
    }

    [Theory]
    [InlineData("1,234", 1234)]
    [InlineData("2.5", 3)]
    [InlineData("-50", 0)]
    [InlineData("9999999999999999999999", int.MaxValue)]
    [InlineData("NaN", 0)]
    [InlineData("bad", 0)]
    public void NumbersAreTolerantBoundedAndNonNegative(string likes, int expected)
    {
        var mod = GameBananaMetadataParser.ParseMod(JsonSerializer.Serialize(new { name = "Name", likes }), 42);
        Assert.Equal(expected, mod.Likes);
    }

    [Fact]
    public void ModFilesUseExistingNewestArchivePolicyAndDownloadCountFallback()
    {
        var mod = GameBananaMetadataParser.ParseMod("""
            {"value":{"name":"测试","catid":"42770","likes":12,"views":34,"downloads":0,"mdate":100,
            "Updates().bSubmissionHasUpdates()":true,"Files().aFiles()":{
            "1":{"_sFile":"old.zip","_sDownloadUrl":"https://example.invalid/old","_tsDateAdded":1,"_nDownloadCount":20},
            "2":{"_sFile":"new.7z","_sDownloadUrl":"https://example.invalid/new","_tsDateAdded":2,"_nFilesize":"1,024","_nDownloadCount":40},
            "3":{"_sFile":"instructions.txt","_sDownloadUrl":"https://example.invalid/readme","_tsDateAdded":3},
            "4":{"_sFile":"archived.zip","_sDownloadUrl":"https://example.invalid/archive","_tsDateAdded":4,"_bIsArchived":true},
            "5":{"_sFile":"unsafe.zip","_sDownloadUrl":"file:///c:/test","_nDownloadCount":5},"bad":null}}}
            """, 42);
        Assert.Equal(42770, mod.CategoryId);
        Assert.Equal(65, mod.Downloads);
        Assert.True(mod.HasUpdates);
        Assert.Equal(4, mod.DownloadFiles.Count);
        OnlineDownloadCandidate selected = Assert.IsType<OnlineDownloadCandidate>(OnlineDownloadSelectionPolicy.SelectDefault(mod.DownloadFiles));
        Assert.Equal("new.7z", selected.FileName);
        Assert.Equal(1024, selected.FileSizeBytes);
        Assert.Equal("https://gamebanana.com/mods/42", mod.ProfileUrl);
    }

    [Fact]
    public void ArrayFilesAndInvalidDatesAreHandledWithoutGuessing()
    {
        var mod = GameBananaMetadataParser.ParseMod("""{"name":"x","Files().aFiles()":[{"_idRow":8,"_sFile":"a.cab","_sDownloadUrl":"http://example.invalid/a","_tsDateAdded":9223372036854775807,"_nFilesize":-20}]}""", 1);
        var file = Assert.Single(mod.DownloadFiles);
        Assert.Equal("8", file.FileId);
        Assert.Equal(DateTimeOffset.MinValue, file.AddedAt);
        Assert.Equal(0, file.FileSizeBytes);
        Assert.True(file.IsSupportedArchive);
    }

    [Theory]
    [InlineData("zip", true)] [InlineData("rar", true)] [InlineData("7z", true)]
    [InlineData("tar.gz", true)] [InlineData("zipx", true)] [InlineData("cab", true)]
    [InlineData("exe", false)] [InlineData("txt", false)]
    public void FileEligibilityMatchesSupportedExtensions(string extension, bool supported)
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = "x", ["Files().aFiles()"] = new[] { new { _sFile = "mod." + extension, _sDownloadUrl = "https://example.invalid/a" } }
        });
        Assert.Equal(supported, Assert.Single(GameBananaMetadataParser.ParseMod(json, 1).DownloadFiles).IsSupportedArchive);
    }

    [Fact]
    public async Task DetailsMemoryCacheIsFreshBoundedAndDoesNotShareMutableImages()
    {
        var clock = new Clock();
        using var handler = new Handler(_ => Task.FromResult(Reply(Details)));
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client, clock: clock, memoryCapacity: 1);
        GameBananaDetails first = await service.GetDetailsAsync(1);
        first.ImageUrls[0] = "changed";
        Assert.NotEqual("changed", (await service.GetDetailsAsync(1)).ImageUrls[0]);
        Assert.Equal(1, handler.Calls);
        clock.Now += GameBananaMetadataService.CacheLifetime + TimeSpan.FromSeconds(1);
        await service.GetDetailsAsync(1);
        Assert.Equal(2, handler.Calls);
        clock.Now += TimeSpan.FromSeconds(1);
        await service.GetDetailsAsync(2);
        await service.GetDetailsAsync(1);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task FileListAlwaysFetchesFresh_AndClientIsBorrowed()
    {
        using var handler = new Handler(_ => Task.FromResult(Reply("{\"name\":\"mod\"}")));
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client);
        await service.GetModAsync(1);
        await service.GetModAsync(1);
        Assert.Equal(2, handler.Calls);
        Assert.Contains("Files().aFiles()", Uri.UnescapeDataString(handler.Url!));
        using HttpResponseMessage stillUsable = await client.GetAsync("https://example.invalid/probe");
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public async Task InvalidIdDoesNotRequestHttpOrCache(int id)
    {
        using var handler = new Handler(_ => Task.FromResult(Reply(Details)));
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetDetailsAsync(id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetModAsync(id));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(4, true)] [InlineData(-1, true)]
    public async Task PersistentCacheMustBeFreshAndValid(int ageDays, bool expectNetwork)
    {
        var clock = new Clock();
        var cache = new Cache { Entry = new(new GameBananaDetails { Summary = "cached" }, clock.Now.AddDays(-ageDays)) };
        using var handler = new Handler(_ => Task.FromResult(Reply(Details)));
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client, cache, clock);
        GameBananaDetails value = await service.GetDetailsAsync(1);
        Assert.Equal(expectNetwork ? "Hello & 世界" : "cached", value.Summary);
        Assert.Equal(expectNetwork ? 1 : 0, handler.Calls);
        Assert.Equal(expectNetwork ? 1 : 0, cache.Writes);
    }

    [Fact]
    public async Task InvalidCacheAndCacheFailuresDoNotLoseFreshResults()
    {
        var clock = new Clock();
        var cache = new Cache { Entry = new(new GameBananaDetails { ImageUrls = ["file:///unsafe"] }, clock.Now), FailWrite = true };
        using var handler = new Handler(_ => Task.FromResult(Reply(Details)));
        using var client = new HttpClient(handler);
        var diagnostics = new List<string>();
        var service = new GameBananaMetadataService(client, cache, clock, diagnostics: (phase, _) => diagnostics.Add(phase));
        Assert.Equal("Hello & 世界", (await service.GetDetailsAsync(1)).Summary);
        cache.FailRead = true;
        await service.GetDetailsAsync(2);
        Assert.Contains("Metadata cache read", diagnostics);
        Assert.Contains("Metadata cache write", diagnostics);
    }

    [Theory]
    [InlineData("<html>bad</html>")]
    [InlineData("{invalid")]
    [InlineData("{\"error\":\"limit\"}")]
    public async Task FailedResponseCannotPoisonCache(string body)
    {
        var cache = new Cache();
        using var handler = new Handler(_ => Task.FromResult(Reply(body)));
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client, cache);
        await Assert.ThrowsAnyAsync<Exception>(() => service.GetDetailsAsync(1));
        await Assert.ThrowsAnyAsync<Exception>(() => service.GetDetailsAsync(1));
        Assert.Equal(0, cache.Writes);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ForcedRefreshBypassesMemoryAndDisk()
    {
        var cache = new Cache();
        using var handler = new Handler(_ => Task.FromResult(Reply(Details)));
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client, cache);
        await service.GetDetailsAsync(1);
        await service.GetDetailsAsync(1, forceRefresh: true);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, cache.Reads);
    }

    [Fact]
    public async Task CancellationBeforeRequestOrDuringHttpDoesNotCache()
    {
        var cache = new Cache();
        using var handler = new Handler(async token => { await Task.Delay(Timeout.Infinite, token); return Reply(Details); });
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client, cache);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDetailsAsync(1, canceled.Token));
        Assert.Equal(0, handler.Calls);
        using var during = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDetailsAsync(1, during.Token));
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task TimeoutIsDistinctFromUserCancellation()
    {
        using var handler = new Handler(async token => { await Task.Delay(Timeout.Infinite, token); return Reply(Details); });
        using var client = new HttpClient(handler);
        var service = new GameBananaMetadataService(client, requestTimeout: TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAsync<TimeoutException>(() => service.GetDetailsAsync(1));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DeclaredOrActualOversizeIsRejected(bool knownLength)
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = knownLength ? new StringContent(new string('x', 64)) : new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes(new string('x', 64)))) }));
        using var client = new HttpClient(handler);
        var cache = new Cache();
        var service = new GameBananaMetadataService(client, cache, maxBytes: 32);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetDetailsAsync(1));
        Assert.Equal(0, cache.Writes);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)] [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task HttpErrorsAreNotCached(HttpStatusCode code)
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(code)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => new GameBananaMetadataService(client).GetDetailsAsync(1));
    }

    [Fact]
    public async Task BoundedReaderAcceptsBomAndExactLimit_RejectsInvalidUtf8()
    {
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("{}" )).ToArray();
        using var stream = new MemoryStream(bytes);
        Assert.Equal("{}", await GameBananaMetadataService.ReadBoundedJsonAsync(stream, bytes.Length, default));
        using var invalid = new MemoryStream([0xFF]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => GameBananaMetadataService.ReadBoundedJsonAsync(invalid, 1, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => GameBananaMetadataService.ReadBoundedJsonAsync(stream, 0, default));
    }

    [Fact]
    public async Task CancelDuringBodyReadDoesNotPublishCache()
    {
        var started = new TaskCompletionSource();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new BlockingReadStream(started)) }));
        using var client = new HttpClient(handler);
        var cache = new Cache();
        using var cancellation = new CancellationTokenSource();
        var service = new GameBananaMetadataService(client, cache);
        Task<GameBananaDetails> work = service.GetDetailsAsync(1, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task BrokenBodyDoesNotPublishCache()
    {
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new BrokenReadStream()) }));
        using var client = new HttpClient(handler);
        var cache = new Cache();
        await Assert.ThrowsAsync<IOException>(() => new GameBananaMetadataService(client, cache).GetDetailsAsync(1));
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task CanceledCacheWriteDoesNotPopulateMemory()
    {
        using var handler = new Handler(_ => Task.FromResult(Reply(Details)));
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var cache = new Cache { BeforeWrite = token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); } };
        var service = new GameBananaMetadataService(client, cache);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDetailsAsync(1, cancellation.Token));
        cache.BeforeWrite = null;
        await service.GetDetailsAsync(1);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task PreCanceledCardDoesNotReachHandler()
    {
        using var handler = new Handler(_ => Task.FromResult(Reply(Details)));
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GameBananaMetadataService(client).GetModAsync(1, cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(16 * 1024 * 1024 + 1)]
    public void InvalidSizeLimitRejected(int limit)
    {
        using var client = new HttpClient();
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameBananaMetadataService(client, maxBytes: limit));
    }

    [Fact]
    public void InvalidTimeoutOrCapacityRejected()
    {
        using var client = new HttpClient();
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameBananaMetadataService(client, memoryCapacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameBananaMetadataService(client, requestTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameBananaMetadataService(client, requestTimeout: TimeSpan.FromMinutes(6)));
        Assert.Throws<ArgumentNullException>(() => new GameBananaMetadataService(null!));
    }

    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public int Calls; public string? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; Url = request.RequestUri!.ToString(); return reply(cancellationToken); }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Cache : IOnlineMetadataCache
    {
        public OnlineMetadataCacheEntry? Entry; public int Reads, Writes; public bool FailRead, FailWrite;
        public Action<CancellationToken>? BeforeWrite;
        public Task<OnlineMetadataCacheEntry?> ReadAsync(int id, CancellationToken token)
        { Reads++; if (FailRead) throw new IOException("fixture"); return Task.FromResult(Entry); }
        public Task WriteAsync(int id, OnlineMetadataCacheEntry entry, CancellationToken token)
        { Writes++; BeforeWrite?.Invoke(token); if (FailWrite) throw new IOException("fixture"); Entry = entry; return Task.CompletedTask; }
    }
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
    private sealed class BlockingReadStream(TaskCompletionSource started) : MemoryStream
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; }
    }
    private sealed class BrokenReadStream : MemoryStream
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => throw new IOException("fixture stream");
    }
}
