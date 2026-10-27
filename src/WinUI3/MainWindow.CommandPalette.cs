using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private const int CommandPaletteLimit = 40;
    private Grid? _commandPaletteOverlay;
    private Border? _commandPalettePanel;
    private TextBox? _commandPaletteSearch;
    private ListView? _commandPaletteList;
    private TextBlock? _commandPaletteEmpty;
    private TextBlock? _commandPaletteHint;
    private CommandPaletteEntry[] _commandPaletteEntries = [];
    private IReadOnlyList<CommandPaletteEntry> _commandPaletteResults = [];

    // Ctrl+K: one search over pages, commands, characters and every Mod in the current repository.
    // Picking a row only navigates or selects; deploying stays on the Deploy button.
    private void OpenCommandPalette()
    {
        EnsureCommandPalette();
        _commandPaletteEntries = BuildCommandPaletteEntries();
        _commandPalettePanel!.Background = GetAppThemeBrush("AppElevatedBackgroundBrush");
        _commandPalettePanel.BorderBrush = GetAppThemeBrush("AppStrongBorderBrush");
        _commandPaletteSearch!.PlaceholderText = T("CommandPalette.Placeholder");
        AutomationProperties.SetName(_commandPaletteSearch, T("CommandPalette.Placeholder"));
        _commandPaletteHint!.Text = T("CommandPalette.Hint");
        _commandPaletteEmpty!.Text = T("CommandPalette.NoResults");
        _commandPaletteSearch.Text = string.Empty;
        RefreshCommandPaletteResults();
        _commandPaletteOverlay!.Visibility = Visibility.Visible;
        _commandPaletteSearch.Focus(FocusState.Programmatic);
    }

    // An accelerator works wherever focus is in the window, unlike the root KeyDown handler.
    private void InitializeCommandPaletteShortcut()
    {
        var shortcut = new KeyboardAccelerator { Key = VirtualKey.K, Modifiers = VirtualKeyModifiers.Control };
        shortcut.Invoked += (_, args) =>
        {
            args.Handled = true;
            if (IsCommandPaletteOpen) CloseCommandPalette();
            else OpenCommandPalette();
        };
        RootGrid.KeyboardAccelerators.Add(shortcut);
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private void CloseCommandPalette()
    {
        if (_commandPaletteOverlay is null || _commandPaletteOverlay.Visibility != Visibility.Visible) return;
        _commandPaletteOverlay.Visibility = Visibility.Collapsed;
        _commandPaletteEntries = [];
        _commandPaletteResults = [];
        _commandPaletteList!.Items.Clear();
        // The search box just disappeared; give focus back so window shortcuts keep working.
        RootGrid.Focus(FocusState.Programmatic);
    }

    private bool IsCommandPaletteOpen => _commandPaletteOverlay?.Visibility == Visibility.Visible;

    private void EnsureCommandPalette()
    {
        if (_commandPaletteOverlay is not null) return;
        _commandPaletteSearch = new TextBox { FontSize = 16, MinHeight = 40, Padding = new Thickness(12, 9, 12, 9) };
        _commandPaletteSearch.TextChanged += (_, _) => RefreshCommandPaletteResults();
        _commandPaletteSearch.KeyDown += OnCommandPaletteKeyDown;
        _commandPaletteList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
            MaxHeight = 420, Margin = new Thickness(0, 6, 0, 0)
        };
        _commandPaletteList.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is FrameworkElement { Tag: CommandPaletteEntry entry }) RunCommandPaletteEntry(entry);
        };
        _commandPaletteEmpty = new TextBlock { Margin = new Thickness(12, 16, 12, 16), Opacity = 0.75, Visibility = Visibility.Collapsed };
        _commandPaletteHint = new TextBlock { FontSize = 12, Opacity = 0.7, Margin = new Thickness(4, 8, 4, 0) };
        var body = new StackPanel();
        body.Children.Add(_commandPaletteSearch);
        body.Children.Add(_commandPaletteList);
        body.Children.Add(_commandPaletteEmpty);
        body.Children.Add(_commandPaletteHint);
        _commandPalettePanel = new Border
        {
            Child = body, Width = 640, Padding = new Thickness(12), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 72, 16, 16)
        };
        // Clicks on the panel stay inside; clicks on the dimmed area close the palette.
        _commandPalettePanel.Tapped += (_, args) => args.Handled = true;
        _commandPaletteOverlay = new Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(110, 0, 0, 0)),
            Visibility = Visibility.Collapsed
        };
        _commandPaletteOverlay.Tapped += (_, _) => CloseCommandPalette();
        _commandPaletteOverlay.Children.Add(_commandPalettePanel);
        Canvas.SetZIndex(_commandPaletteOverlay, 1000);
        RootGrid.Children.Add(_commandPaletteOverlay);
    }

    private CommandPaletteEntry[] BuildCommandPaletteEntries()
    {
        string page = T("CommandPalette.OpenPage");
        string command = T("CommandPalette.Command");
        var entries = new List<CommandPaletteEntry>
        {
            new(CommandPaletteKind.Command, T("Shell.Dashboard"), page, "nav:" + PrimarySection.Dashboard),
            new(CommandPaletteKind.Command, T("Shell.Repositories"), page, "nav:" + PrimarySection.Repository),
            new(CommandPaletteKind.Command, T("Shell.OnlineMods"), page, "nav:" + PrimarySection.Online),
            new(CommandPaletteKind.Command, T("Shell.Updates"), page, "nav:" + PrimarySection.Updates),
            new(CommandPaletteKind.Command, T("Common.ModShortcuts"), page, "nav:" + PrimarySection.Shortcuts),
            new(CommandPaletteKind.Command, T("Shell.Settings"), page, "nav:" + PrimarySection.Settings),
            new(CommandPaletteKind.Command, T("Localization.ImportMod"), command, "cmd:import"),
            new(CommandPaletteKind.Command, T("CommandPalette.RefreshRepository"), command, "cmd:refresh")
        };
        foreach (FirstLevelFolderItem character in _characters.All)
            entries.Add(new(CommandPaletteKind.Character, character.Name,
                T("CommandPalette.CharacterMods", character.Children.Count), "char:" + character.Path));
        foreach (FirstLevelFolderItem character in _characters.All)
            foreach (SecondLevelFolderItem mod in character.Children)
                entries.Add(new(CommandPaletteKind.Mod, mod.Name,
                    string.IsNullOrWhiteSpace(mod.State) ? character.Name : character.Name + " · " + mod.State, "mod:" + mod.Path));
        return [.. entries];
    }

    private void RefreshCommandPaletteResults()
    {
        if (_commandPaletteList is null) return;
        _commandPaletteResults = CommandPaletteSearch.Rank(_commandPaletteEntries, _commandPaletteSearch!.Text, CommandPaletteLimit);
        _commandPaletteList.Items.Clear();
        foreach (CommandPaletteEntry entry in _commandPaletteResults)
            _commandPaletteList.Items.Add(CreateCommandPaletteRow(entry));
        _commandPaletteList.SelectedIndex = _commandPaletteResults.Count > 0 ? 0 : -1;
        _commandPaletteList.Visibility = _commandPaletteResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _commandPaletteEmpty!.Visibility = _commandPaletteResults.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static FrameworkElement CreateCommandPaletteRow(CommandPaletteEntry entry)
    {
        string glyph = entry.Kind switch
        {
            CommandPaletteKind.Character => "",
            CommandPaletteKind.Mod => "",
            _ => entry.Key.StartsWith("nav:", StringComparison.Ordinal) ? "" : ""
        };
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(2, 6, 2, 6), Tag = entry };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = entry.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = entry.Detail, FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        AutomationProperties.SetName(row, entry.Title + ", " + entry.Detail);
        return row;
    }

    // Handle every key the root window also reacts to, so Enter can never reach its
    // "deploy the selected Mod" shortcut while the palette is open.
    private void OnCommandPaletteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                CloseCommandPalette();
                e.Handled = true;
                break;
            case VirtualKey.Down or VirtualKey.Up when _commandPaletteResults.Count > 0:
                int step = e.Key == VirtualKey.Down ? 1 : -1;
                int count = _commandPaletteResults.Count;
                _commandPaletteList!.SelectedIndex = ((_commandPaletteList.SelectedIndex + step) % count + count) % count;
                _commandPaletteList.ScrollIntoView(_commandPaletteList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                if (_commandPaletteList!.SelectedItem is FrameworkElement { Tag: CommandPaletteEntry entry }) RunCommandPaletteEntry(entry);
                e.Handled = true;
                break;
        }
    }

    private async void RunCommandPaletteEntry(CommandPaletteEntry entry)
    {
        try
        {
            CloseCommandPalette();
            int split = entry.Key.IndexOf(':');
            string kind = entry.Key[..split], value = entry.Key[(split + 1)..];
            switch (kind)
            {
                case "nav" when Enum.TryParse(value, out PrimarySection section):
                    NavigateToPrimarySection(section);
                    break;
                case "cmd" when value == "import":
                    NavigateToPrimarySection(PrimarySection.Repository);
                    OnImportZipClicked(ImportZipButton, new RoutedEventArgs());
                    break;
                case "cmd" when value == "refresh":
                    NavigateToPrimarySection(PrimarySection.Repository);
                    await RefreshListsAsync();
                    break;
                case "char":
                    ShowRepositoryItemFromPalette(value, modPath: null);
                    break;
                case "mod":
                    ShowRepositoryItemFromPalette(Path.GetDirectoryName(value), value);
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("RunCommandPaletteEntry", ex);
        }
    }

    // Clears the character and Mod searches first, so the picked row is not hidden by a filter.
    private void ShowRepositoryItemFromPalette(string? characterPath, string? modPath)
    {
        // Opening the page without a rescan: the palette searched the lists already in memory, and a
        // rescan would finish later and restore the previous selection over this one.
        if (_currentPrimarySection != PrimarySection.Repository)
        {
            _currentPrimarySection = PrimarySection.Repository;
            ApplyShellState(refreshRepository: false);
        }
        if (_repositoryWorkspaceBusy)
        {
            StatusTextBlock.Text = T("CommandPalette.Busy");
            return;
        }
        // TextChanged is raised later, so apply each cleared filter right away as well.
        if (!string.IsNullOrEmpty(FirstLevelSearchTextBox.Text))
        {
            FirstLevelSearchTextBox.Text = string.Empty;
            ApplyFirstLevelFilter();
        }
        if (_repositoryModSearch is not null && !string.IsNullOrEmpty(_repositoryModSearch.Text))
        {
            _repositoryModSearch.Text = string.Empty;
            RefreshRepositoryModView();
        }
        if (_modList.View == RepositoryWorkspaceView.Presets) SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView.List);
        if (modPath is not null) SelectSecondLevelByPath(modPath);
        else SelectFirstLevelByPath(characterPath);
    }
}
