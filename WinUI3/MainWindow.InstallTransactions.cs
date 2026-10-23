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
            $"{Path.GetFileName(link.LinkPath)}  →  {link.Target ?? L("（无法读取目标）", "(target unreadable)")}"));
        if (broken.Count > 20)
        {
            list += L($"\n……另有 {broken.Count - 20} 个", $"\n…and {broken.Count - 20} more");
        }

        DialogViewport viewport = DialogViewportPolicy.Selection(RootGrid.ActualWidth, RootGrid.ActualHeight);
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme,
            Title = L("发现失效的 Mod 联接", "Broken Mod links found"),
            Content = new ScrollViewer
            {
                MaxHeight = viewport.ContentHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Text = L(
                        $"加载目录中有 {broken.Count} 个联接指向已不存在的源目录（源 Mod 可能已被删除、改名或移动）：\n\n{list}\n\n清理只会移除这些联接本身，不会删除任何文件，也不会自动关联名称相近的文件夹。",
                        $"{broken.Count} link(s) in the loader folder point to source folders that no longer exist (the source Mod may have been deleted, renamed or moved):\n\n{list}\n\nCleaning up removes only these links. No files are deleted, and similarly named folders are never linked automatically.")
                }
            },
            PrimaryButtonText = L("清理失效联接", "Remove broken links"),
            SecondaryButtonText = L("暂不清理，继续", "Continue without cleanup"),
            CloseButtonText = L("取消", "Cancel"),
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

        StatusTextBlock.Text = L($"已移除 {removed} 个失效联接。", $"Removed {removed} broken link(s).");
        if (failures.Count > 0)
        {
            await ShowMessageAsync(
                L("以下失效联接无法移除，已保留：\n", "These broken links could not be removed and were kept:\n") + string.Join("\n", failures),
                L("部分联接未清理", "Some links were kept"));
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
            Title = L("检测到潜在 Mod 冲突", "Potential Mod Conflicts Detected"),
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
            PrimaryButtonText = L("仍然继续", "Continue Anyway"),
            CloseButtonText = L("取消", "Cancel"),
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
            ?? throw new InvalidOperationException(L("找不到此备份对应的仓库。", "The repository for this backup no longer exists."));

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
                L("拒绝备份目标文件夹之外的路径。", "Refused to back up a path outside the target folder."), ex),
            InstallTransactionErrorKind.LinkOutsideRepository => new InvalidOperationException(L(
                "拒绝备份目标指向仓库之外的目录联接。",
                "Refused to back up a directory junction that points outside the repository."), ex),
            InstallTransactionErrorKind.ManifestMissing => new FileNotFoundException(
                L("找不到安装备份清单。", "The install backup manifest was not found."), (ex as FileNotFoundException)?.FileName, ex),
            InstallTransactionErrorKind.ManifestInvalid => new InvalidDataException(
                L("安装备份清单无效。", "The install backup manifest is invalid."), ex),
            InstallTransactionErrorKind.RestoreTargetOutsideTargetRoot => new InvalidOperationException(
                L("备份包含目标文件夹之外的路径，已停止回滚。", "The backup contains a path outside the target folder. Rollback was stopped."), ex),
            InstallTransactionErrorKind.DuplicateTargets => new InvalidDataException(
                L("备份清单包含重复目标。", "The backup manifest contains duplicate targets."), ex),
            InstallTransactionErrorKind.TargetOccupiedByFile => new IOException(
                L("备份目标被同名文件占用。", "A backup target is occupied by a file."), ex),
            InstallTransactionErrorKind.InvalidLinkTarget => new InvalidDataException(L(
                "安装备份中的目录联接目标无效。",
                "The directory-junction target in the install backup is invalid."), ex),
            InstallTransactionErrorKind.BackupIncomplete => new DirectoryNotFoundException(
                L("安装备份内容不完整。", "The install backup is incomplete."), ex),
            InstallTransactionErrorKind.InvalidTargetPath => new InvalidDataException(
                L("备份目标路径无效。", "The backup target path is invalid."), ex),
            InstallTransactionErrorKind.StagingUnavailable => new IOException(
                L("无法创建安全恢复临时目录。", "Unable to create safe restore staging directories."), ex),
            InstallTransactionErrorKind.UnresolvedInstalledLink => new IOException(
                L("无法读取已安装目录联接的目标。", "Unable to resolve an installed directory junction."), ex),
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
                L("撤销最近一次 Mod 复制、移除或方案切换，并恢复操作前文件？", "Undo the most recent mod copy, removal, or profile change and restore the previous files?"),
                L("撤销 Mod 操作", "Undo Mod Change")))
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
                await ShowMessageAsync(L("回滚失败：", "Rollback failed: ") + ex.Message, L("无法回滚", "Cannot Roll Back"));
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
