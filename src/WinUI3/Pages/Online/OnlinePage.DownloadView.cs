namespace ModFolderCopier.WinUI;

// The page shows the download flow's status, progress and states while it exists (stage J finish, step 5a: the flow
// itself is in OnlineDownloadService).
public sealed partial class OnlinePage : IOnlineDownloadView
{
    void IOnlineDownloadView.SetOnlineStatus(UiMessage status) => SetOnlineStatus(status);

    void IOnlineDownloadView.SetOnlineDownloadProgress(bool isVisible, double percent, string text, bool isIndeterminate)
        => SetOnlineDownloadProgress(isVisible, percent, text, isIndeterminate);

    void IOnlineDownloadView.RefreshOnlineDetailActionState() => RefreshOnlineDetailActionState();

    void IOnlineDownloadView.RefreshOnlinePaneV2() => RefreshOnlinePaneV2();
}
