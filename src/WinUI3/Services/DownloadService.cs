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
}
