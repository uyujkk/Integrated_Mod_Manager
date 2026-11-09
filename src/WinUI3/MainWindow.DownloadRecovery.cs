using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private bool _downloadJournalAvailable = true;
    private bool _downloadJournalWarningShown;
    private int _restoredInterruptedDownloads;

    private void LoadDownloadTasks()
    {
        try
        {
            var restored = _services.Downloads.Journal.Load();
            foreach (var record in restored.Tasks)
            {
                _services.Downloads.Tasks.Add(new DownloadTaskItem
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
            _services.Downloads.Journal.Save(_services.Downloads.Tasks.Select(task => ModUpdateRecoveryPolicy.ForSnapshot(new DownloadTaskRecord<OnlineDownloadRetryRequest>(
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
                ShowAppNotification(T("DownloadRecovery.HistoryNotSaved"), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
        }
    }

    private async Task<bool> ConfirmRestoredDownloadAsync(DownloadTaskItem task, OnlineDownloadRetryRequest request)
    {
        if (request.Mod is null || request.Context is null || request.CharacterAliases is null
            || !_services.Repositories.Items.Any(repository => repository.Id == request.Context.RepositoryId
                && string.Equals(repository.SourcePath, request.Context.RepositoryPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(T("DownloadRecovery.TheOriginalRepositoryWasRemovedOr"));
        if (!task.RestoredFromJournal) return true;
return await RestoredDownloadDialog.ConfirmAsync(_services, request.Context.RepositoryName, request.Destination, request.File?.FileName ?? request.Mod.DownloadFileName);
    }
}
