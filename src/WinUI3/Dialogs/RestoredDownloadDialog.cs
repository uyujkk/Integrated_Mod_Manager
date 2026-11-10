using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Before a download restored from the journal starts again (moved from the window in stage J8).</summary>
internal static class RestoredDownloadDialog
{
    public static async Task<bool> ConfirmAsync(AppServices services, string repositoryName, string destination, string fileName)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var dialog = new ContentDialog
        {
            Title = T("DownloadRecovery.RestartARestoredDownload"),
            Content = new TextBlock { Text = T("DownloadRecovery.RepositoryFolderFileDownloadsAgainAfter", repositoryName, destination, fileName), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MaxWidth = 560 },
            PrimaryButtonText = T("DownloadRecovery.ConfirmRestart"), CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
