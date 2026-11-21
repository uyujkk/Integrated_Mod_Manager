using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mod install pipeline (stage J finish, step 4, moved unchanged from the window): install transactions with their
/// backups, commit and rollback; file and INI conflict checks before an install; broken link cleanup; archives
/// (InstallService.Archives.cs); and the copy ownership records of issue #107 (InstallService.CopyOwnership.cs).
/// The rules themselves are in Core (ModInstallTransactions, ModFileConflictDetector, ModIniConflictDetector,
/// ArchiveImportLayout, CopyDeploymentLedger); this service holds their state, localizes their errors and asks the user.
/// </summary>
internal sealed partial class InstallService(AppServices services)
{
    private const int ConflictLinesPerGroup = 8;

    private Action<string, Exception> _log = (_, _) => { };
    private Action _saveShellConfig = () => { };
    private Func<double> _backupLimitGb = () => 0;
    private Action<int, string> _extractionProgress = (_, _) => { };
    private Action<string> _showStatus = _ => { };

    /// <summary>
    /// Called once by the window: the error log, saving beta-shell.json, the backup storage limit, extraction progress
    /// (already on the UI thread's queue) and the workspace status line.
    /// </summary>
    public void Attach(
        Action<string, Exception> log,
        Action saveShellConfig,
        Func<double> backupLimitGb,
        Action<int, string> extractionProgress,
        Action<string> showStatus)
    {
        _log = log;
        _saveShellConfig = saveShellConfig;
        _backupLimitGb = backupLimitGb;
        _extractionProgress = extractionProgress;
        _showStatus = showStatus;
    }

    private string T(string key, params object?[] values) => services.Language.T(key, values);

    /// <summary>The most recent install transaction the Updates page can undo; saved in beta-shell.json.</summary>
    public string? LastTransactionPath { get; set; }

    public bool CanUndoLastTransaction => !string.IsNullOrWhiteSpace(LastTransactionPath) && Directory.Exists(LastTransactionPath);

    /// <summary>
    /// Lists links in the loader folder whose source is gone (deleted, renamed or moved
    /// outside the manager) and offers to remove just those links. Returns false only
    /// when the user cancels the operation.
    /// </summary>
    public async Task<bool> OfferBrokenLinkCleanupAsync(string loaderRoot)
    {
        IReadOnlyList<BrokenDirectoryLink> broken;
        try
        {
            broken = await Task.Run(() => DirectoryLinkDeployment.FindBrokenLinks(loaderRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log("Find broken deployment links", ex);
            return true;
        }

        if (broken.Count == 0)
        {
            return true;
        }

        string list = string.Join("\n", broken.Take(20).Select(link =>
            $"{Path.GetFileName(link.LinkPath)}  →  {link.Target ?? T("InstallTransactions.TargetUnreadable")}"));
        if (broken.Count > 20)
        {
            list += T("InstallTransactions.AndMore", broken.Count - 20);
        }

        ContentDialogResult result = await BrokenLinksDialog.ShowAsync(services, broken.Count, list);
        if (result == ContentDialogResult.None)
        {
            return false;
        }

        if (result == ContentDialogResult.Secondary)
        {
            return true;
        }

        var failures = new List<string>();
        int removed = 0;
        foreach (BrokenDirectoryLink link in broken)
        {
            try
            {
                // Re-checked: a source restored meanwhile keeps its link.
                if (DirectoryLinkDeployment.RemoveBrokenLink(link.LinkPath))
                {
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add($"{Path.GetFileName(link.LinkPath)}: {ex.Message}");
                _log("Remove broken deployment link", ex);
            }
        }

        _showStatus(T("InstallTransactions.RemovedBrokenLinkS", removed));
        if (failures.Count > 0)
        {
            await services.Dialogs.MessageAsync(
                T("InstallTransactions.SomeLinksWereKept"),
                T("InstallTransactions.TheseBrokenLinksCouldNotBe") + string.Join("\n", failures));
        }

        return true;
    }

    public List<string> DetectModFileConflicts(string sourcePath, string targetRoot, string targetPathToIgnore)
    {
        return DetectModFileConflicts(sourcePath, targetRoot, [targetPathToIgnore]);
    }

    public List<string> DetectModFileConflicts(
        string sourcePath,
        string targetRoot,
        IEnumerable<string> targetPathsToIgnore)
    {
        return WithLocalizedInstallErrors(() => ModFileConflictDetector.Detect(sourcePath, targetRoot, targetPathsToIgnore));
    }

    public async Task<bool> ConfirmInstallConflictsAsync(
        IReadOnlyList<string> fileConflicts,
        IReadOnlyList<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts)
    {
        string text = BuildInstallConflictText(fileConflicts, iniConflicts);
        return await InstallConflictsDialog.ConfirmAsync(services, text);
    }

    // INI comparison is advisory: a failure is logged and never blocks deployment.
    public IReadOnlyList<ModIniConflictSummary> DetectModIniConflictsSafely(
        string candidateDirectory,
        string targetRoot,
        IEnumerable<string> ignoredTargets)
    {
        try
        {
            return ModIniConflictDetector.FindConflictsInTarget(candidateDirectory, targetRoot, ignoredTargets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log("INI conflict detection", ex);
            return [];
        }
    }

    private string BuildInstallConflictText(
        IReadOnlyList<string> fileConflicts,
        IReadOnlyList<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts)
    {
        var sections = new List<string>();
        AddSection(
            T("IniConflicts.OverrideTheSameGameResourcesWhen"),
            iniConflicts.Where(item => item.Summary.Kind == ModIniConflictKind.ResourceHash)
                .Select(item => DescribeIniConflict(item.Candidate, item.Summary))
                .ToList());
        AddSection(
            T("IniConflicts.UseTheSameShortcutOnePress"),
            iniConflicts.Where(item => item.Summary.Kind == ModIniConflictKind.Shortcut)
                .Select(item => DescribeIniConflict(item.Candidate, item.Summary))
                .ToList());
        AddSection(
            T("IniConflicts.ContainMatchingRelativeFilePathsMay"),
            fileConflicts);

        return T("IniConflicts.ThePreInstallCheckFoundThese")
            + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine + Environment.NewLine, sections);

        void AddSection(string title, IReadOnlyList<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }

            string body = string.Join(Environment.NewLine, lines.Take(ConflictLinesPerGroup).Select(line => "• " + line));
            if (lines.Count > ConflictLinesPerGroup)
            {
                int more = lines.Count - ConflictLinesPerGroup;
                body += Environment.NewLine + T("IniConflicts.AndMore", more);
            }

            sections.Add(title + Environment.NewLine + body);
        }
    }

    private string DescribeIniConflict(string? candidate, ModIniConflictSummary summary)
    {
        string pair = candidate is null ? summary.InstalledModName : $"{candidate} ↔ {summary.InstalledModName}";
        return summary.Kind == ModIniConflictKind.ResourceHash
            ? T("IniConflicts.SharedHashEsEG", pair, summary.Count, summary.InstalledSection, summary.Example)
            : T("IniConflicts.Text", pair, summary.Example, summary.CandidateSection, summary.InstalledSection);
    }

    public async Task<string> CreateInstallTransactionAsync(string description, WorkspaceRepository repository, IEnumerable<string> affectedTargets)
    {
        List<string> targets = affectedTargets.ToList();
        string transactionPath = await Task.Run(() => WithLocalizedInstallErrors(() => ModInstallTransactions.Create(
            services.Deployment.InstallBackupPath, description, repository.Id, repository.TargetPath, repository.SourcePath, targets, _log)));
        PruneInstallTransactionsByStorageLimit(transactionPath);
        return transactionPath;
    }

    public void CopyDirectoryTree(string sourcePath, string targetPath)
        => SafeDirectoryTree.Copy(sourcePath, targetPath);

    public (List<string> Directories, List<string> Files) InspectDirectoryTreeSafely(string rootPath)
        => SafeDirectoryTree.Inspect(rootPath);

    public string GetSafePathInsideDirectory(string rootPath, string relativePath)
    {
        return PathSafety.ResolveInsideDirectory(rootPath, relativePath);
    }

    public void DeleteDirectoryTreeSafely(string path)
        => SafeDirectoryTree.Delete(path);

    public void CommitInstallTransaction(string transactionPath)
    {
        LastTransactionPath = transactionPath;
        _saveShellConfig();
    }

    public async Task RollbackInstallTransactionAsync(string transactionPath, bool clearLastTransaction)
    {
        ModInstallTransaction transaction = await Task.Run(() => WithLocalizedInstallErrors(() => ModInstallTransactions.ReadManifest(transactionPath)));
        WorkspaceRepository repository = services.Repositories.Items.FirstOrDefault(item => item.Id == transaction.RepositoryId)
            ?? throw new InvalidOperationException(T("InstallTransactions.TheRepositoryForThisBackupNo"));

        await Task.Run(() => WithLocalizedInstallErrors(() =>
        {
            ModInstallTransactions.Rollback(transactionPath, transaction, repository.TargetPath, repository.SourcePath, _log);
            return true;
        }));

        if (clearLastTransaction && string.Equals(LastTransactionPath, transactionPath, StringComparison.OrdinalIgnoreCase))
        {
            LastTransactionPath = null;
            _saveShellConfig();
        }
    }

    public void PruneInstallTransactionsByStorageLimit(string? preservePath)
    {
        try
        {
            if (!Directory.Exists(services.Deployment.InstallBackupPath))
            {
                return;
            }

            string root = Path.GetFullPath(services.Deployment.InstallBackupPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            List<(DirectoryInfo Directory, long Size)> directories = new DirectoryInfo(services.Deployment.InstallBackupPath).EnumerateDirectories()
                .Select(directory => (directory, GetDirectorySize(directory.FullName)))
                .OrderByDescending(item => item.directory.CreationTimeUtc)
                .ToList();
            long limitBytes = (long)(_backupLimitGb() * 1024 * 1024 * 1024);
            long totalBytes = directories.Sum(item => item.Size);
            foreach ((DirectoryInfo directory, long size) in directories.OrderBy(item => item.Directory.CreationTimeUtc))
            {
                if (totalBytes <= limitBytes)
                {
                    break;
                }
                if (!string.IsNullOrWhiteSpace(preservePath) && string.Equals(directory.FullName, preservePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string resolved = Path.GetFullPath(directory.FullName);
                if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    DeleteDirectoryTreeSafely(resolved);
                    totalBytes -= size;
                }
                catch (Exception ex)
                {
                    _log("Backup pruning", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _log("Backup pruning inventory", ex);
        }
    }

    public long GetDirectorySize(string path)
    {
        try
        {
            return InspectDirectoryTreeSafely(path).Files.Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            _log("Directory size calculation", ex);
            return 0;
        }
    }

    private T WithLocalizedInstallErrors<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (LocalizeInstallError(ex) is Exception localized)
        {
            throw localized;
        }
    }

    /// <summary>Maps a tagged Core install failure to the user's language; null keeps the original.</summary>
    private Exception? LocalizeInstallError(Exception ex)
    {
        return ErrorTags.KindOf<InstallTransactionErrorKind>(ex) switch
        {
            InstallTransactionErrorKind.TargetOutsideTargetRoot => new InvalidOperationException(
                T("InstallTransactions.RefusedToBackUpAPath"), ex),
            InstallTransactionErrorKind.LinkOutsideRepository => new InvalidOperationException(T("InstallTransactions.RefusedToBackUpADirectory"), ex),
            InstallTransactionErrorKind.ManifestMissing => new FileNotFoundException(
                T("InstallTransactions.TheInstallBackupManifestWasNot"), (ex as FileNotFoundException)?.FileName, ex),
            InstallTransactionErrorKind.ManifestInvalid => new InvalidDataException(
                T("InstallTransactions.TheInstallBackupManifestIsInvalid"), ex),
            InstallTransactionErrorKind.RestoreTargetOutsideTargetRoot => new InvalidOperationException(
                T("InstallTransactions.TheBackupContainsAPathOutside"), ex),
            InstallTransactionErrorKind.DuplicateTargets => new InvalidDataException(
                T("InstallTransactions.TheBackupManifestContainsDuplicateTargets"), ex),
            InstallTransactionErrorKind.TargetOccupiedByFile => new IOException(
                T("InstallTransactions.ABackupTargetIsOccupiedBy"), ex),
            InstallTransactionErrorKind.InvalidLinkTarget => new InvalidDataException(T("InstallTransactions.TheDirectoryJunctionTargetInThe"), ex),
            InstallTransactionErrorKind.BackupIncomplete => new DirectoryNotFoundException(
                T("InstallTransactions.TheInstallBackupIsIncomplete"), ex),
            InstallTransactionErrorKind.InvalidTargetPath => new InvalidDataException(
                T("InstallTransactions.TheBackupTargetPathIsInvalid"), ex),
            InstallTransactionErrorKind.StagingUnavailable => new IOException(
                T("InstallTransactions.UnableToCreateSafeRestoreStaging"), ex),
            InstallTransactionErrorKind.UnresolvedInstalledLink => new IOException(
                T("InstallTransactions.UnableToResolveAnInstalledDirectory"), ex),
            _ => null
        };
    }
}
