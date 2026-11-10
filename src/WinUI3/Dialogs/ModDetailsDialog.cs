using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mod workspace's details pane shown as a dialog when the window is too narrow for it (moved from the window
/// in stage J8). The window lends the pane and takes it back afterwards.
/// </summary>
internal static class ModDetailsDialog
{
    public static async Task ShowAsync(AppServices services, UIElement detailPane)
    {
        string T(string key) => services.Language.T(key);
        var dialog = new ContentDialog { Content = detailPane, CloseButtonText = T("Common.Close") };
        try { await services.Dialogs.ShowAsync(dialog); }
        finally { dialog.Content = null; }
    }
}
