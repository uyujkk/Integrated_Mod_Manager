using Microsoft.Data.Sqlite;
using ModFolderCopier.WinUI;
using Xunit;

namespace IntegratedModManager.DataStore.Tests;

public sealed class AppDataStoreTests
{
    [Fact]
    public void Initialize_CreatesHealthyDatabase()
    {
        using var directory = new TemporaryDirectory();
        string databasePath = Path.Combine(directory.Path, "nested", "app-index.db");
        var store = new AppDataStore(databasePath);

        store.Initialize();

        Assert.True(store.IsAvailable);
        Assert.True(File.Exists(databasePath));
        Assert.Contains("SQLite ok", store.GetHealthSummary(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cache_RoundTripsAndReportsExpiry()
    {
        using var directory = new TemporaryDirectory();
        AppDataStore store = CreateStore(directory);
        var payload = new CachePayload("cached", 42);
        DateTimeOffset cachedAt = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(2));

        store.WriteCache("online", "page-1", payload, cachedAt);

        Assert.Null(store.TryReadCache<CachePayload>("online", "page-1", TimeSpan.FromMinutes(30)));
        CachedValue<CachePayload>? expired = store.TryReadCache<CachePayload>(
            "online", "page-1", TimeSpan.FromMinutes(30), allowExpired: true);
        Assert.NotNull(expired);
        Assert.True(expired.IsExpired);
        Assert.Equal(payload, expired.Value);
    }

    [Fact]
    public void Cache_UpsertReplacesExistingValue()
    {
        using var directory = new TemporaryDirectory();
        AppDataStore store = CreateStore(directory);

        store.WriteCache("details", "mod-7", new CachePayload("old", 1));
        store.WriteCache("details", "mod-7", new CachePayload("new", 2));

        CachedValue<CachePayload>? result = store.TryReadCache<CachePayload>("details", "mod-7", null);
        Assert.NotNull(result);
        Assert.Equal(new CachePayload("new", 2), result.Value);
    }

    [Fact]
    public void Favorites_AreCatalogScopedAndCanBeRemoved()
    {
        using var directory = new TemporaryDirectory();
        AppDataStore store = CreateStore(directory);

        store.SetFavorite("endfield", "liino", true);
        store.SetFavorite("genshin", "liino", true);
        store.SetFavorite("endfield", "liino", false);

        HashSet<string> favorites = store.ReadFavorites();
        Assert.DoesNotContain(AppDataStore.BuildFavoriteKey("endfield", "liino"), favorites);
        Assert.Contains(AppDataStore.BuildFavoriteKey("genshin", "liino"), favorites);
    }

    [Fact]
    public void ReplaceModIndex_IsAtomicAndRepositoryScoped()
    {
        using var directory = new TemporaryDirectory();
        AppDataStore store = CreateStore(directory);
        IndexedModFolder first = CreateFolder("characters", "mod-a", 2, 200);
        IndexedModFolder second = CreateFolder("weapons", "mod-b", 3, 300);
        IndexedModFolder otherRepository = CreateFolder("other", "mod-c", 4, 400);

        store.ReplaceModIndex("repo-a", "D:\\mods-a", [first, second]);
        store.ReplaceModIndex("repo-b", "D:\\mods-b", [otherRepository]);
        store.ReplaceModIndex("repo-a", "D:\\mods-a", [second]);

        List<IndexedModFolder> repoA = store.ReadModIndex("repo-a", "D:\\mods-a");
        List<IndexedModFolder> repoB = store.ReadModIndex("repo-b", "D:\\mods-b");
        Assert.Single(repoA);
        Assert.Equal("mod-b", repoA[0].ModPath);
        Assert.Equal(["a.ini", "b.dds"], repoA[0].Files);
        Assert.Single(repoB);
        Assert.Equal("mod-c", repoB[0].ModPath);
    }

    [Fact]
    public void ReadModIndex_DoesNotReturnRowsForAnotherSourceRoot()
    {
        using var directory = new TemporaryDirectory();
        AppDataStore store = CreateStore(directory);
        store.ReplaceModIndex("repo", "D:\\source", [CreateFolder("characters", "mod", 1, 10)]);

        List<IndexedModFolder> result = store.ReadModIndex("repo", "D:\\different");

        Assert.Empty(result);
    }

    [Fact]
    public void UninitializedStore_UsesSafeFallbacks()
    {
        using var directory = new TemporaryDirectory();
        var store = new AppDataStore(Path.Combine(directory.Path, "unused.db"));

        store.WriteCache("kind", "key", new CachePayload("value", 1));
        store.SetFavorite("catalog", "character", true);
        store.ReplaceModIndex("repo", "root", [CreateFolder("first", "mod", 1, 1)]);

        Assert.Null(store.TryReadCache<CachePayload>("kind", "key", null));
        Assert.Empty(store.ReadFavorites());
        Assert.Empty(store.ReadModIndex("repo", "root"));
        Assert.Equal("SQLite unavailable", store.GetHealthSummary());
    }

    private static AppDataStore CreateStore(TemporaryDirectory directory)
    {
        var store = new AppDataStore(Path.Combine(directory.Path, "app-index.db"));
        store.Initialize();
        Assert.True(store.IsAvailable);
        return store;
    }

    private static IndexedModFolder CreateFolder(string firstLevel, string modPath, int fileCount, long totalBytes)
    {
        return new IndexedModFolder
        {
            FirstLevelPath = firstLevel,
            ModPath = modPath,
            FolderStampUtc = "2026-08-22T12:00:00Z",
            FileCount = fileCount,
            TotalBytes = totalBytes,
            Files = ["a.ini", "b.dds"]
        };
    }

    private sealed record CachePayload(string Name, int Count);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "imm-datastore-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
