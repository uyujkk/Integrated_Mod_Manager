using System.Collections.ObjectModel;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;

namespace ModFolderCopier.WinUI;

/// <summary>The download tasks shown in the navigation and on the Updates page, and their journal.</summary>
internal sealed class DownloadService(string baseDirectory)
{
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
}
