using System.Text;
using System.Text.Json;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IOFileAttributes = System.IO.FileAttributes;

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
        HashSet<string> ignoredTargets = targetPathsToIgnore
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> sourceFiles = InspectDirectoryTreeSafely(sourcePath).Files
            .Select(path => Path.GetRelativePath(sourcePath, path))
            .Where(IsConflictRelevantRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<string>();
        foreach (string installedModPath in Directory.GetDirectories(targetRoot))
        {
            if (ignoredTargets.Contains(Path.GetFullPath(installedModPath)))
            {
                continue;
            }

            string inspectionRoot = installedModPath;
            if (DirectoryLinkDeployment.IsBrokenLink(installedModPath))
            {
                // Its source was deleted or renamed: nothing to compare, and it must not
                // stop every other installation. Cleanup is offered before deployment.
                continue;
            }

            if (DirectoryLinkDeployment.IsDirectoryLink(installedModPath))
            {
                inspectionRoot = DirectoryLinkDeployment.TryGetTarget(installedModPath)
                    ?? throw new IOException(L("无法读取已安装目录联接的目标。", "Unable to resolve an installed directory junction."));
            }

            foreach (string file in InspectDirectoryTreeSafely(inspectionRoot).Files)
            {
                string relativePath = Path.GetRelativePath(inspectionRoot, file);
                if (IsConflictRelevantRelativePath(relativePath) && sourceFiles.Contains(relativePath))
                {
                    conflicts.Add($"{Path.GetFileName(installedModPath)}  →  {relativePath}");
                    if (conflicts.Count >= 100)
                    {
                        return conflicts;
                    }
                }
            }
        }

        return conflicts;
    }

    private static bool IsConflictRelevantRelativePath(string relativePath)
    {
        if (relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
        {
            return true;
        }

        string fileName = Path.GetFileName(relativePath);
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        if (baseName.Equals("preview", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("cover", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("thumbnail", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !fileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("license", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
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
        string transactionPath = Path.Combine(_modInstallBackupPath, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionPath);
        var transaction = new ModInstallTransaction
        {
            Description = description,
            RepositoryId = repository.Id
        };

        try
        {
            int index = 0;
            foreach (string targetPath in affectedTargets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string fullTargetPath = Path.GetFullPath(targetPath);
                if (!IsPathInsideDirectory(fullTargetPath, repository.TargetPath))
                {
                    throw new InvalidOperationException(L("拒绝备份目标文件夹之外的路径。", "Refused to back up a path outside the target folder."));
                }

                bool existed = Directory.Exists(fullTargetPath);
                bool wasDirectoryLink = existed && DirectoryLinkDeployment.IsDirectoryLink(fullTargetPath);
                string? linkTarget = wasDirectoryLink ? DirectoryLinkDeployment.TryGetTarget(fullTargetPath) : null;
                if (wasDirectoryLink
                    && (string.IsNullOrWhiteSpace(linkTarget) || !IsPathInsideDirectory(linkTarget, repository.SourcePath)))
                {
                    throw new InvalidOperationException(L(
                        "拒绝备份目标指向仓库之外的目录联接。",
                        "Refused to back up a directory junction that points outside the repository."));
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
                    string backupPath = Path.Combine(transactionPath, backupRelativePath);
                    await Task.Run(() => CopyDirectoryTree(fullTargetPath, backupPath));
                }

                index++;
            }

            string manifest = JsonSerializer.Serialize(transaction, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(transactionPath, "transaction.json"), manifest, Encoding.UTF8);
            PruneInstallTransactionsByStorageLimit(transactionPath);
            return transactionPath;
        }
        catch
        {
            if (Directory.Exists(transactionPath))
            {
                try
                {
                    DeleteDirectoryTreeSafely(transactionPath);
                }
                catch (Exception cleanupException)
                {
                    LogApplicationIssue("Install transaction cleanup", cleanupException);
                }
            }
            throw;
        }
    }

    private static void CopyDirectoryTree(string sourcePath, string targetPath)
    {
        (List<string> directories, List<string> files) = InspectDirectoryTreeSafely(sourcePath);
        Directory.CreateDirectory(targetPath);
        foreach (string directory in directories)
        {
            string destination = GetSafePathInsideDirectory(targetPath, Path.GetRelativePath(sourcePath, directory));
            Directory.CreateDirectory(destination);
        }

        foreach (string file in files)
        {
            string destination = GetSafePathInsideDirectory(targetPath, Path.GetRelativePath(sourcePath, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static (List<string> Directories, List<string> Files) InspectDirectoryTreeSafely(string rootPath)
        => SafeDirectoryTree.Inspect(rootPath);

    private static string GetSafePathInsideDirectory(string rootPath, string relativePath)
    {
        return PathSafety.ResolveInsideDirectory(rootPath, relativePath);
    }

    private static void DeleteDirectoryTreeSafely(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        IOFileAttributes rootAttributes = File.GetAttributes(path);
        if ((rootAttributes & IOFileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, false);
            return;
        }

        foreach (FileSystemInfo entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            IOFileAttributes attributes = entry.Attributes;
            if ((attributes & IOFileAttributes.Directory) != 0)
            {
                if ((attributes & IOFileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(entry.FullName, false);
                }
                else
                {
                    DeleteDirectoryTreeSafely(entry.FullName);
                }
            }
            else
            {
                File.Delete(entry.FullName);
            }
        }

        Directory.Delete(path, false);
    }

    private void CommitInstallTransaction(string transactionPath)
    {
        _lastInstallTransactionPath = transactionPath;
        SaveShellConfig();
    }

    private async Task RollbackInstallTransactionAsync(string transactionPath, bool clearLastTransaction)
    {
        string manifestPath = Path.Combine(transactionPath, "transaction.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(L("找不到安装备份清单。", "The install backup manifest was not found."), manifestPath);
        }

        ModInstallTransaction transaction = JsonSerializer.Deserialize<ModInstallTransaction>(await File.ReadAllTextAsync(manifestPath))
            ?? throw new InvalidDataException(L("安装备份清单无效。", "The install backup manifest is invalid."));
        WorkspaceRepository repository = _repositories.FirstOrDefault(item => item.Id == transaction.RepositoryId)
            ?? throw new InvalidOperationException(L("找不到此备份对应的仓库。", "The repository for this backup no longer exists."));

        await Task.Run(() =>
        {
            var restoreItems = new List<(string TargetPath, string? BackupPath, string StagingPath, string DisplacedPath, bool ExistedBefore, string? LinkTarget)>();
            var uniqueTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string restoreToken = Guid.NewGuid().ToString("N");

            foreach (ModInstallBackupEntry entry in transaction.Entries)
            {
                string targetPath = Path.GetFullPath(entry.TargetPath);
                if (!IsPathInsideDirectory(targetPath, repository.TargetPath))
                {
                    throw new InvalidOperationException(L("备份包含目标文件夹之外的路径，已停止回滚。", "The backup contains a path outside the target folder. Rollback was stopped."));
                }

                if (!uniqueTargets.Add(targetPath))
                {
                    throw new InvalidDataException(L("备份清单包含重复目标。", "The backup manifest contains duplicate targets."));
                }

                if (File.Exists(targetPath))
                {
                    throw new IOException(L("备份目标被同名文件占用。", "A backup target is occupied by a file."));
                }

                string? backupPath = null;
                string? linkTarget = null;
                if (entry.ExistedBefore)
                {
                    if (!string.IsNullOrWhiteSpace(entry.LinkTarget))
                    {
                        linkTarget = Path.GetFullPath(entry.LinkTarget);
                        if (!IsPathInsideDirectory(linkTarget, repository.SourcePath) || !Directory.Exists(linkTarget))
                        {
                            throw new InvalidDataException(L(
                                "安装备份中的目录联接目标无效。",
                                "The directory-junction target in the install backup is invalid."));
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(entry.BackupRelativePath))
                    {
                        throw new InvalidDataException(L("安装备份清单无效。", "The install backup manifest is invalid."));
                    }
                    else
                    {
                        backupPath = GetSafePathInsideDirectory(transactionPath, entry.BackupRelativePath);
                        if (!Directory.Exists(backupPath))
                        {
                            throw new DirectoryNotFoundException(L("安装备份内容不完整。", "The install backup is incomplete."));
                        }
                    }
                }

                string parentPath = Path.GetDirectoryName(targetPath)
                    ?? throw new InvalidDataException(L("备份目标路径无效。", "The backup target path is invalid."));
                Directory.CreateDirectory(parentPath);
                string targetName = Path.GetFileName(targetPath);
                string stagingPath = Path.Combine(parentPath, $".{targetName}.restore-{restoreToken}");
                string displacedPath = Path.Combine(parentPath, $".{targetName}.previous-{restoreToken}");
                if (Directory.Exists(stagingPath) || Directory.Exists(displacedPath) || File.Exists(stagingPath) || File.Exists(displacedPath))
                {
                    throw new IOException(L("无法创建安全恢复临时目录。", "Unable to create safe restore staging directories."));
                }

                restoreItems.Add((targetPath, backupPath, stagingPath, displacedPath, entry.ExistedBefore, linkTarget));
            }

            foreach (var item in restoreItems.Where(item => item.ExistedBefore))
            {
                if (!string.IsNullOrWhiteSpace(item.LinkTarget))
                {
                    DirectoryLinkDeployment.CreateJunction(item.StagingPath, item.LinkTarget);
                }
                else
                {
                    CopyDirectoryTree(item.BackupPath!, item.StagingPath);
                }
            }

            var switchedItems = new List<(string TargetPath, string? BackupPath, string StagingPath, string DisplacedPath, bool ExistedBefore, string? LinkTarget)>();
            try
            {
                try
                {
                    foreach (var item in restoreItems)
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
                        var item = switchedItems[index];
                        if (Directory.Exists(item.TargetPath))
                        {
                            DeleteDirectoryTreeSafely(item.TargetPath);
                        }
                        if (Directory.Exists(item.DisplacedPath))
                        {
                            Directory.Move(item.DisplacedPath, item.TargetPath);
                        }
                    }
                    throw;
                }

                foreach (var item in switchedItems)
                {
                    if (!Directory.Exists(item.DisplacedPath))
                    {
                        continue;
                    }

                    try
                    {
                        DeleteDirectoryTreeSafely(item.DisplacedPath);
                    }
                    catch (Exception ex)
                    {
                        LogApplicationIssue("Restore cleanup", ex);
                    }
                }
            }
            finally
            {
                foreach (var item in restoreItems)
                {
                    if (Directory.Exists(item.StagingPath))
                    {
                        DeleteDirectoryTreeSafely(item.StagingPath);
                    }
                }
            }
        });

        if (clearLastTransaction && string.Equals(_lastInstallTransactionPath, transactionPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastInstallTransactionPath = null;
            SaveShellConfig();
        }
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
