using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;
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
            if (!_downloadJournalAvailable) throw new IOException(T("DownloadRecovery.TheDownloadRecordCouldNotBe"));
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
            throw new InvalidDataException(T("DownloadRecovery.TheOriginalRepositoryWasRemovedOr"));
        if (!task.RestoredFromJournal) return true;
        var dialog = new ContentDialog
        {
            Title = T("DownloadRecovery.RestartARestoredDownload"),
            Content = new TextBlock { Text = T("DownloadRecovery.RepositoryFolderFileDownloadsAgainAfter", request.Context.RepositoryName, request.Destination, request.File?.FileName ?? request.Mod.DownloadFileName), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MaxWidth = 560 },
            PrimaryButtonText = T("DownloadRecovery.ConfirmRestart"), CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
