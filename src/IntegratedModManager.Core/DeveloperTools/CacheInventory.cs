using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.DeveloperTools;

public enum CacheKind
{
    /// <summary>Online data the app downloads again when needed; can be cleared while the app is closed.</summary>
    Regenerable,
    /// <summary>The GameBanana page's WebView2 profile (the sign-in); cleared like the app's Settings card does.</summary>
    GameBananaProfile,
    /// <summary>Kept: holds user data (such as favorites in the index database) or tools; shown, never cleared here.</summary>
    Kept
}

public sealed record CacheEntry(string Key, string Path, CacheKind Kind, bool Exists, bool IsLink, long Bytes, int Files);

public enum CacheClearResult { Cleared, NothingToClear, AppRunning, NotClearable, Link }

/// <summary>
/// What is in the app's <c>cache</c> folder and how big it is, and clearing what may be cleared. Clearing never
/// follows a link (a linked cache folder or entry is refused), refuses while the app runs, and only touches the
/// folders listed as regenerable or the GameBanana profile, which goes through <see cref="GameBananaWeb.DeleteProfile"/>.
/// </summary>
public static class CacheInventory
{
    private static readonly (string Key, CacheKind Kind)[] KnownEntries =
    [
        ("online-pages", CacheKind.Regenerable),
        ("online-details", CacheKind.Regenerable),
        ("online-images", CacheKind.Regenerable),
        ("online-characters", CacheKind.Regenerable),
        ("online-categories", CacheKind.Regenerable),
        ("mod-updates", CacheKind.Regenerable),
        (GameBananaWeb.ProfileFolderName, CacheKind.GameBananaProfile),
        ("app-index.db", CacheKind.Kept),
        ("optional-tools", CacheKind.Kept)
    ];

    public static IReadOnlyList<CacheEntry> Read(string appFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appFolder);
        string cache = Path.Combine(appFolder, "cache");
        var entries = new List<CacheEntry>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, CacheKind kind) in KnownEntries)
        {
            known.Add(key);
            entries.Add(Measure(key, Path.Combine(cache, key), kind));
        }
        // Anything else in the cache folder is listed as kept, so nothing unknown is ever cleared.
        if (Directory.Exists(cache) && !IsLink(cache))
        {
            foreach (string path in SafeChildren(cache))
            {
                string name = Path.GetFileName(path);
                if (known.Contains(name) || name.StartsWith("app-index.db", StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(Measure(name, path, CacheKind.Kept));
            }
        }
        return entries;
    }

    public static CacheClearResult Clear(string appFolder, string key, bool appRunning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appFolder);
        if (appRunning) return CacheClearResult.AppRunning;
        CacheKind? kind = KnownEntries.Where(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (CacheKind?)entry.Kind).FirstOrDefault();
        if (kind is null or CacheKind.Kept) return CacheClearResult.NotClearable;

        string cache = Path.GetFullPath(Path.Combine(appFolder, "cache"));
        if (Directory.Exists(cache) && IsLink(cache)) return CacheClearResult.Link;
        if (kind == CacheKind.GameBananaProfile)
            return GameBananaWeb.DeleteProfile(appFolder) ? CacheClearResult.Cleared : CacheClearResult.NothingToClear;

        string target = PathSafety.ResolveInsideDirectory(cache, key);
        if (File.Exists(target)) return CacheClearResult.NotClearable;
        if (!Directory.Exists(target)) return CacheClearResult.NothingToClear;
        if (IsLink(target)) return CacheClearResult.Link;
        SafeDirectoryTree.Delete(target);
        return CacheClearResult.Cleared;
    }

    private static CacheEntry Measure(string key, string path, CacheKind kind)
    {
        bool isFile = File.Exists(path);
        bool isFolder = Directory.Exists(path);
        if (!isFile && !isFolder) return new CacheEntry(key, path, kind, false, false, 0, 0);
        if (isFile) return new CacheEntry(key, path, kind, true, false, SafeLength(path), 1);
        if (IsLink(path)) return new CacheEntry(key, path, kind, true, true, 0, 0);
        long bytes = 0;
        int files = 0;
        // Links inside (WebView2 may create some) are not followed and not counted.
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            foreach (string entry in SafeChildren(folder))
            {
                if (IsLink(entry)) continue;
                if (Directory.Exists(entry)) pending.Push(entry);
                else
                {
                    bytes += SafeLength(entry);
                    files++;
                }
            }
        }
        return new CacheEntry(key, path, kind, true, false, bytes, files);
    }

    private static IEnumerable<string> SafeChildren(string folder)
    {
        try { return Directory.EnumerateFileSystemEntries(folder).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
