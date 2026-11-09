using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly ShortcutInputService _shortcutInput = new();
    /// <summary>The Shortcuts page and overlay state, shared with ShortcutsPage (stage J5).</summary>
    private ShortcutPageViewModel _shortcuts => _services.Shortcuts;
    private readonly ModShortcutScanCache _shortcutScanCache = new();
    private readonly Dictionary<DetectedModShortcut, Button> _overlayShortcutControls = new();
    private ShortcutsPage? _shortcutsPage;
    // The Mod list and windows are read on first use: the page shown or the overlay opened.
    private bool _shortcutsPrepared;
    private ShortcutOverlayWindow? _shortcutOverlay;
    private ShortcutPanelHotkey? _shortcutPanelHotkey;
    private Slider? _overlayOpacitySlider;
    private bool _shortcutOpacitySyncing;
    // One shared brush: changing its colour recolours every overlay text at once.
    private readonly SolidColorBrush _overlayTextBrush = new(Color.FromArgb(255, 255, 255, 255));
    private TextBlock? _overlayTargetText, _overlayStatusText;
    private ComboBox? _overlayModPicker;
    private ToggleButton? _overlayInputToggle;
    private bool _overlaySyncing;
    private bool _refreshingShortcutOptions;
    // Repository-relative paths of Mods enabled in the loader folder; null when it cannot be determined.
    private HashSet<string>? _shortcutDeployedMods;
    private ShortcutModOption[] _overlayModOptions = [];
    private object? _overlayModOptionsSource;
    private int _shortcutDeployedRevision;
    private StackPanel? _overlayButtonList;
    private ScrollViewer? _overlayScroll;
    private DetectedModShortcut[] _overlayBindings = [];

    private void OnShortcutsNavClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _currentPrimarySection = PrimarySection.Shortcuts;
            ApplyShellState(refreshRepository: false);
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OnShortcutsNavClicked), ex); }
    }

    private bool IsEnglishUi => !IsChineseUi;

    /// <summary>The page relabels itself by rebuilding from the view model; the overlay is rebuilt here, and the Mods are read again.</summary>
    private void RebuildShortcutPaneForLanguage()
    {
        if (!_shortcutsPrepared) return;
        bool overlayVisible = _shortcuts.OverlayVisible;
        ++_shortcuts.ScanRevision;
        _characterFollowTimer?.Dispose();
        _characterFollowTimer = null;
        ShortcutOverlayWindow? oldOverlay = _shortcutOverlay;
        _shortcutOverlay = null; _shortcuts.OverlayVisible = false;
        _overlayButtonList = null; _overlayScroll = null; _overlayBindings = []; _overlayOpacitySlider = null;
        oldOverlay?.Close();
        _overlayShortcutControls.Clear();
        _shortcutsPage?.RebuildForLanguage();
        // Input stays disarmed after the rebuild; the user re-enables it deliberately.
        _shortcuts.InputEnabled = false;
        UpdateShortcutPrivilegeNotice();
        _ = RefreshShortcutPaneSafelyAsync();
        if (overlayVisible) OpenShortcutOverlay();
    }

    private Button ShortcutButton(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Reads the windows once, when the page is first shown or the overlay first opened.</summary>
    private void PrepareShortcuts()
    {
        if (_shortcutsPrepared) return;
        _shortcutsPrepared = true;
        UpdateShortcutPrivilegeNotice();
        RefreshShortcutTargets();
    }

    // Stage J5a: what the page and the overlay change in the view model, applied here.
    private void OnShortcutStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShortcutPageViewModel.SelectedMod):
                if (_refreshingShortcutOptions) return;
                if (!_characterAutoSelecting)
                {
                    if (_characterFollowToggle is not null) _characterFollowToggle.IsOn = false;
                    if (SelectedShortcutRole() is string role && _shortcuts.SelectedMod is ShortcutModOption selected)
                        _characterPreferredMods[role] = selected.Path;
                }
                _ = RunUiActionAsync("Select shortcut Mod", () => LoadShortcutSelectionAsync());
                break;
            case nameof(ShortcutPageViewModel.SelectedTarget):
                // A newly selected target always requires deliberate re-arming (the view model disarms input).
                UpdateShortcutPrivilegeNotice();
                RefreshShortcutOverlay();
                break;
            case nameof(ShortcutPageViewModel.InputEnabled):
                RefreshShortcutOverlay();
                break;
            case nameof(ShortcutPageViewModel.HideUnsupported):
                SaveShellConfig();
                RenderShortcutButtons();
                break;
            case nameof(ShortcutPageViewModel.InputMode):
                SaveShellConfig();
                break;
            case nameof(ShortcutPageViewModel.OverlayOpacity):
                _shortcutOpacitySyncing = true;
                try { if (_overlayOpacitySlider is not null) _overlayOpacitySlider.Value = _shortcuts.OverlayOpacity; }
                finally { _shortcutOpacitySyncing = false; }
                _shortcutOverlay?.SetBackgroundOpacity(_shortcuts.OverlayOpacity);
                ApplyOverlayAppearance();
                SaveShellConfig();
                break;
        }
    }

    private void OnShortcutPageRequested(object? sender, ShortcutPageRequest request)
    {
        switch (request.Action)
        {
            case ShortcutPageAction.RefreshWindows: RefreshShortcutTargets(); break;
            case ShortcutPageAction.ReloadIni: _ = RefreshOverlayFromDiskAsync(); break;
            case ShortcutPageAction.OpenIni: ShowShortcutIniFile(); break;
            case ShortcutPageAction.SendShortcut when request.Argument is DetectedModShortcut shortcut:
                _ = RunUiActionAsync("Send shortcut", () => SendShortcutFromPanelAsync(shortcut));
                break;
            case ShortcutPageAction.SendNumLock: _ = SendNumLockAsync(); break;
            case ShortcutPageAction.RestartElevated: RestartManagerElevated(); break;
            case ShortcutPageAction.SetHotkey when request.Argument is (uint modifiers, uint key): SetShortcutHotkey(modifiers, key); break;
            case ShortcutPageAction.DisableHotkey:
                _shortcutPanelHotkey?.Disable();
                _shortcuts.HotkeyEnabled = false;
                _shortcuts.HotkeyRegistered = false;
                _shortcuts.HotkeyRejected = false;
                SaveShellConfig();
                break;
        }
    }

    // A choice takes effect at once; when it cannot be registered the previous hotkey stays and the page says so.
    private void SetShortcutHotkey(uint modifiers, uint key)
    {
        if (_shortcutPanelHotkey?.TrySet(modifiers, key) == true)
        {
            _shortcuts.HotkeyModifiers = modifiers;
            _shortcuts.HotkeyKey = key;
            _shortcuts.HotkeyEnabled = true;
            _shortcuts.HotkeyRegistered = true;
            _shortcuts.HotkeyRejected = false;
            SaveShellConfig();
        }
        else
        {
            _shortcuts.HotkeyRejected = true;
        }
    }

    private async Task RefreshShortcutModsAsync()
    {
        if (!_shortcutsPrepared) return;
        string repository = GetSelectedRepository()?.SourcePath ?? string.Empty;
        string? previous = _shortcuts.SelectedMod?.Path ?? _currentSecondLevelPath;
        int revision = ++_shortcuts.ScanRevision;
        _shortcuts.InputEnabled = false;
        _shortcuts.Detected = [];
        _shortcuts.LoadedPath = string.Empty;
        RenderShortcutButtons();
        _shortcuts.StatusText = T("ShortcutPanel.ReadingCurrentRepository");
        ShortcutModOption[] options = await Task.Run(() => ReadShortcutModOptions(repository));
        if (_onlineWindowClosed || revision != _shortcuts.ScanRevision) return;
        // A new list clears the choice, as replacing a ComboBox's items did; the choice below loads the bindings again.
        _shortcuts.ModOptions = options;
        _shortcuts.SelectedMod = null;
        _ = RefreshShortcutDeployedModsAsync();
        LoadCharacterFollowProfile(repository);
        _shortcuts.SelectedMod = options.FirstOrDefault(p => string.Equals(p.Path, previous, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        if (options.Length == 0) _shortcuts.StatusText = T("ShortcutPanel.NoModsFoundSelectARepository");
    }

    private async Task RefreshShortcutPaneSafelyAsync()
    {
        try { await RefreshShortcutModsAsync(); }
        catch (Exception ex) { if (!_onlineWindowClosed) _shortcuts.StatusText = ex.Message; }
    }

    private async Task LoadShortcutSelectionAsync(bool force = false)
    {
        int revision = ++_shortcuts.ScanRevision;
        _shortcuts.Detected = [];
        _shortcuts.LoadedPath = string.Empty;
        RenderShortcutButtons();
        if (_shortcuts.SelectedMod is not ShortcutModOption mod) { RefreshShortcutOverlay(bindingsChanged: true); return; }
        try
        {
            _shortcuts.StatusText = T("ShortcutPanel.ReadingINI");
            var detected = await Task.Run(() => _shortcutScanCache.Scan(mod.Path, force));
            if (_onlineWindowClosed || revision != _shortcuts.ScanRevision) return;
            _shortcuts.Detected = detected;
            _shortcuts.LoadedPath = mod.Path;
            _shortcuts.StatusText = T("ShortcutPanel.ReadBindingsInGameConditionsAre", detected.Count);
            RenderShortcutButtons();
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Read shortcut panel INI", ex);
            if (!_onlineWindowClosed && revision == _shortcuts.ScanRevision) _shortcuts.StatusText = ex.Message;
        }
    }

    private void RefreshShortcutTargets()
    {
        ShortcutTarget? previous = _shortcuts.SelectedTarget;
        var targets = ShortcutInputService.ListTargets();
        _shortcuts.Targets = targets;
        ShortcutTarget? kept = targets.FirstOrDefault(t => t.Handle == previous?.Handle && t.StartTicks == previous.StartTicks);
        // Without a (still running) chosen window, pick the game window. Input stays off until the
        // user turns it on: selecting a target never arms it.
        int detected = kept is null
            ? GameWindowDetection.Find(targets.Select(t => new GameWindowCandidate(t.ProcessName, t.Label)).ToArray(),
                PreferredGameProcesses(GetSelectedRepository()))
            : -1;
        _shortcuts.SelectedTarget = detected >= 0 ? targets[detected] : kept;
        if (detected >= 0)
            _shortcuts.StatusText = T("ShortcutPanel.GameWindowDetected", targets[detected].Label);
    }

    // The process names of the current repository's game, so its window wins over other running games.
    private string[] PreferredGameProcesses(WorkspaceRepository? repository)
    {
        if (repository is null) return [];
        if (IsEndfieldRepository(repository)) return [.. GameWindowDetection.KnownGames[0]];
        RepositoryPresetInfo? preset = BuiltInRepositoryPresets.FirstOrDefault(candidate => IsRepositoryForPreset(repository, candidate));
        return preset?.Id switch
        {
            "genshin" => [.. GameWindowDetection.KnownGames[1]],
            "hsr" => [.. GameWindowDetection.KnownGames[2]],
            "zzz" => [.. GameWindowDetection.KnownGames[3]],
            _ => []
        };
    }

    private DetectedModShortcut[] VisibleShortcuts() => _shortcuts.VisibleShortcuts();

    // Selects the INI of the first binding in File Explorer; the panel itself never edits Mod files.
    private void ShowShortcutIniFile()
    {
        if (_shortcuts.Detected.Count == 0 || string.IsNullOrEmpty(_shortcuts.LoadedPath)) return;
        string sourceFile = _shortcuts.Detected[0].SourceFile;
        try
        {
            string? path = Directory.EnumerateFiles(_shortcuts.LoadedPath, sourceFile, new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive
            }).OrderBy(file => file, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            OpenFileLocation(path, sourceFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogApplicationIssue("Show shortcut INI file", ex);
            _shortcuts.StatusText = ex.Message;
        }
    }

    // The page renders its own cards from the view model; the overlay follows here.
    private void RenderShortcutButtons() => RefreshShortcutOverlay(bindingsChanged: true);

    private string UnsupportedReasonText(ShortcutUnsupportedReason reason) => reason switch
    {
        ShortcutUnsupportedReason.Mouse => T("ShortcutPanel.MouseButtonAModMenuClick"),
        ShortcutUnsupportedReason.Controller => T("ShortcutPanel.ControllerButtonKeyboardInputCannotEmulate"),
        ShortcutUnsupportedReason.UnknownKey => T("ShortcutPanel.ContainsAnUnrecognizedKeyName"),
        ShortcutUnsupportedReason.Contradictory => T("ShortcutPanel.ContradictoryKeyConstraints"),
        ShortcutUnsupportedReason.TooManyKeys => T("ShortcutPanel.TooManyKeysHeldAtOnce"),
        ShortcutUnsupportedReason.SystemShortcut => T("ShortcutPanel.WouldTriggerASystemActionSuch"),
        _ => T("ShortcutPanel.NoKeyToPress")
    };

    private TextBlock FixedStatusText(int lines, double fontSize)
    {
        var text = new TextBlock
        {
            FontSize = fontSize, TextWrapping = TextWrapping.Wrap, MaxLines = lines,
            TextTrimming = TextTrimming.CharacterEllipsis, Height = Math.Ceiling(fontSize * 1.34 * lines)
        };
        // The full message stays available when trimmed.
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => ToolTipService.SetToolTip(text, string.IsNullOrEmpty(text.Text) ? null : text.Text));
        return text;
    }

    internal static Color ParseOverlayTextColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && uint.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out uint rgb)
            ? Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : Color.FromArgb(255, 255, 255, 255);

    // Saves the user's colour, not one automatic contrast has put in its place for the moment.
    private string OverlayTextColorValue
    {
        get
        {
            Color color = _overlayAutoTextColor is not null ? _overlayUserTextColor : _overlayTextBrush.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    private Button BuildOverlayTextColorButton()
    {
        var picker = new ColorPicker
        {
            Color = _overlayTextBrush.Color, IsAlphaEnabled = false, IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false, IsHexInputVisible = true
        };
        picker.ColorChanged += (_, e) =>
        {
            _overlayUserTextColor = e.NewColor;
            _overlayAutoTextColor = null;
            _overlayTextBrush.Color = e.NewColor;
            ApplyOverlayAppearance();
        };
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (string hex in new[] { "#FFFFFF", "#FFE8A3", "#B8F5C0", "#A8D8FF", "#FFC0DA", "#202020" })
        {
            Color color = ParseOverlayTextColor(hex);
            var swatch = new Button
            {
                Width = 30, Height = 30, Padding = new Thickness(0), Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)), BorderThickness = new Thickness(1)
            };
            ToolTipService.SetToolTip(swatch, hex);
            swatch.Click += (_, _) => picker.Color = color;
            presets.Children.Add(swatch);
        }
        var flyoutContent = new StackPanel { Spacing = 10 };
        flyoutContent.Children.Add(new TextBlock { Text = T("ShortcutPanel.Presets") });
        flyoutContent.Children.Add(presets);
        flyoutContent.Children.Add(picker);
        var flyout = new Flyout { Content = flyoutContent };
        flyout.Closed += (_, _) => SaveShellConfig();
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        label.Children.Add(new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(3), Background = _overlayTextBrush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)), BorderThickness = new Thickness(1)
        });
        label.Children.Add(new TextBlock { Text = T("ShortcutPanel.OverlayTextColor"), VerticalAlignment = VerticalAlignment.Center });
        return new Button { Content = label, Flyout = flyout };
    }

    private Slider CreateOverlayOpacitySlider(string header)
    {
        var slider = new Slider
        {
            Header = string.IsNullOrEmpty(header) ? null : header, Minimum = ShortcutOverlayWindow.MinimumOpacity, Maximum = 100, StepFrequency = 5,
            SmallChange = 5, Value = _shortcuts.OverlayOpacity
        };
        // WinUI raises ValueChanged while the template loads (observed resetting to the minimum);
        // only react to changes after the slider is live, and restore the saved value first.
        bool live = false;
        slider.Loaded += (_, _) =>
        {
            if (live) return;
            slider.Value = _shortcuts.OverlayOpacity;
            live = true;
        };
        slider.ValueChanged += (_, e) =>
        {
            if (!live || _shortcutOpacitySyncing || (int)Math.Round(e.NewValue) == _shortcuts.OverlayOpacity) return;
            _shortcuts.OverlayOpacity = (int)Math.Round(e.NewValue);
            _shortcutOpacitySyncing = true;
            try
            {
                if (_overlayOpacitySlider is not null && _overlayOpacitySlider != slider) _overlayOpacitySlider.Value = _shortcuts.OverlayOpacity;
            }
            finally { _shortcutOpacitySyncing = false; }
            _shortcutOverlay?.SetBackgroundOpacity(_shortcuts.OverlayOpacity);
            ApplyOverlayAppearance();
            SaveShellConfig();
        };
        return slider;
    }

    // A binding on the overlay; the page draws its own cards (ShortcutsPage).
    private Button CreateShortcutAction(DetectedModShortcut shortcut, bool floating = true)
    {
        ShortcutUnsupportedReason reason = ShortcutKeyPlan.Explain(shortcut.RawKey);
        bool supported = reason == ShortcutUnsupportedReason.None;
        var content = new StackPanel { Spacing = floating ? 3 : 6 };
        var title = new TextBlock { Text = shortcut.Shortcut, FontSize = floating ? 18 : 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var description = new TextBlock { Text = DescribeDetectedShortcut(shortcut), TextWrapping = TextWrapping.Wrap };
        string source = $"{shortcut.SourceFile} · [{shortcut.Section}]";
        if (shortcut.Condition.Length > 0) source += " · " + T("ShortcutPanel.Condition") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + T("ShortcutPanel.HoldForConfiguredDuration");
        var sourceText = new TextBlock { Text = source, FontSize = 11, Opacity = .65, TextWrapping = TextWrapping.Wrap };
        if (floating)
        {
            title.Foreground = description.Foreground = sourceText.Foreground = _overlayTextBrush;
            content.Children.Add(WithOverlayTextShadow(title));
            content.Children.Add(WithOverlayTextShadow(description));
        }
        content.Children.Add(sourceText);
        if (!supported)
            content.Children.Add(new TextBlock
            {
                Text = T("ShortcutPanel.CannotSend") + UnsupportedReasonText(reason) + T("ShortcutPanel.Key", shortcut.RawKey),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 150, 60))
            });
        var button = new Button
        {
            Content = content, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 10, 14, 10)
        };
        if (floating) button.AllowFocusOnInteraction = false;
        button.Click += async (_, _) => await RunUiActionAsync("Send shortcut", () => SendShortcutFromPanelAsync(shortcut));
        return button;
    }

    private async Task SendShortcutFromPanelAsync(DetectedModShortcut shortcut)
    {
        if (_shortcuts.SendInProgress) return;
        _shortcuts.SendInProgress = true;
        try
        {
            if (!_shortcuts.InputEnabled || _shortcuts.SelectedTarget is not ShortcutTarget target)
                throw new InvalidOperationException(T("ShortcutPanel.SelectATargetWindowAndEnable"));
            if (!Directory.Exists(_shortcuts.LoadedPath) || Path.GetFileName(_shortcuts.LoadedPath).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("ShortcutPanel.TheModFolderChangedReloadIt"));
            if (!ShortcutKeyPlan.TryCreate(shortcut.RawKey, out var plan)) return;
            int hold = shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)
                ? _shortcuts.HoldMilliseconds
                : _shortcuts.TapMilliseconds;
            string report = await _shortcutInput.SendAsync(target, plan!, hold, _shortcuts.InputMode, _services.Language.Current);
            if (!_onlineWindowClosed) _shortcuts.StatusText = shortcut.Shortcut + " · " + report;
        }
        catch (Exception ex)
        {
            if (!_onlineWindowClosed) _shortcuts.StatusText = ex.Message;
        }
        finally { _shortcuts.SendInProgress = false; }
        if (!_onlineWindowClosed) RefreshShortcutOverlay();
    }

    private void OpenShortcutOverlay()
    {
        try
        {
            if (_shortcutOverlay is null)
            {
                var overlay = new ShortcutOverlayWindow(BuildShortcutOverlayContent(), _overlayTextBrush, _overlayElementBrush, T("Common.ModShortcuts"));
                _shortcutOverlay = overlay;
                _shortcutOverlay.Closed += (_, _) =>
                {
                    // A language rebuild may already have opened a replacement overlay.
                    if (!ReferenceEquals(_shortcutOverlay, overlay)) return;
                    _shortcutOverlay = null; _shortcuts.OverlayVisible = false; _overlayClickThrough = false;
                    _overlayButtonList = null; _overlayScroll = null; _overlayBindings = []; _overlayOpacitySlider = null;
                };
                _shortcutOverlay.SetBackgroundOpacity(_shortcuts.OverlayOpacity);
            }
            // The game may have started after the window list was read.
            if (_shortcuts.SelectedTarget is not ShortcutTarget) RefreshShortcutTargets();
            _shortcutOverlay.ShowPanel();
            _shortcuts.OverlayVisible = true;
            RefreshShortcutOverlay(bindingsChanged: true);
            // Mods may have been enabled or disabled since the list was read.
            _ = RefreshShortcutDeployedModsAsync();
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OpenShortcutOverlay), ex); }
    }

    private UIElement BuildShortcutOverlayContent()
    {
        // A closed window still owns its controls. Never reparent cached buttons into a new XamlRoot.
        _overlayShortcutControls.Clear();
        _overlayTextShadows.Clear();
        // The overlay window draws the (translucent) background; this content stays transparent.
        var root = new Grid { Padding = new Thickness(14, 4, 14, 14), RowSpacing = 10, RequestedTheme = ElementTheme.Dark };
        ApplyOverlayElementResources(root);
        ApplyOverlayAppearance();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var panel = new StackPanel { Spacing = 6 };
        // Every header row has a fixed height so status changes never shift the list (row 1).
        // Only in-game-safe actions live here: switching Mod, arming input and a read-only preset save.
        // Restoring presets needs the game closed and stays in the main window. State slots can be
        // switched here only when the user turns on hot switching (MainWindow.HotReload.cs).
        _overlayModPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = _overlayTextBrush,
            PlaceholderText = T("ShortcutPanel.SelectAMod") };
        ToolTipService.SetToolTip(_overlayModPicker, T("ShortcutPanel.SwitchTheModWhoseShortcutsAre"));
        _overlayModPicker.SelectionChanged += (_, _) =>
        {
            if (_overlaySyncing || _overlayModPicker.SelectedItem is not ShortcutModOption chosen) return;
            if (!ReferenceEquals(_shortcuts.SelectedMod, chosen)) _shortcuts.SelectedMod = chosen;
        };
        _overlayTargetText = FixedStatusText(lines: 1, fontSize: 11);
        _overlayStatusText = FixedStatusText(lines: 2, fontSize: 11);
        _overlayTargetText.Foreground = _overlayStatusText.Foreground = _overlayTextBrush;
        var targetRow = OverlayLabel(_overlayTargetText);
        panel.Children.Add(_overlayModPicker); panel.Children.Add(CreateOverlayViewRow()); panel.Children.Add(targetRow);
        var actions = new Grid { ColumnSpacing = 6 };
        for (int i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _overlayInputToggle = new ToggleButton { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = _overlayTextBrush };
        ToolTipService.SetToolTip(_overlayInputToggle, T("ShortcutPanel.ArmOrPauseInputToThe"));
        _overlayInputToggle.Click += (_, _) => SetShortcutInputFromOverlay(_overlayInputToggle.IsChecked == true);
        var savePreset = ShortcutButton(T("ShortcutPanel.SavePreset"), () => _ = QuickSaveCombinationFromOverlayAsync());
        ToolTipService.SetToolTip(savePreset, T("ShortcutPanel.SaveTheEnabledModCombinationAnd"));
        var hide = ShortcutButton(T("ShortcutPanel.Hide"), () => { _shortcutOverlay?.AppWindow.Hide(); _shortcuts.OverlayVisible = false; });
        savePreset.Foreground = hide.Foreground = _overlayTextBrush;
        savePreset.HorizontalAlignment = hide.HorizontalAlignment = HorizontalAlignment.Stretch;
        // Three equal columns at the default 420 DIP width: default padding cut "Input: off" / "Save preset".
        foreach (Control action in new Control[] { _overlayInputToggle, savePreset, hide })
        {
            action.Padding = new Thickness(4, 5, 4, 6);
            action.FontSize = 13;
        }
        Grid.SetColumn(savePreset, 1); Grid.SetColumn(hide, 2);
        actions.Children.Add(_overlayInputToggle); actions.Children.Add(savePreset); actions.Children.Add(hide);
        panel.Children.Add(actions);
        var saveSingle = ShortcutButton(T("ShortcutPanel.SaveSingleModPreset"), () => _ = SaveSingleModPresetFromOverlayAsync());
        saveSingle.Foreground = _overlayTextBrush;
        saveSingle.HorizontalAlignment = HorizontalAlignment.Stretch;
        var refresh = ShortcutButton(T("ShortcutCapture.Refresh"), () => _ = RunUiActionAsync("Refresh overlay", RefreshOverlayFromDiskAsync));
        refresh.Foreground = _overlayTextBrush;
        var secondaryActions = new Grid { ColumnSpacing = 6 };
        secondaryActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        secondaryActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(refresh, 1);
        secondaryActions.Children.Add(saveSingle); secondaryActions.Children.Add(refresh);
        panel.Children.Add(secondaryActions);
        var opacityRow = new Grid { ColumnSpacing = 8 };
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var opacityLabel = new TextBlock { Text = T("ShortcutPanel.Opacity"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = _overlayTextBrush };
        _overlayOpacitySlider = CreateOverlayOpacitySlider(string.Empty);
        _overlayOpacitySlider.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(_overlayOpacitySlider, T("ShortcutPanel.OverlayBackgroundOpacity"));
        Grid.SetColumn(_overlayOpacitySlider, 1);
        opacityRow.Children.Add(OverlayLabel(opacityLabel)); opacityRow.Children.Add(_overlayOpacitySlider);
        panel.Children.Add(opacityRow);
        AddOverlaySlotSection(panel);
        var statusRow = OverlayLabel(_overlayStatusText);
        panel.Children.Add(statusRow);
        _overlayDetailRows = [targetRow, actions, secondaryActions, opacityRow, statusRow];
        ApplyOverlayCompactView();
        _overlayButtonList = new StackPanel { Spacing = 8 };
        _overlayScroll = new ScrollViewer { Content = _overlayButtonList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_overlayScroll, 1);
        root.Children.Add(panel); root.Children.Add(_overlayScroll);
        _overlayBindings = [];
        _overlayModOptionsSource = null;
        UpdateShortcutOverlayControls();
        return root;
    }

    private static ShortcutModOption[] ReadShortcutModOptions(string root)
    {
        if (!Directory.Exists(root)) return [];
        var enumeration = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        bool Active(string path) => !Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);
        return Directory.EnumerateDirectories(root, "*", enumeration).Where(Active)
            .SelectMany(category => Directory.EnumerateDirectories(category, "*", enumeration).Where(Active))
            .Select(path => new ShortcutModOption(path, Path.GetRelativePath(root, path)))
            .Take(3000).OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private async Task RefreshShortcutDeployedModsAsync(string? preferredPath = null)
    {
        if (!_shortcutsPrepared) return;
        int revision = ++_shortcutDeployedRevision;
        WorkspaceRepository? repository = GetSelectedRepository();
        string source = repository?.SourcePath ?? string.Empty, target = repository?.TargetPath ?? string.Empty;
        HashSet<string> deployed = new(StringComparer.OrdinalIgnoreCase);
        ShortcutModOption[] fresh = [];
        try
        {
            (fresh, deployed) = await Task.Run(() => (ReadShortcutModOptions(source),
                string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : ModCombinationDeploymentPolicy.Capture(source, target).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            LogApplicationIssue("Shortcut deployed Mods", ex); // Unknown deployment clears actions instead of exposing stale ones.
        }
        if (_onlineWindowClosed || revision != _shortcutDeployedRevision || repository != GetSelectedRepository()
            || source != (repository?.SourcePath ?? string.Empty) || target != (repository?.TargetPath ?? string.Empty)) return;
        ShortcutModOption? current = _shortcuts.SelectedMod;
        // Only a Mod that was enabled and has just been disabled or replaced is followed. The main page also
        // lists Mods that were never enabled; picking one of those must not be undone by a refresh.
        bool wasDeployed = current is not null && _shortcutDeployedMods?.Contains(current.Label) == true;
        _shortcutDeployedMods = deployed;
        _overlayModOptionsSource = null;
        var previous = _shortcuts.ModOptions as ShortcutModOption[] ?? [];
        // Retain record identity so unchanged lists and selections do not trigger a rescan.
        var byPath = previous.ToDictionary(option => option.Path, StringComparer.OrdinalIgnoreCase);
        var options = fresh.Select(option => byPath.GetValueOrDefault(option.Path) ?? option).ToArray();
        ShortcutModOption? chosen = current is null ? null : options.FirstOrDefault(option => option.Path == current.Path);
        if (current is not null && (chosen is null || (wasDeployed && !IsShortcutModDeployed(chosen))))
        {
            string? role = Path.GetDirectoryName(current.Path);
            chosen = options.FirstOrDefault(option => IsShortcutModDeployed(option)
                    && string.Equals(option.Path, preferredPath, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault(option => IsShortcutModDeployed(option)
                    && string.Equals(Path.GetDirectoryName(option.Path), role, StringComparison.OrdinalIgnoreCase));
        }
        _refreshingShortcutOptions = true;
        try
        {
            if (!previous.SequenceEqual(options))
            {
                // A new list clears the choice, as replacing a ComboBox's items did.
                _shortcuts.ModOptions = options;
                _shortcuts.SelectedMod = null;
            }
            _shortcuts.SelectedMod = chosen;
        }
        finally { _refreshingShortcutOptions = false; }
        if (!ReferenceEquals(current, chosen)) await LoadShortcutSelectionAsync();
        if (chosen is null && current is not null) _shortcuts.StatusText = T("ShortcutCapture.CurrentRemoved");
        RefreshShortcutOverlay(bindingsChanged: true);
    }

    private bool IsShortcutModDeployed(ShortcutModOption option) =>
        _shortcutDeployedMods?.Contains(option.Label) != false;

    private void SetShortcutInputFromOverlay(bool enable)
    {
        // Arming needs a target: look for the game window first; the overlay cannot pick other windows.
        if (enable && _shortcuts.SelectedTarget is not ShortcutTarget) RefreshShortcutTargets();
        if (enable && _shortcuts.SelectedTarget is not ShortcutTarget)
            _shortcuts.StatusText = T("ShortcutPanel.SelectTheTargetGameWindowIn");
        else
            _shortcuts.InputEnabled = enable;
        RefreshShortcutOverlay();
    }

    /// <summary>Flush loader memory before capturing; offer an explicit disk-only fallback when input is unavailable.</summary>
    private async Task QuickSaveCombinationFromOverlayAsync()
    {
        if (_savingOverlaySinglePreset || _applyingCombinationProfile || (_shortcuts.SendInProgress && !_restoringPersistentRuntime)) return;
        if (GetSelectedRepository() is not { } repository)
        {
            _shortcuts.StatusText = T("ShortcutPanel.SelectARepositoryInTheMain");
            RefreshShortcutOverlay();
            return;
        }
        _savingOverlaySinglePreset = true;
        if (!await WaitForOverlayCaptureAsync()) { _savingOverlaySinglePreset = false; return; }
        string? userIni = repository.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        bool includeState = userIni is not null && File.Exists(userIni);
        string name = T("ShortcutPanel.InGame", DateTime.Now);
        _shortcuts.StatusText = T("ShortcutPanel.SavingCombinationPreset");
        RefreshShortcutOverlay();
        SetCombinationBusy(true);
        try
        {
            CharacterFrame? preview = await CapturePresetPreviewAsync();
            // Without a d3dx_user.ini there is no loader state to flush: save the Mod combination alone, as before.
            if (includeState && !await PrepareOverlayCaptureAsync(userIni!)) return;
            (int Mods, int Parameters)? saved = await CaptureCombinationProfileAsync(repository, name, null, includeState, userIni ?? string.Empty,
                () => Task.FromResult(false));
            string previewNote = string.Empty;
            if (saved is not null && _configurationProfiles.FirstOrDefault(item => item.Id == _selectedConfigurationProfileId) is { } profile)
            {
                profile.PreviewFile = await StorePresetPreviewAsync(repository.Id, profile.Id, preview);
                if (profile.PreviewFile is not null) SaveShellConfig();
                previewNote = PresetPreviewNote(profile.PreviewFile);
            }
            _shortcuts.StatusText = saved is (int mods, int parameters)
                ? T("ShortcutPanel.SavedPresetModsParameters", name, mods, parameters) + previewNote
                : T("ShortcutPanel.NoEnabledModsThePresetWas");
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay combination capture", ex);
            if (!_onlineWindowClosed) _shortcuts.StatusText = T("ShortcutPanel.PresetNotSaved") + OverlayCaptureError(ex);
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (!_onlineWindowClosed)
            {
                SetCombinationBusy(false);
                RefreshShortcutOverlay(bindingsChanged: true);
            }
        }
    }

    private void RefreshShortcutOverlay(bool bindingsChanged = false)
    {
        if (_shortcutOverlay is not null && _shortcuts.OverlayVisible) UpdateShortcutOverlayControls(bindingsChanged);
    }

    private void UpdateShortcutOverlayControls(bool bindingsChanged = true)
    {
        if (_overlayButtonList is null) return;
        _overlaySyncing = true;
        try
        {
            // In game only enabled Mods matter, so the overlay lists those; it forwards choices to the main picker.
            object? source = _shortcuts.ModOptions;
            if (!ReferenceEquals(_overlayModOptionsSource, source))
            {
                _overlayModOptionsSource = source;
                _overlayModOptions = (source as ShortcutModOption[] ?? []).Where(IsShortcutModDeployed).ToArray();
                _overlayModPicker!.ItemsSource = _overlayModOptions;
            }
            ShortcutModOption? current = _shortcuts.SelectedMod;
            _overlayModPicker!.SelectedItem = current is not null && _overlayModOptions.Contains(current) ? current : null;
            _overlayModPicker.PlaceholderText = _overlayModOptions.Length == 0
                ? T("ShortcutPanel.NoEnabledMods")
                : current is null ? T("ShortcutPanel.SelectAMod") : T("ShortcutPanel.CurrentModNotEnabledPickAn");
            _overlayInputToggle!.IsChecked = _shortcuts.InputEnabled;
            _overlayInputToggle.Content = _shortcuts.InputEnabled ? T("ShortcutPanel.InputOn") : T("ShortcutPanel.InputOff");
        }
        finally { _overlaySyncing = false; }
        _overlayTargetText!.Text = _shortcuts.SelectedTarget?.Label ?? T("ShortcutPanel.NoTargetWindowSelectItIn");
        _overlayStatusText!.Text = _shortcuts.StatusText;
        // Status-only updates never enumerate bindings or rebuild slots.
        if (!bindingsChanged) return;
        UpdateOverlaySlotSection();
        bool enabled = _shortcuts.SelectedMod is ShortcutModOption selected && IsShortcutModDeployed(selected);
        DetectedModShortcut[] visible = enabled ? VisibleShortcuts() : [];
        if (_overlayBindings.SequenceEqual(visible)) return;
        _overlayBindings = visible;
        _overlayButtonList.Children.Clear();
        foreach (var shortcut in _overlayBindings)
        {
            if (!_overlayShortcutControls.TryGetValue(shortcut, out Button? control))
                _overlayShortcutControls[shortcut] = control = CreateShortcutAction(shortcut, floating: true);
            _overlayButtonList.Children.Add(control);
        }
        if (_overlayShortcutControls.Count > 768) _overlayShortcutControls.Clear();
        _overlayScroll?.ChangeView(null, 0, null, true);
    }

    private async Task SendNumLockAsync()
    {
        if (_shortcuts.SendInProgress) return;
        _shortcuts.SendInProgress = true;
        try
        {
            if (!_shortcuts.InputEnabled || _shortcuts.SelectedTarget is not ShortcutTarget target)
                throw new InvalidOperationException(T("ShortcutPanel.SelectATargetWindowAndEnable"));
            _shortcuts.StatusText = await _shortcutInput.SendAsync(target, new ShortcutKeyPlan(new ushort[] { 0x90 }, Array.Empty<ushort>()), 150, ShortcutInputMode.VirtualKey, _services.Language.Current);
        }
        catch (Exception ex) { if (!_onlineWindowClosed) _shortcuts.StatusText = ex.Message; }
        finally { _shortcuts.SendInProgress = false; if (!_onlineWindowClosed) RefreshShortcutOverlay(); }
    }

    private void CloseShortcutPanel()
    {
        ++_shortcuts.ScanRevision;
        _shortcutPanelHotkey?.Dispose();
        _characterFollowTimer?.Dispose();
        _shortcutInput.Dispose();
        _shortcutOverlay?.Close();
        _shortcutOverlay = null;
    }

    private void InitializeShortcutPanelHotkey()
    {
        try
        {
            _shortcutPanelHotkey = new ShortcutPanelHotkey(WinRT.Interop.WindowNative.GetWindowHandle(this),
                () => DispatcherQueue.TryEnqueue(ToggleShortcutOverlay));
            if (_shortcuts.HotkeyEnabled)
                _shortcuts.HotkeyRegistered = _shortcutPanelHotkey.TrySet(_shortcuts.HotkeyModifiers, _shortcuts.HotkeyKey);
        }
        catch (Exception ex) { LogApplicationIssue("Shortcut panel hotkey", ex); }
    }

    private async void ToggleShortcutOverlay()
    {
        try
        {
            if (_onlineWindowClosed || _savingOverlaySinglePreset) return;
            // The hotkey is the only way back once the overlay ignores the mouse: first press restores it.
            if (_overlayClickThrough)
            {
                SetOverlayClickThrough(false);
                return;
            }
            if (_shortcuts.OverlayVisible)
            {
                _shortcutOverlay?.AppWindow.Hide();
                _shortcuts.OverlayVisible = false;
                return;
            }
            if (!_shortcutsPrepared)
            {
                PrepareShortcuts();
                await RefreshShortcutModsAsync();
                if (_onlineWindowClosed) return;
            }
            OpenShortcutOverlay();
        }
        catch (Exception ex) { LogApplicationIssue("Toggle shortcut overlay", ex); }
    }

    private void UpdateShortcutPrivilegeNotice()
    {
        ShortcutSecurity own = ShortcutProcessSecurity.Current;
        bool elevated = own.UiAccess || ShortcutPrivilegePolicy.IsElevated(own.Integrity);
        _shortcuts.IsElevated = elevated;
        if (elevated)
        {
            _shortcuts.PrivilegeText = T("ShortcutPanel.TheManagerIsElevatedAndCan");
            return;
        }
        if (_shortcuts.SelectedTarget is not ShortcutTarget target)
        {
            // Only an Endfield repository gets the Endfield-specific explanation.
            _shortcuts.PrivilegeText = EndfieldBridgeService.CanUse(GetSelectedRepository())
                ? T("ShortcutPanel.EndfieldRunsAsAdministratorInputFrom")
                : T("ShortcutPanel.ElevatedGameNeedsElevatedManager");
            return;
        }
        ShortcutSecurity destination = ShortcutProcessSecurity.Read(target.ProcessId);
        _shortcuts.PrivilegeText = ShortcutPrivilegePolicy.Evaluate(own.Integrity, own.UiAccess, destination.Integrity, destination.Error) == ShortcutPrivilegeVerdict.Allowed
            ? T("ShortcutPanel.TheSelectedWindowRunsAtThe")
            : T("ShortcutPanel.TheSelectedWindowRunsAsAdministrator");
    }

    private void RestartManagerElevated()
    {
        try
        {
            // The new process waits for this one to exit before taking the single-instance key.
            Process.Start(new ProcessStartInfo(EndfieldBridgeService.LaunchExecutablePath, $"{Program.RestartAfterArgument} {Environment.ProcessId}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _shortcuts.StatusText = T("ShortcutPanel.AdministratorApprovalWasDeclinedTheManager");
            return;
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(RestartManagerElevated), ex); return; }
        Close();
    }
}
