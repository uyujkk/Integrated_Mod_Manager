namespace IntegratedModManager.Core.Deployment;

/// <summary>One install backup folder: where it is, when it was made and how much it holds.</summary>
public sealed record InstallBackupFolder(string Path, DateTime CreatedUtc, long Size);

/// <summary>
/// Keeping the install backups under the storage limit set on the Updates page: the oldest backups go first until the
/// rest fit. The backup just made is never removed, nor anything that does not resolve inside the backup folder. A
/// backup that could not be removed still counts, so the next oldest goes instead.
/// </summary>
public static class InstallBackupPruning
{
    /// <summary>The limit in bytes for a limit in GB (as stored in the settings).</summary>
    public static long LimitBytes(double limitGb) => (long)(limitGb * 1024 * 1024 * 1024);

    /// <summary>
    /// Removes backups through <paramref name="tryDelete"/> (false when it failed) and returns the ones removed.
    /// </summary>
    public static IReadOnlyList<InstallBackupFolder> Prune(
        IReadOnlyList<InstallBackupFolder> backups,
        long limitBytes,
        string backupRoot,
        string? preservePath,
        Func<string, bool> tryDelete)
    {
        string root = Path.GetFullPath(backupRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long totalBytes = backups.Sum(item => item.Size);
        var removed = new List<InstallBackupFolder>();
        foreach (InstallBackupFolder backup in backups.OrderBy(item => item.CreatedUtc))
        {
            if (totalBytes <= limitBytes)
            {
                break;
            }
            if (!string.IsNullOrWhiteSpace(preservePath) && string.Equals(backup.Path, preservePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string resolved = Path.GetFullPath(backup.Path);
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tryDelete(resolved))
            {
                totalBytes -= backup.Size;
                removed.Add(backup);
            }
        }
        return removed;
    }
}
