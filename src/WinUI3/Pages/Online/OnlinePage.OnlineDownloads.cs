using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private async void OnOnlineDownloadOptionsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await DownloadOptionsDialog.ShowAsync(_services, _services.Online.Preferences.ConcurrentDownloads, _services.Online.Preferences.RememberDownloadFolders)
                is not { } options) return;
            _services.Online.Preferences.ConcurrentDownloads = options.Concurrency;
            _services.Online.Preferences.RememberDownloadFolders = options.RememberFolders;
            _services.Downloads.Scheduler.Limit = _services.Online.Preferences.ConcurrentDownloads;
            _host.SaveShellConfig();
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnOnlineDownloadOptionsClicked", ex);
        }
    }
}
