using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The first start after installing: the app supports several games, so it asks which one the first
/// repository is for instead of assuming one (stage J4 moved it out of the window). Since J4-b each game
/// is a card, two to a row, and a note says what comes next; the dashboard's tips take it from there.
/// </summary>
internal static class FirstRunDialog
{
    /// <summary>The chosen game, or null when the user decides later.</summary>
    public static async Task<RepositoryPresetInfo?> ShowAsync(AppServices services, IReadOnlyList<RepositoryPresetInfo> presets, ElementTheme theme)
    {
        string T(string key) => services.Language.T(key);
        // A grid of cards, two to a row: arrow keys move between them and the selected one is marked the
        // Fluent way, like a radio group.
        var games = new GridView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false };
        foreach (RepositoryPresetInfo preset in presets)
        {
            string name = BetaLocalization.Resolve(!services.Language.IsChinese, preset.DisplayNameZh, preset.DisplayNameEn);
            GridViewItem option = GameCard(name, services.Appearance.ThemeKey);
            option.Tag = preset.Id;
            AutomationProperties.SetAutomationId(option, "FirstRunGame_" + preset.Id);
            AutomationProperties.SetName(option, name);
            games.Items.Add(option);
        }

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock { Text = T("FirstRun.Explanation"), TextWrapping = TextWrapping.WrapWholeWords });
        content.Children.Add(games);
        content.Children.Add(NextSteps(T("FirstRun.NextSteps"), services.Appearance.ThemeKey));
        var dialog = new ContentDialog
        {
            Title = T("FirstRun.Title"), Content = content, XamlRoot = services.Dialogs.Root, RequestedTheme = theme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            PrimaryButtonText = T("FirstRun.Use"), CloseButtonText = T("FirstRun.Later"),
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false
        };
        games.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = games.SelectedItem is not null;
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return presets.FirstOrDefault(preset => preset.Id == (games.SelectedItem as GridViewItem)?.Tag as string);
    }

    // One game as a card with an icon, all the same size. The card colour is the app's theme token for the
    // dialog's theme; hover and selection are the grid's own.
    private static GridViewItem GameCard(string name, string theme)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon { Glyph = "\uE7FC", FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.WrapWholeWords, MaxWidth = 176,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        return new GridViewItem
        {
            Width = 236,
            Height = 56,
            Padding = new Thickness(16, 0, 12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = ThemeTokens.Get("AppCardBackgroundBrush", theme),
            BorderBrush = ThemeTokens.Get("AppCardBorderBrush", theme),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Content = row
        };
    }

    // What happens after the game: the paths and Mod management, which the dashboard's tips point at.
    private static Border NextSteps(string text, string theme)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = "\uE82F", FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.WrapWholeWords, Style = (Style)Application.Current.Resources["MutedTextStyle"] };
        Grid.SetColumn(note, 1);
        row.Children.Add(note);
        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Background = ThemeTokens.Get("AppInsetBackgroundBrush", theme),
            Child = row
        };
    }

}
