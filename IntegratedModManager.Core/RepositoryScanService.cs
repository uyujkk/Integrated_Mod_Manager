using System.Globalization;
using System.Security;

namespace IntegratedModManager.Core;

public sealed record RepositoryModIndexEntry(
    string FirstLevelPath, string ModPath, string FolderStampUtc,
    int FileCount, long TotalBytes, IReadOnlyList<string> Files);

public interface IRepositoryIndexStore
{
    IReadOnlyList<RepositoryModIndexEntry> Read(string repositoryId, string sourceRoot);
    void Replace(string repositoryId, string sourceRoot, IReadOnlyList<RepositoryModIndexEntry> folders);
}

public interface IRepositoryScanFileSystem
{
    string[] GetDirectories(string path);
    string[] GetFiles(string path);
    DateTime GetLastWriteTimeUtc(string path);
    long GetFileLength(string path);
}

public sealed class RepositoryScanFileSystem : IRepositoryScanFileSystem
{
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public string[] GetFiles(string path) => Directory.GetFiles(path);
    public DateTime GetLastWriteTimeUtc(string path) => Directory.GetLastWriteTimeUtc(path);
    public long GetFileLength(string path) => new FileInfo(path).Length;
}

public sealed record RepositoryScanCategory(string Path, IReadOnlyList<RepositoryModIndexEntry> Mods);
public sealed record RepositoryScanWarning(string Path, string Operation, Exception Error);
public sealed record RepositoryScanResult(
    IReadOnlyList<RepositoryScanCategory> Categories,
    IReadOnlyList<RepositoryScanWarning> Warnings,
    bool IsComplete, int CacheHits)
{
    public int ModCount => Categories.Sum(category => category.Mods.Count);
}

/// <summary>
/// Read-only, two-level character/Mod scan. Only the index store is written;
/// no deployment, configuration, INI, preview or WinUI objects are touched.
/// Directory-stamp caching preserves the existing immediate-file convention:
/// it is not a recursive integrity check or a file-content freshness guarantee.
/// </summary>
public sealed class RepositoryScanService(
    IRepositoryIndexStore indexStore, IRepositoryScanFileSystem? fileSystem = null)
{
    private readonly IRepositoryScanFileSystem _files = fileSystem ?? new RepositoryScanFileSystem();

    public RepositoryScanResult Scan(
        string repositoryId, string sourceRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        cancellationToken.ThrowIfCancellationRequested();

        // Root failure is not an empty repository. Let the caller retain its
        // previous display/index and present a localized refresh failure.
        string[] categories = _files.GetDirectories(sourceRoot);
        Array.Sort(categories, StringComparer.CurrentCultureIgnoreCase);
        List<RepositoryScanWarning> warnings = [];
        Dictionary<string, RepositoryModIndexEntry> cached = new(StringComparer.OrdinalIgnoreCase);
        bool indexChanged = false;
        try
        {
            foreach (RepositoryModIndexEntry folder in indexStore.Read(repositoryId, sourceRoot))
            {
                // A duplicate/corrupt cache row must not crash a live scan.
                if (!cached.TryAdd(folder.ModPath, folder)) indexChanged = true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cached.Clear();
            warnings.Add(new(sourceRoot, "Read repository index", ex));
        }

        indexChanged |= cached.Count == 0;
        List<RepositoryModIndexEntry> refreshedIndex = [];
        List<RepositoryScanCategory> result = [];
        bool complete = true;
        int hits = 0;
        foreach (string category in categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<RepositoryModIndexEntry> mods = [];
            try
            {
                string[] paths = _files.GetDirectories(category);
                Array.Sort(paths, StringComparer.CurrentCultureIgnoreCase);
                foreach (string path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        string stamp = _files.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
                        RepositoryModIndexEntry entry;
                        if (cached.TryGetValue(path, out RepositoryModIndexEntry? previous)
                            && IsReusable(previous, category, path, stamp))
                        {
                            entry = previous with { Files = previous.Files.ToArray() };
                            hits++;
                        }
                        else
                        {
                            string[] files = _files.GetFiles(path);
                            Array.Sort(files, StringComparer.CurrentCultureIgnoreCase);
                            long bytes = 0;
                            foreach (string file in files)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                try { bytes = checked(bytes + _files.GetFileLength(file)); }
                                catch (Exception ex) when (IsFileSystemFailure(ex))
                                {
                                    complete = false;
                                    warnings.Add(new(file, "Read Mod file size", ex));
                                }
                            }
                            entry = new(category, path, stamp, files.Length, bytes, files);
                            indexChanged = true;
                        }
                        mods.Add(entry);
                        refreshedIndex.Add(entry);
                    }
                    catch (Exception ex) when (IsFileSystemFailure(ex))
                    {
                        complete = false;
                        warnings.Add(new(path, "Scan Mod folder", ex));
                    }
                }
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                complete = false;
                warnings.Add(new(category, "Scan category folder", ex));
            }
            result.Add(new(category, mods.ToArray()));
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Never replace the durable index with partial/cancelled results.
        if (complete && (indexChanged || cached.Count != refreshedIndex.Count))
        {
            try { indexStore.Replace(repositoryId, sourceRoot, refreshedIndex); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add(new(sourceRoot, "Write repository index", ex));
            }
        }
        return new(result.ToArray(), warnings.ToArray(), complete, hits);
    }

    private static bool IsReusable(RepositoryModIndexEntry entry, string category, string mod, string stamp)
    {
        if (!entry.FirstLevelPath.Equals(category, StringComparison.OrdinalIgnoreCase)
            || entry.FolderStampUtc != stamp || entry.TotalBytes < 0
            || entry.FileCount != entry.Files.Count) return false;
        try
        {
            // A persisted file list must not redirect preview reads outside
            // the immediate Mod directory, even when the stamp still matches.
            return entry.Files.All(file => Path.IsPathFullyQualified(file)
                && string.Equals(Path.GetDirectoryName(file), mod, StringComparison.OrdinalIgnoreCase))
                && entry.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() == entry.FileCount;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool IsFileSystemFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or SecurityException;
}
