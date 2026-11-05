using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The first start after installing: the app supports several games, so it asks which one the first
/// repository is for instead of assuming one (stage J4 moved it out of the window).
/// </summary>
internal static class FirstRunDialog
{
    /// <summary>The chosen game, or null when the user decides later.</summary>
    public static async Task<RepositoryPresetInfo?> ShowAsync(AppServices services, IReadOnlyList<RepositoryPresetInfo> presets, ElementTheme theme)
    {
        string T(string key) => services.Language.T(key);
        var games = new RadioButtons();
        foreach (RepositoryPresetInfo preset in presets)
        {
            var option = new RadioButton { Content = BetaLocalization.Resolve(!services.Language.IsChinese, preset.DisplayNameZh, preset.DisplayNameEn), Tag = preset.Id };
            AutomationProperties.SetAutomationId(option, "FirstRunGame_" + preset.Id);
            games.Items.Add(option);
        }
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = T("FirstRun.Explanation"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(games);
        var dialog = new ContentDialog
        {
            Title = T("FirstRun.Title"), Content = content, XamlRoot = services.Dialogs.Root, RequestedTheme = theme,
            PrimaryButtonText = T("FirstRun.Use"), CloseButtonText = T("FirstRun.Later"),
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false
        };
        games.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = games.SelectedItem is not null;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return presets.FirstOrDefault(preset => preset.Id == (games.SelectedItem as RadioButton)?.Tag as string);
    }
}
