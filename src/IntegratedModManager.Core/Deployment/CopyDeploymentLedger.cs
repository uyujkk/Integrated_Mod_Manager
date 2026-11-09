using IntegratedModManager.Core.Repository;

namespace IntegratedModManager.Core.Deployment;

/// <summary>
/// A Mod folder the manager copied into the loader folder (issue #107). Kept in the manager's own settings, never
/// as a file in the Mod folder, where the loader could read it.
/// </summary>
public sealed class CopyDeploymentRecord
{
    public string RepositoryId { get; set; } = string.Empty;

    /// <summary>The source Mod as "character\Mod" in the repository.</summary>
    public string SourceRelativePath { get; set; } = string.Empty;

    /// <summary>The copied folder in the loader folder.</summary>
    public string DeployedPath { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>How the folder became managed: copied by the manager, claimed by identical content, or taken over by the user.</summary>
    public string Origin { get; set; } = CopyDeploymentOrigin.Copied;
}

public static class CopyDeploymentOrigin
{
    public const string Copied = "copied";
    public const string ClaimedByContent = "claimed";
    public const string Adopted = "adopted";
}

/// <summary>
/// Which copied folders in a loader folder belong to the manager. A folder with a repository Mod's name is the
/// manager's only when it was recorded, or, for copies made before the records existed, when its file list and
/// every file's size equal the repository Mod's (the user's choice A: strict); such a copy is recorded the first time
/// it is seen. Anything else is an external folder: it is shown, kept, and taken over only when the user says so.
/// </summary>
public sealed class CopyDeploymentLedger
{
    private readonly string _repositoryId;
    private readonly string _sourceRoot;
    // Several ledgers may share the settings' list (and the window reads it to save): it is the lock.
    private readonly IList<CopyDeploymentRecord> _records;

    public CopyDeploymentLedger(string repositoryId, string sourceRoot, IList<CopyDeploymentRecord> records)
    {
        _repositoryId = repositoryId;
        _sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        _records = records;
    }

    /// <summary>A record was added or removed; the settings need saving.</summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Whether <paramref name="deployedPath"/> is the manager's copy of <paramref name="sourcePath"/>. A recorded copy
    /// is, whatever was edited in it since; an unrecorded one is claimed when its content is identical.
    /// </summary>
    public bool Owns(string sourcePath, string deployedPath)
    {
        string relative = Relative(sourcePath);
        lock (_records)
        {
            if (Find(deployedPath) is { } record)
                return string.Equals(record.SourceRelativePath, relative, StringComparison.OrdinalIgnoreCase);
        }
        if (!SameContent(sourcePath, deployedPath)) return false;
        Add(sourcePath, deployedPath, CopyDeploymentOrigin.ClaimedByContent);
        return true;
    }

    /// <summary>The manager copied the Mod there.</summary>
    public void Record(string sourcePath, string deployedPath) => Add(sourcePath, deployedPath, CopyDeploymentOrigin.Copied);

    /// <summary>The user took over a folder the manager could not recognise.</summary>
    public void Adopt(string sourcePath, string deployedPath) => Add(sourcePath, deployedPath, CopyDeploymentOrigin.Adopted);

    /// <summary>The copy was removed (or replaced by a link).</summary>
    public void Forget(string deployedPath)
    {
        lock (_records)
        {
            if (Find(deployedPath) is { } record)
            {
                _records.Remove(record);
                Changed = true;
            }
        }
    }

    /// <summary>
    /// A character of the repository was renamed (issue #108): its records now name the new source path, and a copy
    /// inside the character folder its new place. Each change goes into <paramref name="log"/>, so a failed rename
    /// puts the records back. Other repositories and characters are left alone.
    /// </summary>
    public static void RenameCharacter(IList<CopyDeploymentRecord> records, string repositoryId, CharacterRenamePlan plan, ReferenceUndoLog log)
    {
        lock (records)
        {
            foreach (CopyDeploymentRecord record in records.Where(record => string.Equals(record.RepositoryId, repositoryId, StringComparison.Ordinal)))
            {
                log.Rewrite(() => record.SourceRelativePath, value => record.SourceRelativePath = value, plan.MapRelative);
                log.Rewrite(() => record.DeployedPath, value => record.DeployedPath = value, plan.MapFullPath);
            }
        }
    }

    /// <summary>
    /// The same files, by relative path, with the same sizes. Links inside either folder are not followed, and a
    /// folder that cannot be read is not the same.
    /// </summary>
    public static bool SameContent(string sourcePath, string deployedPath)
    {
        try
        {
            if (!Directory.Exists(sourcePath) || !Directory.Exists(deployedPath)) return false;
            if ((File.GetAttributes(deployedPath) & FileAttributes.ReparsePoint) != 0) return false;
            Dictionary<string, long> source = Snapshot(sourcePath), deployed = Snapshot(deployedPath);
            return source.Count == deployed.Count
                && source.All(file => deployed.TryGetValue(file.Key, out long length) && length == file.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Dictionary<string, long> Snapshot(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return new DirectoryInfo(folder).EnumerateFiles("*", options)
            .ToDictionary(file => Path.GetRelativePath(folder, file.FullName), file => file.Length, StringComparer.OrdinalIgnoreCase);
    }

    private void Add(string sourcePath, string deployedPath, string origin)
    {
        lock (_records)
        {
            if (Find(deployedPath) is { } old) _records.Remove(old);
            _records.Add(new CopyDeploymentRecord
            {
                RepositoryId = _repositoryId,
                SourceRelativePath = Relative(sourcePath),
                DeployedPath = Normalize(deployedPath),
                Origin = origin
            });
            Changed = true;
        }
    }

    private CopyDeploymentRecord? Find(string deployedPath)
    {
        string path = Normalize(deployedPath);
        return _records.FirstOrDefault(record => string.Equals(record.RepositoryId, _repositoryId, StringComparison.Ordinal)
            && string.Equals(Normalize(record.DeployedPath), path, StringComparison.OrdinalIgnoreCase));
    }

    private string Relative(string sourcePath) => Path.GetRelativePath(_sourceRoot, Normalize(sourcePath));

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
