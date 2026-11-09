using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>The download options of the Online page (moved from the window in stage J8).</summary>
internal static class DownloadOptionsDialog
{
    /// <summary>The chosen options, or null when cancelled.</summary>
    public static async Task<(int Concurrency, bool RememberFolders)?> ShowAsync(AppServices services, int concurrentDownloads, bool rememberFolders)
    {
        string T(string key) => services.Language.T(key);
        var concurrency = new ComboBox { Header = T("OnlineDownloads.ConcurrentDownloadTasks"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (int value in Enumerable.Range(1, 4)) concurrency.Items.Add(value);
        concurrency.SelectedItem = concurrentDownloads;
        var remember = new CheckBox { Content = T("OnlineDownloads.RememberConfirmedDownloadFoldersPerRepository"), IsChecked = rememberFolders };
        var body = new StackPanel { Spacing = 12, MinWidth = 300 };
        body.Children.Add(concurrency); body.Children.Add(remember);
        body.Children.Add(new TextBlock { Text = T("OnlineDownloads.RememberedFoldersMustStillBeInside"), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
        var dialog = new ContentDialog { Title = T("Common.DownloadOptions"), Content = body, PrimaryButtonText = T("Common.Save"), CloseButtonText = T("Common.Cancel") };
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return ((int)concurrency.SelectedItem, remember.IsChecked == true);
    }
}
