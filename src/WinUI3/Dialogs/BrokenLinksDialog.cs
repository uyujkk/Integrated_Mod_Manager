using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Broken Mod links in the loader folder before a deployment (moved from the window in stage J8).</summary>
internal static class BrokenLinksDialog
{
    /// <summary>Primary: remove them; Secondary: continue without cleanup; None: cancel.</summary>
    public static async Task<ContentDialogResult> ShowAsync(AppServices services, int count, string list)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var dialog = new ContentDialog
        {
            Title = T("InstallTransactions.BrokenModLinksFound"),
            Content = new ScrollViewer
            {
                MaxHeight = services.Dialogs.Viewport.ContentHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Text = T("InstallTransactions.LinkSInTheLoaderFolder", count, list)
                }
            },
            PrimaryButtonText = T("InstallTransactions.RemoveBrokenLinks"),
            SecondaryButtonText = T("InstallTransactions.ContinueWithoutCleanup"),
            CloseButtonText = T("Common.Cancel"),
        };
        DialogService.MarkDangerous(dialog);
        return await services.Dialogs.ShowAsync(dialog);
    }
}
