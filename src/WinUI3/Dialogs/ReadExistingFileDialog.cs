using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Asked in the Shortcuts overlay: save from the parameter file as it is on disk (moved from the window in stage J8).</summary>
internal static class ReadExistingFileDialog
{
    public static async Task<bool> ConfirmAsync(AppServices services, XamlRoot overlayRoot, string title, string message)
    {
        string T(string key) => services.Language.T(key);
        var dialog = new ContentDialog
        {
            XamlRoot = overlayRoot, RequestedTheme = ElementTheme.Dark,
            Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = T("ShortcutCapture.ReadExisting"), CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
