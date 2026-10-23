using System.Text;
using System.Text.Json;

namespace IntegratedModManager.Core;

public enum InstallTransactionErrorKind
{
    /// <summary>A path to back up is outside the loader's target folder.</summary>
    TargetOutsideTargetRoot,

    /// <summary>A directory junction to back up points outside the repository.</summary>
    LinkOutsideRepository,

    /// <summary>The transaction folder has no transaction.json.</summary>
    ManifestMissing,

    /// <summary>transaction.json is empty or an entry lacks its backup path.</summary>
    ManifestInvalid,

    /// <summary>A recorded target is outside the target folder; rollback stops before any change.</summary>
    RestoreTargetOutsideTargetRoot,

    /// <summary>The manifest names the same target twice.</summary>
    DuplicateTargets,

    /// <summary>A file now sits where a Mod folder must be restored.</summary>
    TargetOccupiedByFile,

    /// <summary>A recorded junction target is missing or outside the repository.</summary>
    InvalidLinkTarget,

    /// <summary>A recorded backup copy is missing from the transaction folder.</summary>
    BackupIncomplete,

    /// <summary>A recorded target has no parent directory.</summary>
    InvalidTargetPath,

    /// <summary>The temporary restore names next to a target are already taken.</summary>
    StagingUnavailable,

    /// <summary>Conflict detection met an installed junction whose target cannot be read. Detail: the link path.</summary>
    UnresolvedInstalledLink
}

/// <summary>
/// Backs up the loader-folder entries an install, removal or profile switch will touch, and
/// restores exactly that state on rollback. Copied Mods are backed up as files; junctions
/// are recorded by target. Rollback validates the whole manifest first, stages every restored
/// entry next to its target, then swaps them in and undoes the swaps if any move fails.
/// </summary>
public static class ModInstallTransactions
{
    public const string ManifestFileName = "transaction.json";

    /// <summary>Creates a transaction folder under <paramref name="backupRoot"/> and returns its path.</summary>
    public static string Create(
        string backupRoot,
        string description,
        string repositoryId,
        string targetRoot,
        string sourceRoot,
        IEnumerable<string> affectedTargets,
        Action<string, Exception>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentNullException.ThrowIfNull(affectedTargets);
        string transactionPath = Path.Combine(backupRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionPath);
        var transaction = new ModInstallTransaction
        {
            Description = description,
            RepositoryId = repositoryId
        };

        try
        {
            int index = 0;
            foreach (string targetPath in affectedTargets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string fullTargetPath = Path.GetFullPath(targetPath);
                if (!PathSafety.IsSameOrInside(fullTargetPath, targetRoot))
                {
                    throw ErrorTags.Tag(
                        new InvalidOperationException("Refused to back up a path outside the target folder."),
                        InstallTransactionErrorKind.TargetOutsideTargetRoot);
                }

                bool existed = Directory.Exists(fullTargetPath);
                bool wasDirectoryLink = existed && DirectoryLinkDeployment.IsDirectoryLink(fullTargetPath);
                string? linkTarget = wasDirectoryLink ? DirectoryLinkDeployment.TryGetTarget(fullTargetPath) : null;
                if (wasDirectoryLink
                    && (string.IsNullOrWhiteSpace(linkTarget) || !PathSafety.IsSameOrInside(linkTarget, sourceRoot)))
                {
                    throw ErrorTags.Tag(
                        new InvalidOperationException("Refused to back up a directory junction that points outside the repository."),
                        InstallTransactionErrorKind.LinkOutsideRepository);
                }

                string backupRelativePath = existed && !wasDirectoryLink
                    ? Path.Combine("files", index.ToString("D4"))
                    : string.Empty;
                transaction.Entries.Add(new ModInstallBackupEntry
                {
                    TargetPath = fullTargetPath,
                    ExistedBefore = existed,
                    BackupRelativePath = backupRelativePath,
                    LinkTarget = linkTarget
                });
                if (existed && !wasDirectoryLink)
                {
                    SafeDirectoryTree.Copy(fullTargetPath, Path.Combine(transactionPath, backupRelativePath));
                }

                index++;
            }

            string manifest = JsonSerializer.Serialize(transaction, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(transactionPath, ManifestFileName), manifest, Encoding.UTF8);
            return transactionPath;
        }
        catch
        {
            if (Directory.Exists(transactionPath))
            {
                try
                {
                    SafeDirectoryTree.Delete(transactionPath);
                }
                catch (Exception cleanupException)
                {
                    log?.Invoke("Install transaction cleanup", cleanupException);
                }
            }

            throw;
        }
    }

    public static ModInstallTransaction ReadManifest(string transactionPath)
    {
        string manifestPath = Path.Combine(transactionPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw ErrorTags.Tag(
                new FileNotFoundException("The install backup manifest was not found.", manifestPath),
                InstallTransactionErrorKind.ManifestMissing);
        }

        return JsonSerializer.Deserialize<ModInstallTransaction>(File.ReadAllText(manifestPath))
            ?? throw InvalidManifest();
    }

    /// <summary>
    /// Restores every entry of <paramref name="transaction"/>. Nothing in the target folder
    /// changes unless the whole manifest validates; a failed swap restores what was moved.
    /// </summary>
    public static void Rollback(
        string transactionPath,
        ModInstallTransaction transaction,
        string targetRoot,
        string sourceRoot,
        Action<string, Exception>? log = null)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        List<RestoreItem> restoreItems = PlanRestore(transactionPath, transaction, targetRoot, sourceRoot);

        foreach (RestoreItem item in restoreItems.Where(item => item.ExistedBefore))
        {
            if (!string.IsNullOrWhiteSpace(item.LinkTarget))
            {
                DirectoryLinkDeployment.CreateJunction(item.StagingPath, item.LinkTarget);
            }
            else
            {
                SafeDirectoryTree.Copy(item.BackupPath!, item.StagingPath);
            }
        }

        var switchedItems = new List<RestoreItem>();
        try
        {
            try
            {
                foreach (RestoreItem item in restoreItems)
                {
                    bool displacedCurrent = false;
                    if (Directory.Exists(item.TargetPath))
                    {
                        Directory.Move(item.TargetPath, item.DisplacedPath);
                        displacedCurrent = true;
                    }

                    try
                    {
                        if (item.ExistedBefore)
                        {
                            Directory.Move(item.StagingPath, item.TargetPath);
                        }
                        switchedItems.Add(item);
                    }
                    catch
                    {
                        if (displacedCurrent && Directory.Exists(item.DisplacedPath) && !Directory.Exists(item.TargetPath))
                        {
                            Directory.Move(item.DisplacedPath, item.TargetPath);
                        }
                        throw;
                    }
                }
            }
            catch
            {
                for (int index = switchedItems.Count - 1; index >= 0; index--)
                {
                    RestoreItem item = switchedItems[index];
                    if (Directory.Exists(item.TargetPath))
                    {
                        SafeDirectoryTree.Delete(item.TargetPath);
                    }
                    if (Directory.Exists(item.DisplacedPath))
                    {
                        Directory.Move(item.DisplacedPath, item.TargetPath);
                    }
                }
                throw;
            }

            foreach (RestoreItem item in switchedItems)
            {
                if (!Directory.Exists(item.DisplacedPath))
                {
                    continue;
                }

                try
                {
                    SafeDirectoryTree.Delete(item.DisplacedPath);
                }
                catch (Exception ex)
                {
                    log?.Invoke("Restore cleanup", ex);
                }
            }
        }
        finally
        {
            foreach (RestoreItem item in restoreItems)
            {
                if (Directory.Exists(item.StagingPath))
                {
                    SafeDirectoryTree.Delete(item.StagingPath);
                }
            }
        }
    }

    private static List<RestoreItem> PlanRestore(
        string transactionPath,
        ModInstallTransaction transaction,
        string targetRoot,
        string sourceRoot)
    {
        var restoreItems = new List<RestoreItem>();
        var uniqueTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string restoreToken = Guid.NewGuid().ToString("N");

        foreach (ModInstallBackupEntry entry in transaction.Entries)
        {
            string targetPath = Path.GetFullPath(entry.TargetPath);
            if (!PathSafety.IsSameOrInside(targetPath, targetRoot))
            {
                throw ErrorTags.Tag(
                    new InvalidOperationException("The backup contains a path outside the target folder. Rollback was stopped."),
                    InstallTransactionErrorKind.RestoreTargetOutsideTargetRoot);
            }

            if (!uniqueTargets.Add(targetPath))
            {
                throw ErrorTags.Tag(
                    new InvalidDataException("The backup manifest contains duplicate targets."),
                    InstallTransactionErrorKind.DuplicateTargets);
            }

            if (File.Exists(targetPath))
            {
                throw ErrorTags.Tag(new IOException("A backup target is occupied by a file."), InstallTransactionErrorKind.TargetOccupiedByFile);
            }

            string? backupPath = null;
            string? linkTarget = null;
            if (entry.ExistedBefore)
            {
                if (!string.IsNullOrWhiteSpace(entry.LinkTarget))
                {
                    linkTarget = Path.GetFullPath(entry.LinkTarget);
                    if (!PathSafety.IsSameOrInside(linkTarget, sourceRoot) || !Directory.Exists(linkTarget))
                    {
                        throw ErrorTags.Tag(
                            new InvalidDataException("The directory-junction target in the install backup is invalid."),
                            InstallTransactionErrorKind.InvalidLinkTarget);
                    }
                }
                else if (string.IsNullOrWhiteSpace(entry.BackupRelativePath))
                {
                    throw InvalidManifest();
                }
                else
                {
                    backupPath = PathSafety.ResolveInsideDirectory(transactionPath, entry.BackupRelativePath);
                    if (!Directory.Exists(backupPath))
                    {
                        throw ErrorTags.Tag(
                            new DirectoryNotFoundException("The install backup is incomplete."),
                            InstallTransactionErrorKind.BackupIncomplete);
                    }
                }
            }

            string parentPath = Path.GetDirectoryName(targetPath)
                ?? throw ErrorTags.Tag(new InvalidDataException("The backup target path is invalid."), InstallTransactionErrorKind.InvalidTargetPath);
            Directory.CreateDirectory(parentPath);
            string targetName = Path.GetFileName(targetPath);
            string stagingPath = Path.Combine(parentPath, $".{targetName}.restore-{restoreToken}");
            string displacedPath = Path.Combine(parentPath, $".{targetName}.previous-{restoreToken}");
            if (Directory.Exists(stagingPath) || Directory.Exists(displacedPath) || File.Exists(stagingPath) || File.Exists(displacedPath))
            {
                throw ErrorTags.Tag(
                    new IOException("Unable to create safe restore staging directories."),
                    InstallTransactionErrorKind.StagingUnavailable);
            }

            restoreItems.Add(new RestoreItem(targetPath, backupPath, stagingPath, displacedPath, entry.ExistedBefore, linkTarget));
        }

        return restoreItems;
    }

    private static InvalidDataException InvalidManifest() =>
        ErrorTags.Tag(new InvalidDataException("The install backup manifest is invalid."), InstallTransactionErrorKind.ManifestInvalid);

    private sealed record RestoreItem(
        string TargetPath,
        string? BackupPath,
        string StagingPath,
        string DisplacedPath,
        bool ExistedBefore,
        string? LinkTarget);
}
