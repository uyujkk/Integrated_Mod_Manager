using System.Text.Json;
using System.Text.Json.Serialization;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.Data;

namespace ModFolderCopier.WinUI;

// Listing the repository through the local index (RepositoryIndex in app-index.db) into the workspace's characters and
// Mods, and the opt-in repository performance log (stage J finish, step 8a: moved from the window unchanged).
internal sealed partial class WorkspaceService
{
    private AppDataStore? _dataStore;

    private static readonly object RepositoryPerformanceLogSync = new();

    // Explicit opt-in marker, read once. No settings/schema changes, no upload.
    public RepositoryPerformanceRecorder Performance { get; } = new(
        File.Exists(Path.Combine(AppContext.BaseDirectory, "repository-performance.enabled")), WriteRepositoryPerformance);

    private static void WriteRepositoryPerformance(RepositoryPerformanceSample sample)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "repository-performance.log");
        lock (RepositoryPerformanceLogSync)
        {
            if (File.Exists(file) && new FileInfo(file).Length >= 1024 * 1024) return;
            File.AppendAllText(file, JsonSerializer.Serialize(sample,
                new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }) + Environment.NewLine);
        }
    }

    private (List<FirstLevelFolderItem> Items, int SecondCount) LoadFolderItemsUsingIndex(
        string repositoryId,
        string sourceDir,
        string targetDir,
        CancellationToken cancellationToken,
        out int warningCount)
    {
        var scanner = new RepositoryScanService(new RepositoryIndexStoreAdapter(_dataStore!));
        RepositoryScanResult scan;
        using (var scanTiming = Performance.Measure(RepositoryPerformanceOperation.Scan))
        {
            scan = scanner.Scan(repositoryId, sourceDir, cancellationToken);
            scanTiming?.SetCounts(scan.ModCount, scan.CacheHits);
        }
        using var projectionTiming = Performance.Measure(RepositoryPerformanceOperation.Projection, scan.ModCount, scan.CacheHits);
        warningCount = scan.Warnings.Count;
        foreach (RepositoryScanWarning warning in scan.Warnings)
        {
            _log(warning.Operation, warning.Error);
        }
        List<FirstLevelFolderItem> loadedItems = [];
        foreach (RepositoryScanCategory category in scan.Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var firstItem = new FirstLevelFolderItem(category.Path);
            foreach (RepositoryModIndexEntry mod in category.Mods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                firstItem.Children.Add(new SecondLevelFolderItem(
                    mod.ModPath, mod.Files.ToList(), FolderCopyState(targetDir, mod.ModPath)));
            }
            loadedItems.Add(firstItem);
        }
        return (loadedItems, scan.ModCount);
    }
}
