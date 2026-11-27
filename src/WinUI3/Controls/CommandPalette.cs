using IntegratedModManager.Core.Repository;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

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
    // For the opening animation and Tab: the column each tile and heading stands in, counted from the left, and the
    // first tile of every group.
    private readonly List<(UIElement Element, int Column)> _tileColumns = [];
    private readonly List<Button> _groupFirstTiles = [];
    private readonly Dictionary<Button, int> _tileGroupIndex = [];
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
        _commandPalettePanel!.BorderBrush = _themeBrush("AppStrongBorderBrush");
        _commandPaletteSearch!.PlaceholderText = T("CommandPalette.Placeholder");
        AutomationProperties.SetName(_commandPaletteSearch, T("CommandPalette.Placeholder"));
        _commandPaletteHint!.Text = T("CommandPalette.Hint") + "  ·  " + (_detailed ? T("CommandPalette.DetailedHint") : T("CommandPalette.BriefHint"));
        // The list of matches needs room for the place each feature is in.
        _commandPaletteList!.MaxHeight = _detailed ? 560 : 420;
        _commandPaletteEmpty!.Text = T("CommandPalette.NoResults");
        _commandPaletteSearch.Text = string.Empty;
        RefreshCommandPaletteResults();
        _commandPaletteOverlay!.Visibility = Visibility.Visible;
        if (_tilesShown)
        {
            RevealFromBottomLeft();
            AnimateTilesIn();
        }
        _commandPaletteSearch.Focus(FocusState.Programmatic);
        _opened();
    }

    // Builds the start screen while the window is idle, so the first Ctrl+K opens without building some eighty tiles
    // first. The tiles are measured too, which applies their templates; they are drawn only when the search opens.
    public void Prepare()
    {
        if (IsOpen || !_detailedStyle()) return;
        EnsureCommandPalette();
        string key = TilesKey();
        if (key == _tilesKey) return;
        _detailed = true;
        _tilesKey = key;
        BuildTiles(CommandPaletteSearch.Directory(BuildCommandPaletteEntries()));
        _tileGroups!.Measure(new Windows.Foundation.Size(double.PositiveInfinity, Math.Max(_root.ActualHeight, 1)));
    }

    // The tiles depend on the language, the theme and how many rows fit in the window.
    private string TilesKey() => string.Join("|", _services.Language.Current, _services.Appearance.ThemeKey, TileRows());

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
        _tileGroups.PreviewKeyDown += OnTilePreviewKeyDown;
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
            string key = TilesKey();
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
        _commandPalettePanel.Margin = tiles ? new Thickness(0, 48, 0, 0) : new Thickness(16, 72, 16, 16);
        _commandPaletteSearch!.Width = tiles ? 420 : double.NaN;
        _commandPaletteSearch.HorizontalAlignment = tiles ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        _commandPalettePanel.Padding = tiles ? new Thickness(36, 16, 36, 20) : new Thickness(12);
        // The start screen fills the window with a thin acrylic, so the page shows through blurred; its panel has no
        // surface of its own. The list is an acrylic flyout with a shadow over a dimmed window.
        _commandPaletteOverlay!.Background = _themeBrush(tiles ? "AppStartScreenBrush" : "AppSmokeBrush");
        _commandPalettePanel.Background = tiles ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : _themeBrush("AppPaletteBackgroundBrush");
        _commandPalettePanel.BorderThickness = new Thickness(tiles ? 0 : 1);
        _commandPalettePanel.Translation = tiles ? Vector3.Zero : new Vector3(0, 0, 32);
    }

    // Each area has its own colour for the icon squares of its tiles (lightened in the dark theme); high contrast keeps
    // system colours.
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

    // Windows 8's groups: blocks two square tiles wide, filled row by row; a wide tile takes a whole row of its block,
    // and a full block starts the next one to the right. The main features come first, as wide tiles.
    private void BuildTiles(IReadOnlyList<CommandPaletteEntry> features)
    {
        _tileGroups!.Children.Clear();
        _firstTile = null;
        _tileColumns.Clear();
        _groupFirstTiles.Clear();
        _tileGroupIndex.Clear();
        int columnBase = 0;
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
            _tileColumns.Add((heading, columnBase));
            var grid = new Grid { RowSpacing = TileGap, ColumnSpacing = TileGap };
            for (int row = 0; row < rows; row++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileHeight) });
            Windows.UI.Color? areaColor = highContrast ? null : AreaColors[(area.Key - 1) % AreaColors.Length];
            CommandPaletteEntry[] ordered = [.. area.Where(IsWide), .. area.Where(entry => !IsWide(entry))];
            int block = 0, cell = 0, index = 0;
            foreach (CommandPaletteEntry entry in ordered)
            {
                bool wide = IsWide(entry);
                if (wide && cell % 2 == 1) cell++;
                if (cell >= rows * 2) { block++; cell = 0; }
                while (grid.ColumnDefinitions.Count < (block + 1) * 2)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TileSize) });
                Button tile = CreateTile(entry, wide, areaColor);
                Grid.SetRow(tile, cell / 2);
                Grid.SetColumn(tile, block * 2 + cell % 2);
                if (wide) Grid.SetColumnSpan(tile, 2);
                grid.Children.Add(tile);
                _tileColumns.Add((tile, columnBase + block * 2 + cell % 2));
                _tileGroupIndex[tile] = _groupFirstTiles.Count + (index == 0 ? 0 : -1);
                if (index == 0) _groupFirstTiles.Add(tile);
                _firstTile ??= tile;
                cell += wide ? 2 : 1;
                index++;
            }
            group.Children.Add(grid);
            _tileGroups.Children.Add(group);
            // The gap between groups counts as a column, so the next group follows a beat later.
            columnBase += grid.ColumnDefinitions.Count + 1;
        }
    }

    private bool IsWide(CommandPaletteEntry entry) => _wideFeatures.Contains(entry.Key["feature:".Length..]);

    // A Windows 11 card: a light translucent surface with a hairline border, the feature's icon on a small square tinted
    // in its page's colour, and its name. High contrast keeps the system colours.
    private Button CreateTile(CommandPaletteEntry entry, bool wide, Windows.UI.Color? accent)
    {
        string id = entry.Key["feature:".Length..];
        bool dark = string.Equals(_services.Appearance.ThemeKey, "Dark", StringComparison.Ordinal);
        var face = new Grid { Padding = new Thickness(12, 12, 12, 10) };
        face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        face.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var icon = new FontIcon
        {
            Glyph = _featureGlyphs.TryGetValue(id, out string? glyph) ? glyph : ((char)0xE8FD).ToString(), FontSize = 16
        };
        var badge = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(6), Child = icon,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        if (accent is { } color)
        {
            badge.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(dark ? (byte)0x59 : (byte)0x22, color.R, color.G, color.B));
            icon.Foreground = new SolidColorBrush(dark ? Lighten(color, 0.55) : color);
        }
        else
        {
            badge.BorderThickness = new Thickness(1);
            badge.BorderBrush = _themeBrush("AppTileBorderBrush");
        }
        face.Children.Add(badge);
        if (entry.Shortcut.Length > 0)
        {
            face.Children.Add(new Border
            {
                Padding = new Thickness(5, 0, 5, 1), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1), BorderBrush = _themeBrush("AppStrongBorderBrush"),
                Child = new TextBlock { Text = entry.Shortcut, FontSize = 11 }
            });
        }
        // Wide tiles also say where the feature is; square ones keep it for the line under the tiles.
        if (wide)
        {
            var place = new TextBlock
            {
                Text = entry.Detail, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2)
            };
            Grid.SetRow(place, 1);
            face.Children.Add(place);
        }
        var title = new TextBlock
        {
            Text = entry.Title, FontSize = wide ? 14 : 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, MaxLines = wide ? 1 : 2, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = wide ? 20 : 18
        };
        Grid.SetRow(title, 2);
        face.Children.Add(title);
        Brush rest = _themeBrush("AppTileBackgroundBrush"), over = _themeBrush("AppTilePointerOverBrush");
        Brush edge = _themeBrush("AppTileBorderBrush"), strongEdge = _themeBrush("AppStrongBorderBrush");
        var card = new Border { Child = face, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Background = rest, BorderBrush = edge };
        var tile = new Button
        {
            Content = card, Width = wide ? TileSize * 2 + TileGap : TileSize, Height = TileHeight, Padding = new Thickness(0),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Tag = entry,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5), RenderTransform = new ScaleTransform()
        };
        // The card draws its own hover; the button's own states would paint a second surface behind it.
        var none = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (string key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            tile.Resources[key] = none;
        string where = T("CommandPalette.TileWhere", entry.Title, entry.Detail);
        ToolTipService.SetToolTip(tile, entry.Detail);
        AutomationProperties.SetAutomationId(tile, "Feature" + id);
        AutomationProperties.SetName(tile, entry.Title + ", " + entry.Detail + (entry.Shortcut.Length > 0 ? ", " + entry.Shortcut : ""));
        tile.Click += (_, _) => RunCommandPaletteEntry(entry);
        // Pointing at a tile or moving to it says where the feature is, under the tiles; pressing sinks it in a little.
        void ShowWhere()
        {
            _tileWhere!.Text = where;
            card.Background = over;
            card.BorderBrush = strongEdge;
        }
        void Leave()
        {
            card.Background = rest;
            card.BorderBrush = edge;
        }
        void Press(double scale)
        {
            var transform = (ScaleTransform)tile.RenderTransform;
            transform.ScaleX = transform.ScaleY = scale;
        }
        tile.PointerEntered += (_, _) => ShowWhere();
        tile.PointerExited += (_, _) => { Leave(); Press(1); };
        tile.GotFocus += (_, _) => ShowWhere();
        tile.LostFocus += (_, _) => Leave();
        tile.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Press(0.97)), handledEventsToo: true);
        tile.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Press(1)), handledEventsToo: true);
        tile.PointerCaptureLost += (_, _) => Press(1);
        return tile;
    }

    private static Windows.UI.Color Lighten(Windows.UI.Color color, double amount)
        => Microsoft.UI.ColorHelper.FromArgb(255, (byte)(color.R + (255 - color.R) * amount), (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));

    // The start screen opens from the bottom-left corner: its backdrop spreads out as a growing circle, and the tiles come
    // in column by column from the left, each sliding up and to the right as it fades in. Everything runs on the
    // composition thread (no layout work per frame), and the animations always end with every tile shown. Reduced
    // motion shows it at once.
    private int _tilesAnimation;
    private static readonly TimeSpan RevealTime = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan TileFadeTime = TimeSpan.FromMilliseconds(240), TileMoveTime = TimeSpan.FromMilliseconds(360);
    private const int ColumnStepMs = 40, FirstColumnMs = 70, LastAnimatedColumn = 16;

    private void RevealFromBottomLeft()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(_commandPaletteOverlay!);
        if (_services.ShellSettings.ReduceMotion)
        {
            visual.Clip = null;
            return;
        }
        Compositor compositor = visual.Compositor;
        float width = (float)Math.Max(_root.ActualWidth, 1), height = (float)Math.Max(_root.ActualHeight, 1);
        float radius = MathF.Sqrt(width * width + height * height);
        CompositionEllipseGeometry circle = compositor.CreateEllipseGeometry();
        circle.Center = new Vector2(0, height);
        circle.Radius = new Vector2(radius);
        visual.Clip = compositor.CreateGeometricClip(circle);
        Vector2KeyFrameAnimation grow = compositor.CreateVector2KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector2(16));
        grow.InsertKeyFrame(1, new Vector2(radius), Decelerate(compositor));
        grow.Duration = RevealTime;
        int version = _tilesAnimation;
        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        circle.StartAnimation("Radius", grow);
        batch.End();
        // The clip is only for the opening; without it nothing is cut off when the window is resized later.
        batch.Completed += (_, _) => _root.DispatcherQueue.TryEnqueue(() =>
        {
            if (version == _tilesAnimation) visual.Clip = null;
        });
    }

    private void AnimateTilesIn()
    {
        int version = ++_tilesAnimation;
        _tileScroller!.ChangeView(0, null, null, disableAnimation: true);
        if (_services.ShellSettings.ReduceMotion || _tileColumns.Count == 0)
        {
            foreach ((UIElement element, _) in _tileColumns)
            {
                Visual still = ElementCompositionPreview.GetElementVisual(element);
                still.StopAnimation("Opacity");
                still.Opacity = 1;
            }
            return;
        }
        Compositor compositor = ElementCompositionPreview.GetElementVisual(_tileGroups!).Compositor;
        CompositionEasingFunction ease = Decelerate(compositor);
        // One pair of animations per column: the columns past the window's right edge all start with the last one.
        var fades = new Dictionary<int, ScalarKeyFrameAnimation>();
        var moves = new Dictionary<int, Vector3KeyFrameAnimation>();
        foreach ((UIElement element, int column) in _tileColumns)
        {
            int step = Math.Min(column, LastAnimatedColumn);
            if (!fades.TryGetValue(step, out ScalarKeyFrameAnimation? fade))
            {
                TimeSpan delay = TimeSpan.FromMilliseconds(FirstColumnMs + ColumnStepMs * step);
                fade = compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(0, 0);
                fade.InsertKeyFrame(1, 1, ease);
                fade.Duration = TileFadeTime;
                fade.DelayTime = delay;
                fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                Vector3KeyFrameAnimation move = compositor.CreateVector3KeyFrameAnimation();
                move.InsertKeyFrame(0, new Vector3(-24, 28, 0));
                move.InsertKeyFrame(1, Vector3.Zero, ease);
                move.Duration = TileMoveTime;
                move.DelayTime = delay;
                move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                fades[step] = fade;
                moves[step] = move;
            }
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Translation", moves[step]);
        }
    }

    // Windows 11's "fast out, slow in" curve for things that arrive.
    private static CompositionEasingFunction Decelerate(Compositor compositor)
        => compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

    // Tab and Shift+Tab jump between the groups of tiles; past the last or first group they go back to the search box.
    private void OnTilePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab || _groupFirstTiles.Count == 0) return;
        if (FocusManager.GetFocusedElement(_root.XamlRoot) is not Button focused || !_tileGroupIndex.TryGetValue(focused, out int group)) return;
        bool back = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        int next = back ? group - 1 : group + 1;
        if (next < 0 || next >= _groupFirstTiles.Count) _commandPaletteSearch!.Focus(FocusState.Keyboard);
        else _groupFirstTiles[next].Focus(FocusState.Keyboard);
        e.Handled = true;
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
            case VirtualKey.Down or VirtualKey.Tab when _tilesShown && _firstTile is not null
                && !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down):
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
