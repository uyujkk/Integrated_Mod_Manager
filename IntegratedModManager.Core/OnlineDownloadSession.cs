namespace IntegratedModManager.Core;

/// <summary>Immutable routing captured before the first asynchronous download step.</summary>
public sealed record OnlineDownloadContext(string RepositoryId, string RepositoryPath, string RepositoryName);

public sealed record OnlineDownloadCleanup(string? ArchivePath, string? ExtractionPath);

/// <summary>
/// Records only resources explicitly acquired by this attempt. The caller owns filesystem
/// cleanup and must not register pre-existing resources. Not a filesystem lock or transaction.
/// </summary>
public sealed class OnlineDownloadSession
{
    public OnlineDownloadSession(OnlineDownloadContext context, string destinationPath)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        DestinationPath = Path.GetFullPath(destinationPath);
    }

    public OnlineDownloadContext Context { get; }
    public string DestinationPath { get; }
    public string? ArchivePath { get; private set; }
    public string? ExtractionPath { get; private set; }
    public bool CommitStarted { get; private set; }
    public bool IsCommitted { get; private set; }
    public bool CanCancel => !CommitStarted;

    public void RegisterArchive(string path)
    {
        EnsurePreparing();
        string ownedPath = RequireDirectChild(path);
        if (string.Equals(ownedPath, ExtractionPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The archive cannot be the extraction directory.", nameof(path));
        ArchivePath = ownedPath;
    }

    public void RegisterExtraction(string path)
    {
        EnsurePreparing();
        if (ExtractionPath is not null)
            throw new InvalidOperationException("This attempt already owns an extraction directory.");
        string ownedPath = RequireDirectChild(path);
        if (string.Equals(ownedPath, ArchivePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The extraction directory cannot be the archive.", nameof(path));
        ExtractionPath = ownedPath;
    }

    /// <summary>
    /// Preparation may await and must not mutate shared tracking dictionaries. The synchronous
    /// commit must not partially mutate and then throw. No cancellation point after commit starts.
    /// A persistence warning should be returned as metadata, not thrown after a completed commit.
    /// </summary>
    public async Task<T> PrepareAndCommitAsync<T>(
        Func<CancellationToken, Task<T>> prepare,
        Action<T> commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(commit);
        EnsurePreparing();
        if (ExtractionPath is null)
            throw new InvalidOperationException("No extraction directory has been acquired.");
        cancellationToken.ThrowIfCancellationRequested();
        T prepared = await prepare(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePreparing();
        CommitStarted = true;
        commit(prepared);
        IsCommitted = true;
        return prepared;
    }

    public OnlineDownloadCleanup GetCleanup(bool canceled)
        => IsCommitted ? new(null, null) : new(canceled ? ArchivePath : null, ExtractionPath);

    private void EnsurePreparing()
    {
        if (CommitStarted)
            throw new InvalidOperationException("Metadata commit has already started.");
    }

    private string RequireDirectChild(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? parent = Path.GetDirectoryName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                DestinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Owned resources must be direct children of the confirmed destination.", nameof(path));
        return fullPath;
    }
}

/// <summary>Same source identity in different repository roots must retain independent records.</summary>
public sealed record OnlineTrackedInstallation(string Path, string Identity, bool Exists, DateTimeOffset? UpdatedAt = null);

public static class OnlineTrackingScopePolicy
{
    public static IReadOnlyList<string> FindStaleDuplicatePaths(
        IEnumerable<OnlineTrackedInstallation> installations, IEnumerable<string> repositoryPaths)
    {
        string[] roots = repositoryPaths.ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stale = new List<string>();
        foreach (OnlineTrackedInstallation item in installations.OrderByDescending(item => item.Exists)
            .ThenByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(item.Identity)) continue;
            string scope;
            try { scope = GetScope(item.Path, roots); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!seen.Add(scope + "\n" + item.Identity) && !item.Exists)
                stale.Add(item.Path);
        }
        return stale;
    }

    public static string GetScope(string modPath, IEnumerable<string> repositoryPaths)
    {
        ArgumentNullException.ThrowIfNull(repositoryPaths);
        string path = Path.GetFullPath(modPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? best = null;
        foreach (string root in repositoryPaths.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string normalized;
            try { normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if ((string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && (best is null || normalized.Length > best.Length))
                best = normalized;
        }
        return best ?? Path.GetDirectoryName(path) ?? path;
    }
}
