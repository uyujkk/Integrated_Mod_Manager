using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Ctrl+K (stage J finish, step 8c, moved from the window unchanged): one search over pages, commands, every feature
/// and where it is, characters and every Mod in the current repository, drawn over the dimmed window. Picking a row only
/// navigates, selects or points at a place; deploying stays on the Deploy button. The window gives it its root, the
/// title bar search box and what each row does.
/// In the detailed style (the default; Settings › Appearance and Language › Search style) the empty search is a start
/// screen like Windows 8's: it fills the window with one group of tiles per area (Settings has its own page and is
/// found by typing), the main features as wide tiles, scrolling sideways; pointing at a tile says where the feature is,
/// and typing turns it into the list of matches. The brief
/// style lists pages and commands and shows features only as matches.
/// </summary>
internal sealed class CommandPalette(AppServices services, Grid root, FrameworkElement titleBarSearch, Func<string, Brush> themeBrush,
    Action<PrimarySection> navigate, Action importMod, Func<Task> refresh, Action openDeveloperTools, Action<string?, string?> showItem,
    Action<string, Exception> reportFailure, Func<bool> detailed, Action<string> runFeature, Action opened)
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
    private readonly Func<bool> _detailedStyle = detailed;
    private readonly Action<string> _runFeature = runFeature;
    private readonly Action _opened = opened;
    // Read when the palette opens; a change in Settings applies the next time.
    private bool _detailed;
    private readonly Dictionary<string, string> _featureGlyphs = new(StringComparer.Ordinal);
    // The detailed style's start screen.
    private ScrollViewer? _tileScroller;
    private StackPanel? _tileGroups;
    private TextBlock? _tileWhere, _tileTitle;
    private bool _tilesShown;
    // The tiles are kept between opens and built again only for another language, theme or window height.
    private string? _tilesKey;
    private Button? _firstTile;
    private readonly HashSet<string> _wideFeatures = new(StringComparer.Ordinal);
    private const double TileSize = 150, TileHeight = 120, TileGap = 8;

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private const int CommandPaletteLimit = 40;
    // The detailed style shows more matches: a word such as "folder" fits many features.
    private const int DetailedLimit = 120;
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
        _detailed = _detailedStyle();
        _commandPaletteEntries = BuildCommandPaletteEntries();
        // An in-app Acrylic surface over a dimmed window, like other Windows flyouts.
        _commandPalettePanel!.Background = _themeBrush("AppPaletteBackgroundBrush");
        _commandPalettePanel.BorderBrush = _themeBrush("AppStrongBorderBrush");
        _commandPaletteOverlay!.Background = _themeBrush("AppSmokeBrush");
        _commandPaletteSearch!.PlaceholderText = T("CommandPalette.Placeholder");
        AutomationProperties.SetName(_commandPaletteSearch, T("CommandPalette.Placeholder"));
        _commandPaletteHint!.Text = T("CommandPalette.Hint") + "  ·  " + (_detailed ? T("CommandPalette.DetailedHint") : T("CommandPalette.BriefHint"));
        // The list of matches needs room for the place each feature is in.
        _commandPaletteList!.MaxHeight = _detailed ? 560 : 420;
        _commandPaletteEmpty!.Text = T("CommandPalette.NoResults");
        _commandPaletteSearch.Text = string.Empty;
        RefreshCommandPaletteResults();
        _commandPaletteOverlay!.Visibility = Visibility.Visible;
        if (_tilesShown) AnimateTilesIn();
        _commandPaletteSearch.Focus(FocusState.Programmatic);
        _opened();
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
        _commandPaletteHint = new TextBlock { FontSize = 12, Opacity = 0.7, Margin = new Thickness(4, 8, 4, 0), TextWrapping = TextWrapping.Wrap };
        // The start screen: groups side by side that scroll sideways (the wheel too), arrow keys move between tiles,
        // and typing on a tile goes on in the search box.
        _tileGroups = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled };
        _tileGroups.CharacterReceived += OnTileCharacterReceived;
        _tileScroller = new ScrollViewer
        {
            Content = _tileGroups, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled,
            Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(4, 4, 4, 16), Visibility = Visibility.Collapsed
        };
        _tileScroller.PointerWheelChanged += (_, args) =>
        {
            int delta = args.GetCurrentPoint(_tileScroller).Properties.MouseWheelDelta;
            _tileScroller.ChangeView(_tileScroller.HorizontalOffset - delta, null, null);
            args.Handled = true;
        };
        AutomationProperties.SetAutomationId(_tileScroller, "FeatureTiles");
        _tileWhere = new TextBlock { FontSize = 13, Margin = new Thickness(4, 8, 4, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(_tileWhere, "FeatureTileWhere");
        // The start screen's big title, beside the search box like Windows 8's "Start".
        _tileTitle = new TextBlock
        {
            FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.Light, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0), Visibility = Visibility.Collapsed
        };
        var body = new Grid();
        body.Children.Add(_tileTitle);
        foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            body.RowDefinitions.Add(new RowDefinition { Height = height });
        UIElement[] rows = [_commandPaletteSearch, _commandPaletteList, _commandPaletteEmpty, _tileScroller, _tileWhere, _commandPaletteHint];
        for (int row = 0; row < rows.Length; row++)
        {
            Grid.SetRow((FrameworkElement)rows[row], row);
            body.Children.Add(rows[row]);
        }
        _commandPalettePanel = new Border
        {
            Child = body, Width = 640, MaxWidth = 2000, Padding = new Thickness(12), CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 72, 16, 16)
        };
        // Clicks on the panel stay inside; clicks on the dimmed area close the palette.
        _commandPalettePanel.Tapped += (_, args) => args.Handled = true;
        // Esc from a tile closes too (the search box handles its own keys).
        _commandPalettePanel.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape || args.Handled) return;
            Close();
            args.Handled = true;
        };
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
        // Every feature with where it is (FeatureCatalog); found by its name, its place or another word for it.
        _featureGlyphs.Clear();
        foreach (FeatureEntry feature in FeatureCatalog.Build(T))
        {
            entries.Add(feature.ToPaletteEntry());
            _featureGlyphs[feature.Id] = feature.Glyph;
            if (feature.Wide) _wideFeatures.Add(feature.Id);
        }
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
        string query = _commandPaletteSearch!.Text;
        // Detailed and nothing typed: the start screen of tiles instead of a list.
        bool tiles = _detailed && string.IsNullOrWhiteSpace(query);
        ShowTileScreen(tiles);
        if (tiles)
        {
            _commandPaletteResults = [];
            _commandPaletteRowIndexes = [];
            _commandPaletteCursor = -1;
            _commandPaletteList.Items.Clear();
            _commandPaletteList.Visibility = Visibility.Collapsed;
            _commandPaletteEmpty!.Visibility = Visibility.Collapsed;
            string key = string.Join("|", _services.Language.Current, _services.Appearance.ThemeKey, TileRows());
            if (key != _tilesKey)
            {
                _tilesKey = key;
                BuildTiles(CommandPaletteSearch.Directory(_commandPaletteEntries));
            }
            // When the palette opens, Open starts the slide-in once the panel is on screen.
            if (IsOpen) AnimateTilesIn();
            return;
        }
        // Detailed and nothing typed: the whole feature directory, area by area.
        IReadOnlyList<CommandPaletteEntry> ranked = _detailed && string.IsNullOrWhiteSpace(query)
            ? CommandPaletteSearch.Directory(_commandPaletteEntries)
            : CommandPaletteSearch.Rank(_commandPaletteEntries, query, _detailed ? DetailedLimit : CommandPaletteLimit);
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
                FeatureGroup => T("CommandPalette.GroupFeatures"),
                > FeatureGroup and < CharacterGroup => FeatureCatalog.AreaName(group.Key - FeatureGroup, T),
                CharacterGroup => T("CommandPalette.GroupCharacters"),
                ModGroup => T("CommandPalette.GroupMods"),
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

    // Pages, then features (one group in the brief style, one per area in the detailed style), characters, Mods and
    // commands.
    private const int FeatureGroup = 10, CharacterGroup = 20, ModGroup = 30, CommandGroup = 40;

    private int CommandPaletteGroup(CommandPaletteEntry entry) => entry.Kind switch
    {
        CommandPaletteKind.Feature => _detailed ? FeatureGroup + Math.Clamp(entry.Area, 1, FeatureCatalog.AreaCount) : FeatureGroup,
        CommandPaletteKind.Character => CharacterGroup,
        CommandPaletteKind.Mod => ModGroup,
        _ => entry.Key.StartsWith("nav:", StringComparison.Ordinal) ? 0 : CommandGroup
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

    private FrameworkElement CreateCommandPaletteRow(CommandPaletteEntry entry)
    {
        if (entry.Kind == CommandPaletteKind.Feature) return CreateFeatureRow(entry);
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

    // The start screen fills the window; the list is a centred panel at the top.
    private void ShowTileScreen(bool tiles)
    {
        _tilesShown = tiles;
        _tileScroller!.Visibility = _tileWhere!.Visibility = _tileTitle!.Visibility = tiles ? Visibility.Visible : Visibility.Collapsed;
        _tileTitle.Text = T("CommandPalette.TileTitle");
        _tileWhere.Text = T("CommandPalette.TileHint");
        _commandPalettePanel!.Width = tiles ? double.NaN : _detailed ? 820 : 640;
        _commandPalettePanel.HorizontalAlignment = tiles ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        _commandPalettePanel.VerticalAlignment = tiles ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        _commandPalettePanel.Margin = tiles ? new Thickness(24, 56, 24, 24) : new Thickness(16, 72, 16, 16);
        _commandPaletteSearch!.Width = tiles ? 420 : double.NaN;
        _commandPaletteSearch.HorizontalAlignment = tiles ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        _commandPalettePanel.Padding = new Thickness(tiles ? 24 : 12);
    }

    // Each area has its own tile colour (white text keeps a contrast of 4.5:1 or more); high contrast keeps system colours.
    private static readonly Windows.UI.Color[] AreaColors =
    [
        Microsoft.UI.ColorHelper.FromArgb(255, 0x00, 0x63, 0xB1), Microsoft.UI.ColorHelper.FromArgb(255, 0x00, 0x6E, 0x75),
        Microsoft.UI.ColorHelper.FromArgb(255, 0x5B, 0x3F, 0xB8), Microsoft.UI.ColorHelper.FromArgb(255, 0x10, 0x7C, 0x10),
        Microsoft.UI.ColorHelper.FromArgb(255, 0xB4, 0x00, 0x9E), Microsoft.UI.ColorHelper.FromArgb(255, 0x8E, 0x56, 0x2E),
        Microsoft.UI.ColorHelper.FromArgb(255, 0x4C, 0x4A, 0x48)
    ];

    // Rows of tiles that fit: the window less the panel's margins and padding, the title row, the group heading and
    // the lines under the tiles.
    private int TileRows() => Math.Clamp((int)((_root.ActualHeight - 56 - 24 - 48 - 56 - 16 - 40 - 70) / (TileHeight + TileGap)), 2, 6);

    private static Windows.UI.Color Shade(Windows.UI.Color color, double factor)
        => Microsoft.UI.ColorHelper.FromArgb(255, (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

    // Windows 8's groups: blocks two square tiles wide, filled row by row; a wide tile takes a whole row of its block,
    // and a full block starts the next one to the right. The main features come first, as wide tiles.
    private void BuildTiles(IReadOnlyList<CommandPaletteEntry> features)
    {
        _tileGroups!.Children.Clear();
        _firstTile = null;
        bool highContrast = string.Equals(_services.Appearance.ThemeKey, "HighContrast", StringComparison.Ordinal);
        int rows = TileRows();
        foreach (var area in features.Where(feature => feature.Area != FeatureCatalog.SettingsArea)
                     .GroupBy(feature => Math.Clamp(feature.Area, 1, FeatureCatalog.AreaCount)))
        {
            var group = new StackPanel { Spacing = 12 };
            var heading = new TextBlock
            {
                Text = FeatureCatalog.AreaName(area.Key, T), FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiLight,
                Margin = new Thickness(2, 0, 0, 0)
            };
            group.Children.Add(heading);
            var grid = new Grid { RowSpacing = TileGap, ColumnSpacing = TileGap };
            for (int row = 0; row < rows; row++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileHeight) });
            Windows.UI.Color? baseColor = highContrast ? null : AreaColors[(area.Key - 1) % AreaColors.Length];
            CommandPaletteEntry[] ordered = [.. area.Where(IsWide), .. area.Where(entry => !IsWide(entry))];
            int block = 0, cell = 0, index = 0;
            foreach (CommandPaletteEntry entry in ordered)
            {
                bool wide = IsWide(entry);
                if (wide && cell % 2 == 1) cell++;
                if (cell >= rows * 2) { block++; cell = 0; }
                while (grid.ColumnDefinitions.Count < (block + 1) * 2)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TileSize) });
                Button tile = CreateTile(entry, wide, baseColor is { } color ? Shade(color, (index % 3) switch { 0 => 1.0, 1 => 0.88, _ => 0.94 }) : null);
                Grid.SetRow(tile, cell / 2);
                Grid.SetColumn(tile, block * 2 + cell % 2);
                if (wide) Grid.SetColumnSpan(tile, 2);
                grid.Children.Add(tile);
                _firstTile ??= tile;
                cell += wide ? 2 : 1;
                index++;
            }
            group.Children.Add(grid);
            _tileGroups.Children.Add(group);
        }
    }

    private bool IsWide(CommandPaletteEntry entry) => _wideFeatures.Contains(entry.Key["feature:".Length..]);

    private Button CreateTile(CommandPaletteEntry entry, bool wide, Windows.UI.Color? color)
    {
        string id = entry.Key["feature:".Length..];
        Brush? text = color is null ? null : new SolidColorBrush(Microsoft.UI.Colors.White);
        var face = new Grid { Padding = new Thickness(12, 10, 12, 10) };
        if (color is { } fill)
        {
            // A light diagonal gradient, like a lit tile.
            var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop { Color = fill, Offset = 0 });
            gradient.GradientStops.Add(new GradientStop { Color = Shade(fill, 0.8), Offset = 1 });
            face.Background = gradient;
        }
        face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        face.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var icon = new FontIcon
        {
            Glyph = _featureGlyphs.TryGetValue(id, out string? glyph) ? glyph : ((char)0xE8FD).ToString(),
            FontSize = wide ? 30 : 24, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        if (text is not null) icon.Foreground = text;
        face.Children.Add(icon);
        if (entry.Shortcut.Length > 0)
        {
            var key = new Border
            {
                Padding = new Thickness(5, 0, 5, 1), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1),
                BorderBrush = text ?? _themeBrush("AppStrongBorderBrush"),
                Child = new TextBlock { Text = entry.Shortcut, FontSize = 11, Foreground = text }
            };
            if (text is null) ((TextBlock)key.Child).ClearValue(TextBlock.ForegroundProperty);
            face.Children.Add(key);
        }
        // Wide tiles also say where the feature is; square ones keep it for the line under the tiles.
        if (wide)
        {
            var place = new TextBlock
            {
                Text = entry.Detail, FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2)
            };
            if (text is not null) place.Foreground = text;
            Grid.SetRow(place, 1);
            face.Children.Add(place);
        }
        var title = new TextBlock
        {
            Text = entry.Title, FontSize = wide ? 15 : 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, MaxLines = wide ? 1 : 3, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = wide ? 20 : 17
        };
        if (text is not null) title.Foreground = text;
        Grid.SetRow(title, 2);
        face.Children.Add(title);
        // Pointing at a tile draws a light frame around it, as on the start screen.
        var frame = new Border
        {
            BorderThickness = new Thickness(2), BorderBrush = text ?? _themeBrush("AppStrongBorderBrush"), Opacity = 0,
            Margin = new Thickness(-12, -10, -12, -10), IsHitTestVisible = false
        };
        Grid.SetRowSpan(frame, 3);
        face.Children.Add(frame);
        var tile = new Button
        {
            Content = face, Width = wide ? TileSize * 2 + TileGap : TileSize, Height = TileHeight, Padding = new Thickness(0),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, Tag = entry, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform()
        };
        string where = T("CommandPalette.TileWhere", entry.Title, entry.Detail);
        ToolTipService.SetToolTip(tile, entry.Detail);
        AutomationProperties.SetAutomationId(tile, "Feature" + id);
        AutomationProperties.SetName(tile, entry.Title + ", " + entry.Detail + (entry.Shortcut.Length > 0 ? ", " + entry.Shortcut : ""));
        tile.Click += (_, _) => RunCommandPaletteEntry(entry);
        // Pointing at a tile or moving to it says where the feature is, under the tiles; pressing tilts it in a little.
        void ShowWhere()
        {
            _tileWhere!.Text = where;
            frame.Opacity = 0.9;
        }
        void Leave() => frame.Opacity = 0;
        void Press(double scale)
        {
            var transform = (ScaleTransform)tile.RenderTransform;
            transform.ScaleX = transform.ScaleY = scale;
        }
        tile.PointerEntered += (_, _) => ShowWhere();
        tile.PointerExited += (_, _) => { Leave(); Press(1); };
        tile.GotFocus += (_, _) => ShowWhere();
        tile.LostFocus += (_, _) => Leave();
        tile.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Press(0.96)), handledEventsToo: true);
        tile.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Press(1)), handledEventsToo: true);
        tile.PointerCaptureLost += (_, _) => Press(1);
        return tile;
    }

    // The groups slide in from the right one after another when the start screen opens, unless motion is reduced.
    private int _tilesAnimation;

    private void AnimateTilesIn()
    {
        int version = ++_tilesAnimation;
        bool still = _services.ShellSettings.ReduceMotion;
        foreach (UIElement group in _tileGroups!.Children)
        {
            group.OpacityTransition = null;
            group.TranslationTransition = null;
            group.Opacity = still ? 1 : 0;
            group.Translation = still ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3(80, 0, 0);
        }
        _tileScroller!.ChangeView(0, null, null, disableAnimation: true);
        if (still) return;
        // One frame later, so the start values are drawn; each later group takes a little longer, which staggers them.
        bool queued = _root.DispatcherQueue.TryEnqueue(() =>
        {
            // A newer start replaces this one; otherwise the tiles always end up shown.
            if (version != _tilesAnimation) return;
            int index = 0;
            foreach (UIElement group in _tileGroups.Children)
            {
                group.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(200 + 60 * index) };
                group.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(260 + 70 * index) };
                group.Opacity = 1;
                group.Translation = System.Numerics.Vector3.Zero;
                index++;
            }
        });
        if (!queued)
        {
            foreach (UIElement group in _tileGroups.Children)
            {
                group.Opacity = 1;
                group.Translation = System.Numerics.Vector3.Zero;
            }
        }
    }

    // Typing on a tile goes on in the search box, as on Windows 8's start screen.
    private void OnTileCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (char.IsControl(args.Character)) return;
        _commandPaletteSearch!.Text += args.Character;
        _commandPaletteSearch.Focus(FocusState.Keyboard);
        _commandPaletteSearch.SelectionStart = _commandPaletteSearch.Text.Length;
        args.Handled = true;
    }

    // A feature: its icon, its name and where it is, and its shortcut on the right. The brief style shows the place
    // only as the area, on one line.
    private FrameworkElement CreateFeatureRow(CommandPaletteEntry entry)
    {
        string id = entry.Key["feature:".Length..];
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(2, 6, 2, 6), Tag = entry };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon
        {
            Glyph = _featureGlyphs.TryGetValue(id, out string? glyph) ? glyph : ((char)0xE8FD).ToString(),
            FontSize = 16, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center
        });
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = entry.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string place = _detailed ? entry.Detail : FeatureCatalog.AreaName(entry.Area, T);
        text.Children.Add(new TextBlock
        {
            Text = place, FontSize = 12, Opacity = 0.7,
            TextWrapping = _detailed ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        if (entry.Shortcut.Length > 0)
        {
            var key = new Border
            {
                Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                BorderBrush = _themeBrush("AppStrongBorderBrush"),
                Background = _themeBrush("AppInsetBackgroundBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = entry.Shortcut, FontSize = 12 }
            };
            Grid.SetColumn(key, 2);
            row.Children.Add(key);
        }
        AutomationProperties.SetName(row, entry.Title + ", " + entry.Detail + (entry.Shortcut.Length > 0 ? ", " + entry.Shortcut : ""));
        AutomationProperties.SetAutomationId(row, "Feature" + id);
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
            case VirtualKey.Down when _tilesShown && _firstTile is not null:
                _firstTile.Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case VirtualKey.Enter when _tilesShown:
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
                case "feature":
                    _runFeature(value);
                    break;
            }
        }
        catch (Exception ex)
        {
            _reportFailure("RunCommandPaletteEntry", ex);
        }
    }
}
