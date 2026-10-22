using System.Text.Json;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _modUpdatesInProgress = new(StringComparer.OrdinalIgnoreCase);
    private bool _showingModUpdateNotification;

    private void PopulateStableIntervalOptions(ComboBox combo, UpdateCheckInterval selected)
    {
        (UpdateCheckInterval Value, string Label)[] choices =
        [
            (UpdateCheckInterval.Manual, L("仅手动检查", "Manual only")),
            (UpdateCheckInterval.Daily, L("每天启动时检查", "Check daily on startup")),
            (UpdateCheckInterval.EveryThreeDays, L("每 3 天启动时检查", "Check every 3 days on startup")),
            (UpdateCheckInterval.Weekly, L("每周启动时检查", "Check weekly on startup"))
        ];
        // Do not clear/recreate an open ComboBox in its SelectionChanged call stack.
        if (combo.Items.Count == 0)
            foreach (var choice in choices) combo.Items.Add(new ComboBoxItem { Tag = choice.Value, Content = choice.Label });
        foreach (ComboBoxItem item in combo.Items.OfType<ComboBoxItem>())
        {
            var choice = choices.First(option => Equals(option.Value, item.Tag));
            if (!Equals(item.Content, choice.Label)) item.Content = choice.Label;
            if (choice.Value == selected && !ReferenceEquals(combo.SelectedItem, item)) combo.SelectedItem = item;
        }
    }

    private async Task NotifyModUpdatesAvailableAsync()
    {
        if (_showingModUpdateNotification || _onlineWindowClosed) return;
        var updates = _trackedModUpdateResults.Where(item => item.HasUpdate).ToArray();
        if (updates.Length == 0) return;
        _showingModUpdateNotification = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = L("发现 Mod 更新", "Mod updates available"),
                Content = new TextBlock
                {
                    Text = L($"发现 {updates.Length} 个 Mod 有更新：", $"Updates found for {updates.Length} Mods:")
                        + "\n\n" + string.Join("\n", updates.Take(6).Select(item => "• " + item.Title))
                        + L("\n\n请在更新页选择文件并确认后下载替换，旧版将保留备份。", "\n\nChoose a file and confirm Download and Replace in Updates. The old version will be backed up."),
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 520
                },
                PrimaryButtonText = L("查看更新", "View updates"), CloseButtonText = L("稍后", "Later"),
                DefaultButton = ContentDialogButton.Close, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _currentPrimarySection = PrimarySection.Updates;
                ApplyShellState(refreshRepository: false);
            }
        }
        finally { _showingModUpdateNotification = false; }
    }

    private async Task DownloadAndReplaceTrackedModAsync(TrackedModUpdateResult result)
    {
        if (!_modUpdatesInProgress.Add(result.Path)) return;
        DownloadTaskItem? task = null;
        bool committed = false;
        string? work = null;
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is null || !_trackedModOrigins.TryGetValue(result.Path, out TrackedModOrigin? origin)
                || origin.ItemId != result.ItemId) throw new IOException(L("原 Mod 记录已改变，请重新检查更新。", "The tracked Mod changed. Check for updates again."));
            string repositoryId = repository.Id;
            string repositoryPath = repository.SourcePath;
            ModUpdateSnapshot snapshot = await Task.Run(() => ModUpdateReplacement.Capture(repositoryPath, result.Path));
            OnlineModCard mod = await FetchGameBananaModCardAsyncV2(result.ItemId)
                ?? throw new IOException(L("无法读取最新下载文件。", "Could not load current download files."));
            var candidates = mod.DownloadFiles.Where(file => file.IsSupportedArchive && !file.IsArchived && !string.IsNullOrWhiteSpace(file.DownloadUrl)).ToArray();
            if (candidates.Length == 0) throw new IOException(L("此条目没有可用压缩包，请打开页面查看。", "No supported archives are available. Review the source page."));
            var picker = new ComboBox { Header = L("选择更新文件／变体", "Choose update file / variant"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var file in candidates)
                picker.Items.Add(new ComboBoxItem { Content = $"{file.FileName} ({FormatFileSize(file.FileSizeBytes)})", Tag = file });
            picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is OnlineDownloadCandidate file
                && !string.IsNullOrWhiteSpace(origin.DownloadFileId) && file.FileId == origin.DownloadFileId);
            if (picker.SelectedItem is null && picker.Items.Count == 1) picker.SelectedIndex = 0;
            var body = new StackPanel { Spacing = 12, MaxWidth = 560 };
            body.Children.Add(new TextBlock
            {
                Text = result.Path + L("\n\n将完整替换这个本地 Mod 文件夹；旧文件（含自行修改的 INI）会备份在仓库旁的 .imm-mod-update-backups。目录联接继续使用原路径；复制部署的目标目录需在更新后重新部署。", "\n\nThe entire local Mod folder will be replaced. Old files, including edited INIs, will be backed up beside the repository in .imm-mod-update-backups. Directory links keep their path; copy deployments must be deployed again."),
                TextWrapping = TextWrapping.Wrap
            });
            body.Children.Add(picker);
            var dialog = new ContentDialog
            {
                Title = L("下载并替换 Mod", "Download and replace Mod"), Content = body,
                PrimaryButtonText = L("下载并替换", "Download and replace"), CloseButtonText = L("取消", "Cancel"),
                IsPrimaryButtonEnabled = picker.SelectedItem is not null, DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme
            };
            picker.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = picker.SelectedItem is not null;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (picker.SelectedItem is not ComboBoxItem { Tag: OnlineDownloadCandidate selected }) return;
            OnlineModCard effective = WithSelectedDownloadFile(mod, selected);
            task = CreateDownloadTask(effective, result.Path);
            // Recovery must never replay a replacement as an ordinary separate installation.
            task.SourceIdentity = "mod-update|" + task.SourceIdentity;
            RefreshUpdatesPane();
            work = Path.Combine(AppContext.BaseDirectory, "cache", "mod-updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            using IDisposable execution = await _onlineDownloadScheduler.AcquireAsync(task.Cancellation.Token);
            string archive = EnsureDownloadArchiveExtension(await DownloadOnlineModArchiveAsync(effective, work, task, task.Cancellation.Token));
            if (!IsSupportedArchiveFile(archive)) throw new InvalidDataException(L("下载文件不是可解压的压缩包。", "The downloaded file is not a supported archive."));
            string payload = Path.Combine(work, "payload");
            Directory.CreateDirectory(payload);
            UpdateDownloadTask(task, DownloadTaskState.Extracting, 92, "正在解压更新...", "Extracting update...", forceRefresh: true);
            await Task.Run(() =>
            {
                ExtractArchiveToDirectory(archive, payload);
                ArchiveImportLayout.HoistSingleTopFolder(payload);
            });
            task.Cancellation.Token.ThrowIfCancellationRequested();
            // Never redirect a captured update to whichever repository is now visible.
            if (!_repositories.Any(item => item.Id == repositoryId && string.Equals(item.SourcePath, repositoryPath, StringComparison.OrdinalIgnoreCase))
                || !_trackedModOrigins.TryGetValue(result.Path, out var current) || !ReferenceEquals(current, origin))
                throw new IOException(L("原仓库或跟踪记录已改变，未替换文件。", "The original repository or tracking record changed. Files were not replaced."));
            string backupRoot = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath)))!, ".imm-mod-update-backups");
            task.ReplacementCommitting = true;
            UpdateDownloadTask(task, DownloadTaskState.Extracting, 98, "正在备份并替换...", "Backing up and replacing...", forceRefresh: true);
            ModUpdateReplacementResult replaced = await Task.Run(() => ModUpdateReplacement.Replace(snapshot, payload, backupRoot,
                JsonSerializer.Serialize(origin), task.Cancellation.Token));
            committed = true;
            origin.LastKnownUpdatedAt = mod.UpdatedAt;
            origin.DownloadFileId = selected.FileId;
            origin.DownloadFileName = selected.FileName;
            origin.Title = mod.Title;
            origin.ProfileUrl = mod.ProfileUrl;
            origin.PreviewUrl = mod.PreviewUrl;
            _modLinks[result.Path] = origin.ProfileUrl;
            result.HasUpdate = false;
            result.LastKnownUpdatedAt = origin.LastKnownUpdatedAt;
            result.StatusTextZh = "更新已替换；复制部署请重新部署。";
            result.StatusTextEn = "Update replaced; redeploy copy installations.";
            result.BackupPath = replaced.BackupPath;
            SetModUpdateStatus($"已更新 {origin.Title}，旧版已备份；复制部署请重新部署。", $"Updated {origin.Title}; old version backed up. Redeploy copy installations.");
            UpdateDownloadTask(task, DownloadTaskState.Completed, 100, $"已替换，旧版备份：{replaced.BackupPath}", $"Replaced; old version backed up: {replaced.BackupPath}", forceRefresh: true);
            if (!SaveConfig()) throw new IOException(L("更新文件已替换，但跟踪记录未能保存。", "Updated files were replaced, but tracking could not be saved."));
            SaveShellConfig(throwOnError: true);
            if (GetSelectedRepository()?.Id == repositoryId) await RefreshListsAsync();
            ShowAppNotification($"Mod 已更新：{origin.Title}。复制部署请重新部署。", $"Mod updated: {origin.Title}. Redeploy copy installations.");
        }
        catch (OperationCanceledException) when (!committed)
        {
            if (task is not null) UpdateDownloadTask(task, DownloadTaskState.Canceled, task.Progress, "更新已取消，原 Mod 未替换。", "Update canceled; original Mod unchanged.", forceRefresh: true);
        }
        catch (Exception ex)
        {
            LogApplicationIssueWithStack("Tracked Mod update", ex);
            if (task is not null && !committed) UpdateDownloadTask(task, DownloadTaskState.Failed, task.Progress, $"更新失败：{ex.Message}", $"Update failed: {ex.Message}", forceRefresh: true);
            if (!_onlineWindowClosed) await ShowMessageAsync(
                L(committed ? "文件已替换并备份，但保存记录或刷新失败：" : "更新未完成，原文件保留：", committed ? "Files were replaced and backed up, but saving or refreshing failed: " : "Update stopped; original files retained: ") + ex.Message,
                L("Mod 更新", "Mod update"));
        }
        finally
        {
            _modUpdatesInProgress.Remove(result.Path);
            // Retain failed downloads for diagnosis; successful work is an owned cache folder.
            if (committed && work is not null) { try { Directory.Delete(work, true); } catch (Exception ex) { LogApplicationIssue("Clean Mod update cache", ex); } }
            if (!_onlineWindowClosed) RefreshUpdatesPane();
        }
    }
}
