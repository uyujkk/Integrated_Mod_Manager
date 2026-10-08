using System.Diagnostics;
using IntegratedModManager.Core;
using Xunit.Abstractions;

namespace IntegratedModManager.Core.Tests;

public sealed class RepositoryScanServiceTests(ITestOutputHelper output)
{
    [Fact]
    public void ScanPreservesTwoLevelConventionSortingAndDisabledFolders()
    {
        using var fixture = new Fixture();
        fixture.AddMod("z-category", "z-mod", "b.ini", "a.dds");
        string mod = fixture.AddMod("a-category", "DISABLED a-mod", "config.ini");
        Directory.CreateDirectory(Path.Combine(mod, "nested"));
        File.WriteAllText(Path.Combine(mod, "nested", "not-an-immediate-file.ini"), "nested");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "empty"));
        var scan = fixture.Scan();
        Assert.True(scan.IsComplete);
        Assert.Empty(scan.Warnings);
        Assert.Equal(2, scan.ModCount);
        Assert.Equal(["a-category", "empty", "z-category"], scan.Categories.Select(c => Path.GetFileName(c.Path)));
        Assert.Equal("DISABLED a-mod", Path.GetFileName(scan.Categories[0].Mods[0].ModPath));
        Assert.Single(scan.Categories[0].Mods[0].Files);
        Assert.Equal(["a.dds", "b.ini"], scan.Categories[2].Mods[0].Files.Select(Path.GetFileName));
        Assert.Equal(8, scan.Categories[2].Mods[0].TotalBytes);
        Assert.Equal("test", File.ReadAllText(Path.Combine(mod, "config.ini")));
        Assert.Equal(1, fixture.Store.ReplaceCount);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void LargeRepositoryWarmScanReusesEveryFileList(int modCount)
    {
        using var fixture = new Fixture();
        for (int i = 0; i < modCount; i++)
            fixture.AddMod($"Character-{i % 10:D2}", $"Mod-{i:D4}", "mod.ini", "preview.png");
        var clock = Stopwatch.StartNew();
        var cold = fixture.Scan();
        long coldMs = clock.ElapsedMilliseconds;
        Assert.Equal(modCount, cold.ModCount);
        Assert.Equal(0, cold.CacheHits);
        Assert.Equal(modCount, fixture.Files.FileListReads);
        fixture.Files.FileListReads = 0;
        fixture.Files.FileSizeReads = 0;
        clock.Restart();
        var warm = fixture.Scan();
        output.WriteLine($"Synthetic {modCount} mods: cold={coldMs}ms, warm={clock.ElapsedMilliseconds}ms; "
            + $"warm file-list reads={fixture.Files.FileListReads}, size reads={fixture.Files.FileSizeReads}.");
        Assert.Equal(modCount, warm.ModCount);
        Assert.Equal(modCount, warm.CacheHits);
        Assert.Equal(0, fixture.Files.FileListReads);
        Assert.Equal(0, fixture.Files.FileSizeReads);
        Assert.Equal(1, fixture.Store.ReplaceCount);
        Assert.True(warm.IsComplete);
    }

    [Fact]
    public void ChangedStampRefreshesFilesAndBytes()
    {
        using var fixture = new Fixture();
        string mod = fixture.AddMod("character", "mod", "a.ini");
        fixture.Scan();
        File.WriteAllText(Path.Combine(mod, "b.ini"), "123456");
        Directory.SetLastWriteTimeUtc(mod, DateTime.UtcNow.AddMinutes(1));
        var scan = fixture.Scan();
        var entry = Assert.Single(Assert.Single(scan.Categories).Mods);
        Assert.Equal(2, entry.FileCount);
        Assert.Equal(10, entry.TotalBytes);
        Assert.Equal(0, scan.CacheHits);
        Assert.Equal(2, fixture.Store.ReplaceCount);
    }

    [Fact]
    public void RenameWithSameCountAndRemovalReplaceStaleIndex()
    {
        using var fixture = new Fixture();
        string mod = fixture.AddMod("character", "old", "a.ini");
        fixture.Scan();
        string renamed = Path.Combine(Path.GetDirectoryName(mod)!, "new");
        Directory.Move(mod, renamed);
        fixture.Scan();
        Assert.Equal(renamed, Assert.Single(fixture.Store.Entries).ModPath);
        Directory.Delete(renamed, true);
        Assert.Equal(0, fixture.Scan().ModCount);
        Assert.Empty(fixture.Store.Entries);
        Assert.Equal(3, fixture.Store.ReplaceCount);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("negative-bytes")]
    [InlineData("foreign-file")]
    [InlineData("relative-file")]
    [InlineData("wrong-category")]
    [InlineData("duplicate-files")]
    public void CorruptCacheIsRebuiltIncludingByteCount(string corruption)
    {
        using var fixture = new Fixture();
        string mod = fixture.AddMod("character", "mod", "a.ini");
        fixture.Scan();
        var entry = fixture.Store.Entries[0];
        fixture.Store.Entries = [corruption switch
        {
            "count" => entry with { FileCount = 999, TotalBytes = 999 },
            "negative-bytes" => entry with { TotalBytes = -1 },
            "foreign-file" => entry with { Files = [Path.Combine(fixture.Root, "foreign.ini")] },
            "relative-file" => entry with { Files = ["a.ini"] },
            "wrong-category" => entry with { FirstLevelPath = fixture.Root },
            _ => entry with { Files = [entry.Files[0], entry.Files[0]], FileCount = 2 }
        }];
        var scan = fixture.Scan();
        var refreshed = Assert.Single(Assert.Single(scan.Categories).Mods);
        Assert.Equal(0, scan.CacheHits);
        Assert.Equal(4, refreshed.TotalBytes);
        Assert.Equal(Path.Combine(mod, "a.ini"), Assert.Single(refreshed.Files));
    }

    [Fact]
    public void DuplicateCacheRowsDoNotCrashAndAreReplaced()
    {
        using var fixture = new Fixture();
        fixture.AddMod("character", "mod", "a.ini");
        fixture.Scan();
        fixture.Store.Entries = [fixture.Store.Entries[0], fixture.Store.Entries[0]];
        var scan = fixture.Scan();
        Assert.Equal(1, scan.ModCount);
        Assert.Equal(1, scan.CacheHits);
        Assert.Single(fixture.Store.Entries);
        Assert.Equal(2, fixture.Store.ReplaceCount);
    }

    [Theory]
    [InlineData("directories")]
    [InlineData("files")]
    [InlineData("stamp")]
    [InlineData("size")]
    public void UnreadableChildDoesNotAbortSiblingsOrReplaceCompleteCache(string operation)
    {
        using var fixture = new Fixture();
        string broken = fixture.AddMod("a-broken", "mod", "a.ini");
        fixture.AddMod("z-readable", "mod", "a.ini");
        fixture.Scan();
        fixture.Files.Fail = (op, path) => op == operation && path.Contains("a-broken", StringComparison.Ordinal);
        // Invalidate the broken row so file/size failure is exercised too.
        Directory.SetLastWriteTimeUtc(broken, DateTime.UtcNow.AddMinutes(2));
        var scan = fixture.Scan();
        Assert.False(scan.IsComplete);
        Assert.Single(scan.Warnings);
        Assert.Single(scan.Categories.Single(c => c.Path.EndsWith("z-readable")).Mods);
        Assert.Equal(1, fixture.Store.ReplaceCount);
        Assert.Equal(2, fixture.Store.Entries.Count);
    }

    [Fact]
    public void MissingRootIsAnErrorNotAnEmptyIndex()
    {
        using var fixture = new Fixture();
        fixture.AddMod("character", "mod", "a.ini");
        fixture.Scan();
        Assert.Throws<DirectoryNotFoundException>(() => fixture.Service.Scan("repo", Path.Combine(fixture.Root, "missing")));
        Assert.Equal(1, fixture.Store.ReplaceCount);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public void CacheFailureStillReturnsLiveFolders(string operation)
    {
        using var fixture = new Fixture();
        fixture.AddMod("character", "mod", "a.ini");
        fixture.Store.Fail = operation;
        var scan = fixture.Scan();
        Assert.Equal(1, scan.ModCount);
        Assert.True(scan.IsComplete);
        Assert.Single(scan.Warnings);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("directories")]
    [InlineData("stamp")]
    [InlineData("files")]
    [InlineData("size")]
    public void CancelledScanNeverPublishesPartialIndex(string stage)
    {
        using var fixture = new Fixture();
        fixture.AddMod("character", "mod", "a.ini");
        using var cancellation = new CancellationTokenSource();
        if (stage == "before") cancellation.Cancel();
        else fixture.Files.Before = (operation, _) => { if (operation == stage) cancellation.Cancel(); };
        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Service.Scan("repo", fixture.Root, cancellation.Token));
        Assert.Equal(0, fixture.Store.ReplaceCount);
    }

    [Theory]
    [InlineData("", "root")]
    [InlineData(" ", "root")]
    [InlineData("repo", "")]
    public void InvalidRequestsFailBeforeReadingFiles(string repository, string root)
    {
        using var fixture = new Fixture();
        Assert.ThrowsAny<ArgumentException>(() => fixture.Service.Scan(repository, root));
        Assert.Equal(0, fixture.Files.FileListReads);
    }

    [Fact]
    public void EmptyRepositoryAndEmptyModAreRepresented()
    {
        using var fixture = new Fixture();
        Assert.Empty(fixture.Scan().Categories);
        fixture.AddMod("character", "empty");
        var entry = Assert.Single(Assert.Single(fixture.Scan().Categories).Mods);
        Assert.Empty(entry.Files);
        Assert.Equal(0, entry.TotalBytes);
        Assert.Equal(1, fixture.Scan().CacheHits);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "imm-repository-scan", Guid.NewGuid().ToString("N"));
        public MemoryIndexStore Store { get; } = new();
        public TrackingFileSystem Files { get; } = new();
        public RepositoryScanService Service { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Service = new(Store, Files);
        }
        public string AddMod(string category, string mod, params string[] files)
        {
            string path = Path.Combine(Root, category, mod);
            Directory.CreateDirectory(path);
            foreach (string file in files) File.WriteAllText(Path.Combine(path, file), "test");
            return path;
        }
        public RepositoryScanResult Scan() => Service.Scan("repo", Root);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class MemoryIndexStore : IRepositoryIndexStore
    {
        public IReadOnlyList<RepositoryModIndexEntry> Entries { get; set; } = [];
        public int ReplaceCount { get; private set; }
        public string? Fail { get; set; }
        public IReadOnlyList<RepositoryModIndexEntry> Read(string repositoryId, string sourceRoot)
        {
            if (Fail == "read") throw new InvalidOperationException("Unavailable cache");
            return Entries;
        }
        public void Replace(string repositoryId, string sourceRoot, IReadOnlyList<RepositoryModIndexEntry> folders)
        {
            if (Fail == "write") throw new InvalidOperationException("Unavailable cache");
            ReplaceCount++;
            Entries = folders.ToArray();
        }
    }

    private sealed class TrackingFileSystem : IRepositoryScanFileSystem
    {
        public int FileListReads;
        public int FileSizeReads;
        public Func<string, string, bool>? Fail;
        public Action<string, string>? Before;
        private void Check(string operation, string path)
        {
            Before?.Invoke(operation, path);
            if (Fail?.Invoke(operation, path) == true) throw new UnauthorizedAccessException("Synthetic inaccessible child");
        }
        public string[] GetDirectories(string path) { Check("directories", path); return Directory.GetDirectories(path); }
        public string[] GetFiles(string path) { Check("files", path); FileListReads++; return Directory.GetFiles(path); }
        public DateTime GetLastWriteTimeUtc(string path) { Check("stamp", path); return Directory.GetLastWriteTimeUtc(path); }
        public long GetFileLength(string path) { Check("size", path); FileSizeReads++; return new FileInfo(path).Length; }
    }
}
