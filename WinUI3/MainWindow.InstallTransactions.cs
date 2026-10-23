using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    /// <summary>
    /// Lists links in the loader folder whose source is gone (deleted, renamed or moved
    /// outside the manager) and offers to remove just those links. Returns false only
    /// when the user cancels the operation.
    /// </summary>
    private async Task<bool> OfferBrokenLinkCleanupAsync(string loaderRoot)
    {
        IReadOnlyList<BrokenDirectoryLink> broken;
        try
        {
            broken = await Task.Run(() => DirectoryLinkDeployment.FindBrokenLinks(loaderRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogApplicationIssue("Find broken deployment links", ex);
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

        DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = T("InstallTransactions.BrokenModLinksFound"),
            Content = new ScrollViewer
            {
                MaxHeight = viewport.ContentHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Text = T("InstallTransactions.LinkSInTheLoaderFolder", broken.Count, list)
                }
            },
            PrimaryButtonText = T("InstallTransactions.RemoveBrokenLinks"),
            SecondaryButtonText = T("InstallTransactions.ContinueWithoutCleanup"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogResult result = await dialog.ShowAsync();
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
                LogApplicationIssue("Remove broken deployment link", ex);
            }
        }

        StatusTextBlock.Text = T("InstallTransactions.RemovedBrokenLinkS", removed);
        if (failures.Count > 0)
        {
            await ShowMessageAsync(
                T("InstallTransactions.TheseBrokenLinksCouldNotBe") + string.Join("\n", failures),
                T("InstallTransactions.SomeLinksWereKept"));
        }

        return true;
    }

    private List<string> DetectModFileConflicts(string sourcePath, string targetRoot, string targetPathToIgnore)
    {
        return DetectModFileConflicts(sourcePath, targetRoot, [targetPathToIgnore]);
    }

    private List<string> DetectModFileConflicts(
        string sourcePath,
        string targetRoot,
        IEnumerable<string> targetPathsToIgnore)
    {
        return WithLocalizedInstallErrors(() => ModFileConflictDetector.Detect(sourcePath, targetRoot, targetPathsToIgnore));
    }

    private async Task<bool> ConfirmInstallConflictsAsync(
        IReadOnlyList<string> fileConflicts,
        IReadOnlyList<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts)
    {
        string text = BuildInstallConflictText(fileConflicts, iniConflicts);
        var dialog = new ContentDialog
        {
            Title = T("InstallTransactions.PotentialModConflictsDetected"),
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            },
            PrimaryButtonText = T("InstallTransactions.ContinueAnyway"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<string> CreateInstallTransactionAsync(string description, WorkspaceRepository repository, IEnumerable<string> affectedTargets)
    {
        List<string> targets = affectedTargets.ToList();
        string transactionPath = await Task.Run(() => WithLocalizedInstallErrors(() => ModInstallTransactions.Create(
            _modInstallBackupPath, description, repository.Id, repository.TargetPath, repository.SourcePath, targets, LogApplicationIssue)));
        PruneInstallTransactionsByStorageLimit(transactionPath);
        return transactionPath;
    }

    private static void CopyDirectoryTree(string sourcePath, string targetPath)
        => SafeDirectoryTree.Copy(sourcePath, targetPath);

    private static (List<string> Directories, List<string> Files) InspectDirectoryTreeSafely(string rootPath)
        => SafeDirectoryTree.Inspect(rootPath);

    private static string GetSafePathInsideDirectory(string rootPath, string relativePath)
    {
        return PathSafety.ResolveInsideDirectory(rootPath, relativePath);
    }

    private static void DeleteDirectoryTreeSafely(string path)
        => SafeDirectoryTree.Delete(path);

    private void CommitInstallTransaction(string transactionPath)
    {
        _lastInstallTransactionPath = transactionPath;
        SaveShellConfig();
    }

    private async Task RollbackInstallTransactionAsync(string transactionPath, bool clearLastTransaction)
    {
        ModInstallTransaction transaction = await Task.Run(() => WithLocalizedInstallErrors(() => ModInstallTransactions.ReadManifest(transactionPath)));
        WorkspaceRepository repository = _repositories.FirstOrDefault(item => item.Id == transaction.RepositoryId)
            ?? throw new InvalidOperationException(T("InstallTransactions.TheRepositoryForThisBackupNo"));

        await Task.Run(() => WithLocalizedInstallErrors(() =>
        {
            ModInstallTransactions.Rollback(transactionPath, transaction, repository.TargetPath, repository.SourcePath, LogApplicationIssue);
            return true;
        }));

        if (clearLastTransaction && string.Equals(_lastInstallTransactionPath, transactionPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastInstallTransactionPath = null;
            SaveShellConfig();
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

    private async void OnRollbackLastInstallClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_lastInstallTransactionPath) || !Directory.Exists(_lastInstallTransactionPath))
            {
                RefreshConfigurationProfiles();
                return;
            }

            if (!await ShowConfirmAsync(
                T("InstallTransactions.UndoTheMostRecentModCopy"),
                T("InstallTransactions.UndoModChange")))
            {
                return;
            }

            SetBusyState(true);
            try
            {
                await RollbackInstallTransactionAsync(_lastInstallTransactionPath, clearLastTransaction: true);
                await RefreshListsAsync();
                ShowAppNotification("已恢复上次 Mod 操作前的状态。", "Restored the state before the last mod operation.");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(T("InstallTransactions.RollbackFailed") + ex.Message, T("InstallTransactions.CannotRollBack"));
            }
            finally
            {
                SetBusyState(false);
                RefreshConfigurationProfiles();
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRollbackLastInstallClicked", ex);
        }
    }
}
