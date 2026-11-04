using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// How a download task is shown: its status line, progress and action button. Shared by the
/// navigation's task flyout and the download history on the Updates page.
/// </summary>
internal static class DownloadTaskViews
{
    public static string StatusText(DownloadTaskItem task, LanguageService language)
        => task.Status is not null ? language.T(task.Status) : BetaLocalization.Resolve(!language.IsChinese, task.StatusZh, task.StatusEn);

    // Cancel while running, retry after a failure or cancel, otherwise open the download folder.
    public static void Refresh(DownloadTaskItem task, DownloadTaskCardView view, LanguageService language, bool retryBlocked)
    {
        bool active = DownloadProgressPolicy.IsActive(task.State);
        view.StatusTextBlock.Text = StatusText(task, language);
        view.ProgressBar.Value = task.Progress;
        view.ProgressBar.IsIndeterminate = active
            && (task.State is not DownloadTaskState.Downloading || !task.HasKnownTotalLength);
        bool retry = task.State is DownloadTaskState.Failed or DownloadTaskState.Canceled && task.RetryRequest is not null;
        view.ActionButton.Content = active ? T("Common.Cancel") : retry ? T("DownloadTasks.Retry") : T("DownloadTasks.OpenFolder");
        view.ActionButton.IsEnabled = active
            ? task.State is not DownloadTaskState.Canceling && task.Session?.CanCancel != false && !task.ReplacementCommitting
            : retry ? !task.RetryInProgress && !retryBlocked : Directory.Exists(task.DestinationPath);

        string T(string key) => language.T(key);
    }
}
