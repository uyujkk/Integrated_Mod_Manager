using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Tracked Mods have updates (moved from the window in stage J8).</summary>
internal static class ModUpdatesAvailableDialog
{
    /// <summary>True when the user wants to see the updates.</summary>
    public static async Task<bool> ShowAsync(AppServices services, IReadOnlyList<string> titles)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var dialog = new ContentDialog
        {
            Title = T("ModUpdates.ModUpdatesAvailable"),
            Content = new TextBlock
            {
                Text = T("ModUpdates.UpdatesFoundForMods", titles.Count)
                    + "\n\n" + string.Join("\n", titles.Take(6).Select(title => "• " + title))
                    + T("ModUpdates.ChooseAFileAndConfirmDownload"),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 520
            },
            PrimaryButtonText = T("ModUpdates.ViewUpdates"), CloseButtonText = T("ModUpdates.Later"),
            DefaultButton = ContentDialogButton.Close
        };
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
