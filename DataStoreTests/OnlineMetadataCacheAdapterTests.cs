using System.Text.Json;
using IntegratedModManager.Core;
using ModFolderCopier.WinUI;
using Xunit;

namespace IntegratedModManager.DataStore.Tests;

public sealed class OnlineMetadataCacheAdapterTests
{
    [Fact]
    public async Task NewCacheRoundTripsAndDoesNotOverwriteLegacyCache()
    {
        using var fixture = new Fixture();
        string legacy = Path.Combine(fixture.Path, "mod-42.json");
        const string original = """{"Summary":"old","Description":"old","ImageUrls":[],"ShortcutBindings":[{"Shortcut":"F8","Action":"old"}]}""";
        File.WriteAllText(legacy, original);
        fixture.Store.WriteCache("online-details", "42", new GameBananaDetails { Summary = "old" });
        var entry = new OnlineMetadataCacheEntry(new GameBananaDetails { Summary = "新缓存", ImageUrls = ["https://example.invalid/large.jpg"] }, DateTimeOffset.UtcNow.AddMinutes(-1));
        await fixture.Adapter.WriteAsync(42, entry, default);
        OnlineMetadataCacheEntry result = Assert.IsType<OnlineMetadataCacheEntry>(await fixture.Adapter.ReadAsync(42, default));
        Assert.Equal("新缓存", result.Value.Summary);
        Assert.Equal(entry.CachedAtUtc, result.CachedAtUtc);
        Assert.Equal(original, File.ReadAllText(legacy));
        Assert.Equal("old", fixture.Store.TryReadCache<GameBananaDetails>("online-details", "42", null)!.Value.Summary);
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.download"));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task LegacyJsonShapeIsReadableWithExtraUiFields(bool sqlite)
    {
        using var fixture = new Fixture();
        var legacy = new { Summary = "legacy", Description = "description", ImageUrls = new[] { "https://example.invalid/a" },
            AccessRequirementSummary = "extra", ShortcutBindings = new[] { new { Shortcut = "F8", Action = "toggle" } } };
        if (sqlite) fixture.Store.WriteCache("online-details", "42", legacy);
        else File.WriteAllText(Path.Combine(fixture.Path, "mod-42.json"), JsonSerializer.Serialize(legacy));
        Assert.Equal("legacy", (await fixture.Adapter.ReadAsync(42, default))!.Value.Summary);
        Assert.False(File.Exists(Path.Combine(fixture.Path, "metadata-mod-42.json")));
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("{}")]
    [InlineData("{\"Summary\":null,\"Description\":\"\",\"ImageUrls\":[]}")]
    [InlineData("{\"Summary\":\"\",\"Description\":\"\",\"ImageUrls\":null}")]
    [InlineData("{\"Summary\":\"\",\"Description\":\"\",\"ImageUrls\":[\"file:///bad\"]}")]
    public async Task CorruptNewFileFallsBackToValidLegacy(string corrupt)
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Path, "metadata-mod-42.json"), corrupt);
        fixture.Store.WriteCache("online-details", "42", new GameBananaDetails { Summary = "fallback" });
        Assert.Equal("fallback", (await fixture.Adapter.ReadAsync(42, default))!.Value.Summary);
    }

    [Fact]
    public async Task ExpiredFilesAndSqliteEntriesAreNotReturned()
    {
        using var fixture = new Fixture();
        fixture.Store.WriteCache("online-details", "42", new GameBananaDetails { Summary = "expired" }, DateTimeOffset.UtcNow.AddDays(-4));
        string file = Path.Combine(fixture.Path, "mod-42.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new GameBananaDetails { Summary = "expired" }));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-4));
        Assert.Null(await fixture.Adapter.ReadAsync(42, default));
    }

    [Fact]
    public async Task FileCacheWorksWithoutAvailableSqlite()
    {
        using var fixture = new Fixture();
        var unavailable = new AppDataStore(Path.Combine(fixture.Path, "unused.db"));
        var adapter = new OnlineMetadataCacheAdapter(unavailable, fixture.Path);
        var stamp = DateTimeOffset.UtcNow.AddMinutes(-3);
        await adapter.WriteAsync(42, new(new GameBananaDetails { Summary = "file-only" }, stamp), default);
        var result = await adapter.ReadAsync(42, default);
        Assert.Equal("file-only", result!.Value.Summary);
        Assert.InRange(Math.Abs((result.CachedAtUtc - stamp).TotalSeconds), 0, 1);
    }

    [Fact]
    public async Task FileWriteFailureDoesNotBlockSqliteAndReportsIssue()
    {
        using var fixture = new Fixture();
        string blocked = Path.Combine(fixture.Path, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var issues = new List<string>();
        var adapter = new OnlineMetadataCacheAdapter(fixture.Store, blocked, (phase, _) => issues.Add(phase));
        await adapter.WriteAsync(42, new(new GameBananaDetails { Summary = "sqlite-only" }, DateTimeOffset.UtcNow), default);
        Assert.Equal("sqlite-only", (await adapter.ReadAsync(42, default))!.Value.Summary);
        Assert.Contains("Metadata file cache write", issues);
        Assert.Equal("not a directory", File.ReadAllText(blocked));
    }

    [Fact]
    public async Task PreCanceledOrInvalidWriteDoesNotTouchExistingFile()
    {
        using var fixture = new Fixture();
        string file = Path.Combine(fixture.Path, "metadata-mod-42.json");
        File.WriteAllText(file, "preserve");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Adapter.WriteAsync(42, new(new GameBananaDetails(), DateTimeOffset.UtcNow), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Adapter.ReadAsync(42, cancellation.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Adapter.WriteAsync(-1, new(new GameBananaDetails(), DateTimeOffset.UtcNow), default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Adapter.WriteAsync(42, new(new GameBananaDetails { ImageUrls = ["bad"] }, DateTimeOffset.UtcNow), default));
        Assert.Equal("preserve", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.download"));
    }

    [Fact]
    public async Task OversizeWriteIsRejectedWithoutReplacingExistingCache()
    {
        using var fixture = new Fixture();
        string file = Path.Combine(fixture.Path, "metadata-mod-42.json");
        File.WriteAllText(file, "preserve");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Adapter.WriteAsync(42,
            new(new GameBananaDetails { Description = new string('x', GameBananaMetadataService.DefaultMaxBytes + 1) }, DateTimeOffset.UtcNow), default));
        Assert.Equal("preserve", File.ReadAllText(file));
        Assert.Null(fixture.Store.TryReadCache<GameBananaDetails>("online-details-raw-v1", "42", null));
    }

    [Fact]
    public async Task FailedFilePublicationCleansOwnedPartialAndKeepsExistingFile()
    {
        using var fixture = new Fixture();
        string file = Path.Combine(fixture.Path, "metadata-mod-42.json");
        File.WriteAllText(file, "preserve");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await fixture.Adapter.WriteAsync(42, new(new GameBananaDetails { Summary = "new" }, DateTimeOffset.UtcNow), default);
            Assert.Empty(Directory.GetFiles(fixture.Path, "*.download"));
            Assert.Equal("new", fixture.Store.TryReadCache<GameBananaDetails>("online-details-raw-v1", "42", null)!.Value.Summary);
        }
        Assert.Equal("preserve", File.ReadAllText(file));
    }

    [Fact]
    public async Task ServiceAndRealSqliteReuseDetailsButKeepFileListLive()
    {
        using var fixture = new Fixture();
        using var handler = new MetadataHandler();
        using var client = new HttpClient(handler);
        var first = new GameBananaMetadataService(client, fixture.Adapter);
        Assert.Equal("description", (await first.GetDetailsAsync(42)).Description);
        var restartedService = new GameBananaMetadataService(client, fixture.Adapter);
        Assert.Equal("description", (await restartedService.GetDetailsAsync(42)).Description);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("version 2", (await restartedService.GetModAsync(42)).Title);
        Assert.Equal("version 3", (await restartedService.GetModAsync(42)).Title);
        Assert.Equal(3, handler.Calls);
    }

    private sealed class MetadataHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            bool files = Uri.UnescapeDataString(request.RequestUri!.Query).Contains("Files().aFiles()", StringComparison.Ordinal);
            string json = files ? JsonSerializer.Serialize(new { name = $"version {Calls}" }) : "{\"text\":\"description\"}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imm-metadata-cache-" + Guid.NewGuid().ToString("N"));
        public AppDataStore Store { get; }
        public OnlineMetadataCacheAdapter Adapter { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Path);
            Store = new AppDataStore(System.IO.Path.Combine(Path, "cache.db"));
            Store.Initialize();
            Adapter = new OnlineMetadataCacheAdapter(Store, Path);
        }
        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path, true);
        }
    }
}
