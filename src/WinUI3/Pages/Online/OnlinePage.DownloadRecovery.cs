namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private bool _downloadJournalWarningShown;
    internal int _restoredInterruptedDownloads;

    internal void LoadDownloadTasks()
    {
        try
        {
            (int interrupted, bool usedBackup) = _services.Downloads.Restore();
            _restoredInterruptedDownloads = interrupted;
            if (usedBackup) _host.LogApplicationIssue("Download queue recovered from backup", new IOException("The primary queue record was invalid; the previous complete record was loaded."));
        }
        catch (Exception ex)
        {
            // Never replace an unreadable queue with an empty one (the service stops using the journal).
            _host.LogApplicationIssue("Load download recovery record", ex);
        }
    }

    internal void PersistDownloadTasks(bool required = false)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            if (required) throw new InvalidOperationException("Download queue snapshots require the UI thread.");
            DispatcherQueue.TryEnqueue(() => { if (!_services.IsClosing) PersistDownloadTasks(); });
            return;
        }
        try
        {
            if (!_services.Downloads.JournalAvailable) throw new IOException(T("DownloadRecovery.TheDownloadRecordCouldNotBe"));
            _services.Downloads.Save();
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Save download recovery record", ex);
            if (required) throw;
            if (!_services.IsClosing && !_downloadJournalWarningShown)
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
