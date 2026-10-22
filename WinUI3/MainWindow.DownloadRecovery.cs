using IntegratedModManager.Core;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly DownloadTaskJournal<OnlineDownloadRetryRequest> _downloadJournal = new(
        Path.Combine(AppContext.BaseDirectory, "backups", "download-queue", "tasks.json"));
    private bool _downloadJournalAvailable = true;
    private bool _downloadJournalWarningShown;
    private int _restoredInterruptedDownloads;

    private void LoadDownloadTasks()
    {
        try
        {
            var restored = _downloadJournal.Load();
            foreach (var record in restored.Tasks)
            {
                _downloadTasks.Add(new DownloadTaskItem
                {
                    Id = record.Id, Title = record.Title, SourceIdentity = record.SourceIdentity,
                    DestinationPath = record.DestinationPath, ArchivePath = record.ArchivePath,
                    State = record.State, Progress = record.Progress,
                    StatusZh = record.StatusZh, StatusEn = record.StatusEn,
                    RetryRequest = record.Request, RestoredFromJournal = true,
                    Interrupted = record.Interrupted
                });
            }
            _restoredInterruptedDownloads = restored.Tasks.Count(task => task.Interrupted);
            if (restored.UsedBackup) LogApplicationIssue("Download queue recovered from backup", new IOException("The primary queue record was invalid; the previous complete record was loaded."));
        }
        catch (Exception ex)
        {
            // Never replace an unreadable queue with an empty one.
            _downloadJournalAvailable = false;
            LogApplicationIssue("Load download recovery record", ex);
        }
    }

    private void PersistDownloadTasks(bool required = false)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            if (required) throw new InvalidOperationException("Download queue snapshots require the UI thread.");
            DispatcherQueue.TryEnqueue(() => { if (!_onlineWindowClosed) PersistDownloadTasks(); });
            return;
        }
        try
        {
            if (!_downloadJournalAvailable) throw new IOException(L("下载记录无法读取，已保留原文件。请先检查诊断报告和记录文件。", "The download record could not be read and was preserved. Check diagnostics and the record before starting new downloads."));
            _downloadJournal.Save(_downloadTasks.Select(task => ModUpdateRecoveryPolicy.ForSnapshot(new DownloadTaskRecord<OnlineDownloadRetryRequest>(
                task.Id, task.Title, task.SourceIdentity, task.DestinationPath, task.State, task.Progress,
                task.StatusZh, task.StatusEn, task.RetryRequest, task.ArchivePath, task.Interrupted))));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Save download recovery record", ex);
            if (required) throw;
            if (!_onlineWindowClosed && !_downloadJournalWarningShown)
            {
                _downloadJournalWarningShown = true;
                ShowAppNotification("下载记录保存失败，当前任务重启后可能无法恢复。", "Download history could not be saved; current tasks may not be recoverable after restart.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
        }
    }

    private async Task<bool> ConfirmRestoredDownloadAsync(DownloadTaskItem task, OnlineDownloadRetryRequest request)
    {
        if (request.Mod is null || request.Context is null || request.CharacterAliases is null
            || !_repositories.Any(repository => repository.Id == request.Context.RepositoryId
                && string.Equals(repository.SourcePath, request.Context.RepositoryPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(L("原仓库已移除或路径已改变，请从在线卡片重新选择下载位置。", "The original repository was removed or changed. Start from the online card to choose a destination again."));
        if (!task.RestoredFromJournal) return true;
        var dialog = new ContentDialog
        {
            Title = L("重新下载已恢复的任务", "Restart a restored download"),
            Content = new TextBlock { Text = L($"仓库：{request.Context.RepositoryName}\n目录：{request.Destination}\n文件：{request.File?.FileName ?? request.Mod.DownloadFileName}\n\n将重新获取下载信息并从头下载，不会自动清理上次留下的文件。若上次在解压或安装阶段退出，请先检查原目录。", $"Repository: {request.Context.RepositoryName}\nFolder: {request.Destination}\nFile: {request.File?.FileName ?? request.Mod.DownloadFileName}\n\nDownloads again after refreshing file information. Files left by the previous attempt are preserved. Inspect the original folder if the app exited during extraction or installation."), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MaxWidth = 560 },
            PrimaryButtonText = L("确认重新下载", "Confirm restart"), CloseButtonText = L("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Close, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
