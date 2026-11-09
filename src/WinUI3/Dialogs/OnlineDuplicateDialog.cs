using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>The repository already has this online entry installed (moved from the window in stage J8).</summary>
internal static class OnlineDuplicateDialog
{
    public static async Task<bool> ConfirmAsync(AppServices services, string summary, IEnumerable<string> installedPaths)
    {
        string T(string key) => services.Language.T(key);
        var body = new TextBlock { Text = summary + "\n\n" + string.Join("\n", installedPaths.Take(5))
            + T("OnlineDownloads.ContinuingCreatesASeparateCopy"), TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        var dialog = new ContentDialog { Title = T("OnlineDownloads.ExistingInstallationFound"), Content = body,
            PrimaryButtonText = T("OnlineDownloads.DownloadASeparateCopy"), CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close };
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
