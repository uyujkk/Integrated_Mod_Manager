using System.Collections.ObjectModel;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>The download tasks shown in the navigation and on the Updates page, and their journal.</summary>
internal sealed class DownloadService(string baseDirectory)
{
    public ObservableCollection<DownloadTaskItem> Tasks { get; } = [];

    /// <summary>Unfinished downloads, restored after a restart.</summary>
    public DownloadTaskJournal<OnlineDownloadRetryRequest> Journal { get; } = new(
        Path.Combine(baseDirectory, "backups", "download-queue", "tasks.json"));

    /// <summary>Whether a retry has to wait: the Online page is preparing another download. Set by the window.</summary>
    public Func<bool> IsRetryBlocked { get; set; } = () => false;

    /// <summary>Raised when tasks were added, removed or finished; lists of tasks rebuild themselves.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when one task's state or progress changed; its views update in place.</summary>
    public event EventHandler<DownloadTaskItem>? TaskUpdated;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void NotifyTaskUpdated(DownloadTaskItem task) => TaskUpdated?.Invoke(this, task);
}
