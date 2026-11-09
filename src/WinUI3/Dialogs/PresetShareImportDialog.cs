using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Importing a shared preset: which Mods match and the new preset's name (moved from the window in stage J8).</summary>
internal static class PresetShareImportDialog
{
    /// <summary>The name for the imported preset, or null when cancelled.</summary>
    public static async Task<string?> ShowAsync(AppServices services, PresetShareFile file, PresetSharePreview preview, string gameName, string suggestedName, double windowHeight)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var nameBox = new TextBox { Header = T("PresetShare.Name"), Text = suggestedName, MaxLength = 200 };
        var content = new StackPanel { Spacing = 10, MaxWidth = 560 };
        content.Children.Add(new TextBlock
        {
            Text = T("PresetShare.Summary", preview.Matched.Count, file.Mods.Count, preview.MatchPercent(file.Mods.Count)),
            TextWrapping = TextWrapping.Wrap
        });
        if (!preview.SameGame)
            content.Children.Add(new InfoBar { Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false, Message = T("PresetShare.OtherGame", file.Game, gameName) });
        if (preview.Missing.Count > 0)
            content.Children.Add(new TextBlock { Text = T("PresetShare.Missing", string.Join("\n", preview.Missing.Take(30))), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = T("PresetShare.Import"), Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(200, windowHeight - 260) },
            PrimaryButtonText = T("PresetShare.ImportMatched"), CloseButtonText = T("Common.Cancel"), DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = preview.Matched.Count > 0
        };
        nameBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = preview.Matched.Count > 0 && nameBox.Text.Trim().Length > 0;
        return await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary ? nameBox.Text.Trim() : null;
    }
}
