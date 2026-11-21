using System.Collections.ObjectModel;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The download tasks shown in the navigation and on the Updates page, and their journal. Creating and updating tasks,
/// saving and restoring the journal and clearing finished tasks moved here from the window and the Online page in
/// stage J finish, step 5 (unchanged).
/// </summary>
internal sealed class DownloadService(string baseDirectory)
{
    private AppServices _services = null!;
    private DispatcherQueue _ui = null!;
    private Action<string, Exception> _log = (_, _) => { };
    private bool _journalWarningShown;

    /// <summary>Called once by the window: the shared services, the UI thread's queue and the error log.</summary>
    public void Attach(AppServices services, DispatcherQueue ui, Action<string, Exception> log)
    {
        _services = services;
        _ui = ui;
        _log = log;
    }

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    /// <summary>How many unfinished downloads <see cref="Load"/> found interrupted (the window mentions them once).</summary>
    public int RestoredInterrupted { get; private set; }

    /// <summary>
    /// Raised on the UI thread when a task's card should show its new state (throttled while downloading); the second
    /// value says whether the task finished, so installed states are read again.
    /// </summary>
    public event EventHandler<(DownloadTaskItem Task, bool Finished)>? TaskRefreshRequested;

    public ObservableCollection<DownloadTaskItem> Tasks { get; } = [];

    /// <summary>Unfinished downloads, restored after a restart.</summary>
    public DownloadTaskJournal<OnlineDownloadRetryRequest> Journal { get; } = new(
        Path.Combine(baseDirectory, "backups", "download-queue", "tasks.json"));

    /// <summary>How many downloads run at once (the Online page's download options set the limit).</summary>
    public OnlineDownloadScheduler Scheduler { get; } = new(2);

    /// <summary>False once the journal could not be read: an unreadable queue is never replaced by an empty one.</summary>
    public bool JournalAvailable { get; private set; } = true;

    /// <summary>
    /// Adds the unfinished downloads of the journal to <see cref="Tasks"/> (moved from the window in stage J6).
    /// Returns how many were interrupted and whether the backup record was used; throws when the journal cannot be read.
    /// </summary>
    public (int Interrupted, bool UsedBackup) Restore()
    {
        try
        {
            var restored = Journal.Load();
            foreach (var record in restored.Tasks)
            {
                Tasks.Add(new DownloadTaskItem
                {
                    Id = record.Id, Title = record.Title, SourceIdentity = record.SourceIdentity,
                    DestinationPath = record.DestinationPath, ArchivePath = record.ArchivePath,
                    State = record.State, Progress = record.Progress,
                    StatusZh = record.StatusZh, StatusEn = record.StatusEn,
                    RetryRequest = record.Request, RestoredFromJournal = true,
                    Interrupted = record.Interrupted
                });
            }
            return (restored.Tasks.Count(task => task.Interrupted), restored.UsedBackup);
        }
        catch
        {
            JournalAvailable = false;
            throw;
        }
    }

    /// <summary>Writes the tasks to the journal; the caller checks <see cref="JournalAvailable"/> first.</summary>
    public void Save()
        => Journal.Save(Tasks.Select(task => ModUpdateRecoveryPolicy.ForSnapshot(new DownloadTaskRecord<OnlineDownloadRetryRequest>(
            task.Id, task.Title, task.SourceIdentity, task.DestinationPath, task.State, task.Progress,
            task.StatusZh, task.StatusEn, task.RetryRequest, task.ArchivePath, task.Interrupted))));

    /// <summary>Whether a retry has to wait: the Online page is preparing another download. Set by the window.</summary>
    public Func<bool> IsRetryBlocked { get; set; } = () => false;

    /// <summary>Raised when tasks were added, removed or finished; lists of tasks rebuild themselves.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when one task's state or progress changed; its views update in place.</summary>
    public event EventHandler<DownloadTaskItem>? TaskUpdated;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void NotifyTaskUpdated(DownloadTaskItem task) => TaskUpdated?.Invoke(this, task);

    /// <summary>Reads the journal at startup; an unreadable one is logged and never replaced by an empty one.</summary>
    public void Load()
    {
        try
        {
            (int interrupted, bool usedBackup) = Restore();
            RestoredInterrupted = interrupted;
            if (usedBackup) _log("Download queue recovered from backup", new IOException("The primary queue record was invalid; the previous complete record was loaded."));
        }
        catch (Exception ex)
        {
            // Never replace an unreadable queue with an empty one (the service stops using the journal).
            _log("Load download recovery record", ex);
        }
    }

    public void Persist(bool required = false)
    {
        if (!_ui.HasThreadAccess)
        {
            if (required) throw new InvalidOperationException("Download queue snapshots require the UI thread.");
            _ui.TryEnqueue(() => { if (!_services.IsClosing) Persist(); });
            return;
        }
        try
        {
            if (!JournalAvailable) throw new IOException(T("DownloadRecovery.TheDownloadRecordCouldNotBe"));
            Save();
        }
        catch (Exception ex)
        {
            _log("Save download recovery record", ex);
            if (required) throw;
            if (!_services.IsClosing && !_journalWarningShown)
            {
                _journalWarningShown = true;
                _services.Status.Notify(T("DownloadRecovery.HistoryNotSaved"), InfoBarSeverity.Warning);
            }
        }
    }

    public DownloadTaskItem CreateTask(OnlineModCard mod, string destinationPath)
    {
        var task = new DownloadTaskItem
        {
            Title = mod.Title,
            SourceIdentity = OnlineDownloadService.GetOnlineDownloadIdentity(mod),
            DestinationPath = destinationPath,
            State = DownloadTaskState.Queued,
            Status = M("DownloadTasks.WaitingToStart"),
            StatusZh = UiText.Chinese("DownloadTasks.WaitingToStart"),
            StatusEn = UiText.English("DownloadTasks.WaitingToStart")
        };
        Tasks.Insert(0, task);
        while (Tasks.Count > 30)
        {
            DownloadTaskItem? oldestFinished = Tasks.LastOrDefault(item =>
                item.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled);
            if (oldestFinished is null)
            {
                break;
            }
            Tasks.Remove(oldestFinished);
            oldestFinished.Cancellation.Dispose();
        }
        NotifyChanged();
        return task;
    }

    public void UpdateTask(
        DownloadTaskItem task,
        DownloadTaskState state,
        double progress,
        UiMessage status,
        bool forceRefresh = false)
    {
        if (!DownloadProgressPolicy.CanApply(task.State, state))
        {
            return;
        }

        bool phaseChanged = task.State != state;
        task.State = state;
        task.Progress = DownloadProgressPolicy.MergeProgress(task.Progress, progress);
        task.Status = status;
        task.StatusZh = status.Resolve(UiLanguagePolicy.Chinese);
        task.StatusEn = status.Resolve(UiLanguagePolicy.English);
        if (!_services.IsClosing && (phaseChanged || DownloadProgressPolicy.IsTerminal(state))) Persist();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!forceRefresh && now - task.LastUiRefreshUtc < TimeSpan.FromMilliseconds(125))
        {
            return;
        }
        task.LastUiRefreshUtc = now;
        _ui.TryEnqueue(() =>
        {
            if (_services.IsClosing) return;
            TaskRefreshRequested?.Invoke(this, (task, DownloadProgressPolicy.IsTerminal(task.State)));
        });
    }

    /// <summary>Removes the finished tasks from the list and the journal.</summary>
    public void ClearFinished()
    {
        foreach (DownloadTaskItem task in Tasks
            .Where(item => item.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled)
            .ToList())
        {
            Tasks.Remove(task);
            task.Cancellation.Dispose();
        }
        NotifyChanged();
    }
}
