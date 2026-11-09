using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Files or INI sections a deployment would share with other Mods (moved from the window in stage J8).</summary>
internal static class InstallConflictsDialog
{
    public static async Task<bool> ConfirmAsync(AppServices services, string conflicts)
    {
        string T(string key) => services.Language.T(key);
        var dialog = new ContentDialog
        {
            Title = T("InstallTransactions.PotentialModConflictsDetected"),
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = conflicts,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            },
            PrimaryButtonText = T("InstallTransactions.ContinueAnyway"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
