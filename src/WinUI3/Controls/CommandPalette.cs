using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Ctrl+K (stage J finish, step 8c, moved from the window unchanged): one search over pages, commands, characters and
/// every Mod in the current repository, drawn over the dimmed window. Picking a row only navigates or selects;
/// deploying stays on the Deploy button. The window gives it its root, the title bar search box and what each row does.
/// </summary>
internal sealed class CommandPalette(AppServices services, Grid root, FrameworkElement titleBarSearch, Func<string, Brush> themeBrush,
    Action<PrimarySection> navigate, Action importMod, Func<Task> refresh, Action openDeveloperTools, Action<string?, string?> showItem,
    Action<string, Exception> reportFailure)
{
    private readonly AppServices _services = services;
    private readonly Grid _root = root;
    private readonly FrameworkElement _titleBarSearch = titleBarSearch;
    private readonly Func<string, Brush> _themeBrush = themeBrush;
    private readonly Action<PrimarySection> _navigate = navigate;
    private readonly Action _importMod = importMod;
    private readonly Func<Task> _refresh = refresh;
    private readonly Action _openDeveloperTools = openDeveloperTools;
    private readonly Action<string?, string?> _showItem = showItem;
    private readonly Action<string, Exception> _reportFailure = reportFailure;

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private const int CommandPaletteLimit = 40;
    private Grid? _commandPaletteOverlay;
    private Border? _commandPalettePanel;
    private TextBox? _commandPaletteSearch;
    private ListView? _commandPaletteList;
    private TextBlock? _commandPaletteEmpty;
    private TextBlock? _commandPaletteHint;
    private CommandPaletteEntry[] _commandPaletteEntries = [];
    private IReadOnlyList<CommandPaletteEntry> _commandPaletteResults = [];
    // Where each result sits in the list, which also holds the group headings (J8).
    private int[] _commandPaletteRowIndexes = [];
    private int _commandPaletteCursor = -1;
    // Where focus was before the palette opened; it goes back there when the palette closes.
    private Control? _focusBeforeCommandPalette;

    // Ctrl+K: one search over pages, commands, characters and every Mod in the current repository.
    // Picking a row only navigates or selects; deploying stays on the Deploy button.
    public void Open()
    {
        EnsureCommandPalette();
        if (IsOpen) return;
        // The title bar search box only opens the palette; returning focus to it would open it again.
        _focusBeforeCommandPalette = FocusManager.GetFocusedElement(_root.XamlRoot) is Control focused
            && !IsInside(focused, _titleBarSearch) ? focused : null;
        _commandPaletteEntries = BuildCommandPaletteEntries();
        // An in-app Acrylic surface over a dimmed window, like other Windows flyouts.
        _commandPalettePanel!.Background = _themeBrush("AppPaletteBackgroundBrush");
        _commandPalettePanel.BorderBrush = _themeBrush("AppStrongBorderBrush");
        _commandPaletteOverlay!.Background = _themeBrush("AppSmokeBrush");
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
    public void InitializeShortcut()
    {
        var shortcut = new KeyboardAccelerator { Key = VirtualKey.K, Modifiers = VirtualKeyModifiers.Control };
        shortcut.Invoked += (_, args) =>
        {
            args.Handled = true;
            if (IsOpen) Close();
            else Open();
        };
        _root.KeyboardAccelerators.Add(shortcut);
        _root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    public void Close()
    {
        if (_commandPaletteOverlay is null || _commandPaletteOverlay.Visibility != Visibility.Visible) return;
        _commandPaletteOverlay.Visibility = Visibility.Collapsed;
        _commandPaletteEntries = [];
        _commandPaletteResults = [];
        _commandPaletteList!.Items.Clear();
        // Back to where the user was; otherwise to the window, so window shortcuts keep working.
        Control? previous = _focusBeforeCommandPalette;
        _focusBeforeCommandPalette = null;
        if (previous is null || previous.XamlRoot is null || !previous.Focus(FocusState.Programmatic))
            _root.Focus(FocusState.Programmatic);
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (DependencyObject? current = element; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, container)) return true;
        return false;
    }

    public bool IsOpen => _commandPaletteOverlay?.Visibility == Visibility.Visible;

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
            Child = body, Width = 640, Padding = new Thickness(12), CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 72, 16, 16)
        };
        // Clicks on the panel stay inside; clicks on the dimmed area close the palette.
        _commandPalettePanel.Tapped += (_, args) => args.Handled = true;
        _commandPaletteOverlay = new Grid { Visibility = Visibility.Collapsed };
        _commandPaletteOverlay.Tapped += (_, _) => Close();
        // The panel floats above the dimmed window with the system's flyout shadow (J8). Outside a popup a
        // ThemeShadow needs a receiver to fall on: a transparent layer under the panel that fills the overlay.
        var shadowReceiver = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var shadow = new ThemeShadow();
        shadow.Receivers.Add(shadowReceiver);
        _commandPalettePanel.Shadow = shadow;
        _commandPalettePanel.Translation = new System.Numerics.Vector3(0, 0, 32);
        _commandPaletteOverlay.Children.Add(shadowReceiver);
        _commandPaletteOverlay.Children.Add(_commandPalettePanel);
        Canvas.SetZIndex(_commandPaletteOverlay, 1000);
        _root.Children.Add(_commandPaletteOverlay);
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
            new(CommandPaletteKind.Command, T("CommandPalette.RefreshRepository"), command, "cmd:refresh"),
            new(CommandPaletteKind.Command, T("CommandPalette.OpenDeveloperTools"), command, "cmd:devtools")
        };
        foreach (FirstLevelFolderItem character in _services.Workspace.Characters.All)
            entries.Add(new(CommandPaletteKind.Character, character.Name,
                T("CommandPalette.CharacterMods", character.Children.Count), "char:" + character.Path));
        foreach (FirstLevelFolderItem character in _services.Workspace.Characters.All)
            foreach (SecondLevelFolderItem mod in character.Children)
                entries.Add(new(CommandPaletteKind.Mod, mod.Name,
                    string.IsNullOrWhiteSpace(mod.State) ? character.Name : character.Name + " · " + mod.State, "mod:" + mod.Path));
        return [.. entries];
    }

    private void RefreshCommandPaletteResults()
    {
        if (_commandPaletteList is null) return;
        IReadOnlyList<CommandPaletteEntry> ranked = CommandPaletteSearch.Rank(_commandPaletteEntries, _commandPaletteSearch!.Text, CommandPaletteLimit);
        // Grouped by type like the Windows search flyout; the ranking holds inside each group (J8).
        var groups = ranked.GroupBy(CommandPaletteGroup).OrderBy(group => group.Key).ToArray();
        _commandPaletteResults = [.. groups.SelectMany(group => group)];
        _commandPaletteList.Items.Clear();
        var rowIndexes = new List<int>();
        foreach (var group in groups)
        {
            _commandPaletteList.Items.Add(CreateCommandPaletteHeader(group.Key switch
            {
                0 => T("CommandPalette.GroupPages"),
                1 => T("CommandPalette.GroupCharacters"),
                2 => T("CommandPalette.GroupMods"),
                _ => T("CommandPalette.GroupCommands")
            }));
            foreach (CommandPaletteEntry entry in group)
            {
                rowIndexes.Add(_commandPaletteList.Items.Count);
                _commandPaletteList.Items.Add(CreateCommandPaletteRow(entry));
            }
        }
        _commandPaletteRowIndexes = [.. rowIndexes];
        SelectCommandPaletteResult(_commandPaletteResults.Count > 0 ? 0 : -1);
        _commandPaletteList.Visibility = _commandPaletteResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _commandPaletteEmpty!.Visibility = _commandPaletteResults.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // 0 pages, 1 characters, 2 Mods, 3 commands.
    private static int CommandPaletteGroup(CommandPaletteEntry entry) => entry.Kind switch
    {
        CommandPaletteKind.Character => 1,
        CommandPaletteKind.Mod => 2,
        _ => entry.Key.StartsWith("nav:", StringComparison.Ordinal) ? 0 : 3
    };

    // A group heading: skipped by the arrow keys and not hit-testable (a disabled item would dim its text).
    private static ListViewItem CreateCommandPaletteHeader(string text) => new()
    {
        Content = new TextBlock
        {
            Text = text, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], Margin = new Thickness(2, 6, 0, 0)
        },
        IsHitTestVisible = false, IsTabStop = false, MinHeight = 28
    };

    private void SelectCommandPaletteResult(int index)
    {
        _commandPaletteCursor = index;
        _commandPaletteList!.SelectedIndex = index < 0 ? -1 : _commandPaletteRowIndexes[index];
        if (_commandPaletteList.SelectedItem is not null) _commandPaletteList.ScrollIntoView(_commandPaletteList.SelectedItem);
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
                Close();
                e.Handled = true;
                break;
            case VirtualKey.Down or VirtualKey.Up when _commandPaletteResults.Count > 0:
                int step = e.Key == VirtualKey.Down ? 1 : -1;
                int count = _commandPaletteResults.Count;
                SelectCommandPaletteResult(((_commandPaletteCursor + step) % count + count) % count);
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
            Close();
            int split = entry.Key.IndexOf(':');
            string kind = entry.Key[..split], value = entry.Key[(split + 1)..];
            switch (kind)
            {
                case "nav" when Enum.TryParse(value, out PrimarySection section):
                    _navigate(section);
                    break;
                case "cmd" when value == "import":
                    _navigate(PrimarySection.Repository);
                    _importMod();
                    break;
                case "cmd" when value == "refresh":
                    _navigate(PrimarySection.Repository);
                    await _refresh();
                    break;
                case "cmd" when value == "devtools":
                    _openDeveloperTools();
                    break;
                case "char":
                    _showItem(value, null);
                    break;
                case "mod":
                    _showItem(Path.GetDirectoryName(value), value);
                    break;
            }
        }
        catch (Exception ex)
        {
            _reportFailure("RunCommandPaletteEntry", ex);
        }
    }
}
