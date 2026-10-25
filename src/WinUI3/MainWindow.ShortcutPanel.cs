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
    private readonly ModShortcutScanCache _shortcutScanCache = new();
    private DetectedModShortcut[] _mainBindings = [];
    private readonly Dictionary<DetectedModShortcut, Button> _mainShortcutControls = new();
    private readonly Dictionary<DetectedModShortcut, Button> _overlayShortcutControls = new();
    private ComboBox? _shortcutModPicker;
    private ComboBox? _shortcutTargetPicker;
    private StackPanel? _shortcutButtons;
    private TextBlock? _shortcutStatus;
    private ToggleSwitch? _shortcutEnabledToggle;
    private NumberBox? _shortcutHoldDuration;
    private NumberBox? _shortcutTapDuration;
    private ComboBox? _shortcutInputModePicker;
    private ShortcutInputMode _shortcutInputMode = ShortcutInputMode.ScanCode;
    private ShortcutOverlayWindow? _shortcutOverlay;
    private IReadOnlyList<DetectedModShortcut> _shortcutDetected = [];
    private int _shortcutScanRevision;
    private bool _shortcutInputEnabled;
    private bool _shortcutSendInProgress;
    private string _shortcutLoadedPath = string.Empty;
    private ShortcutPanelHotkey? _shortcutPanelHotkey;
    private uint _shortcutPanelHotkeyModifiers = 3; // Ctrl + Alt
    private uint _shortcutPanelHotkeyKey = 0x4B; // K
    private bool _shortcutPanelHotkeyEnabled = true;
    private bool _shortcutOverlayVisible;
    private bool _shortcutHotkeyRegistered;
    private TextBlock? _shortcutHotkeyStatus;
    private TextBlock? _shortcutPrivilegeNotice;
    private Button? _shortcutElevateButton;
    private int _shortcutOverlayOpacity = 100;
    private bool _shortcutHideUnsupported;
    private Slider? _shortcutOpacitySlider, _overlayOpacitySlider;
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

    private sealed record ShortcutModOption(string Path, string Label)
    {
        public override string ToString() => Label;
    }

    private void OnShortcutsNavClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _currentPrimarySection = PrimarySection.Shortcuts;
            ApplyShellState(refreshRepository: false);
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OnShortcutsNavClicked), ex); }
    }

    private bool IsEnglishUi => _currentLanguage == AppLanguage.EnUs;
    private string? _shortcutRestoreModPath;

    /// <summary>The pane and overlay are built once with localized text; a language switch rebuilds both, keeping the selection.</summary>
    private void RebuildShortcutPaneForLanguage()
    {
        if (_shortcutModPicker is null) return;
        _shortcutRestoreModPath = (_shortcutModPicker.SelectedItem as ShortcutModOption)?.Path;
        var target = _shortcutTargetPicker?.SelectedItem as ShortcutTarget;
        double tap = _shortcutTapDuration?.Value ?? 200, hold = _shortcutHoldDuration?.Value ?? 300;
        bool overlayVisible = _shortcutOverlayVisible;
        ++_shortcutScanRevision;
        _characterFollowTimer?.Dispose();
        _characterFollowTimer = null;
        ShortcutOverlayWindow? oldOverlay = _shortcutOverlay;
        _shortcutOverlay = null; _shortcutOverlayVisible = false;
        _overlayButtonList = null; _overlayScroll = null; _overlayBindings = []; _overlayOpacitySlider = null;
        oldOverlay?.Close();
        ShortcutPaneContent.Children.Clear();
        _shortcutModPicker = null;
        _mainBindings = []; _mainShortcutControls.Clear(); _overlayShortcutControls.Clear();
        BuildShortcutPane();
        if (double.IsFinite(tap)) _shortcutTapDuration!.Value = tap;
        if (double.IsFinite(hold)) _shortcutHoldDuration!.Value = hold;
        // Input stays disarmed after the rebuild; the user re-enables it deliberately.
        if (target is not null && _shortcutTargetPicker!.ItemsSource is IReadOnlyList<ShortcutTarget> targets)
            _shortcutTargetPicker.SelectedItem = targets.FirstOrDefault(t => t.Handle == target.Handle && t.StartTicks == target.StartTicks);
        _ = RefreshShortcutPaneSafelyAsync();
        if (overlayVisible) OpenShortcutOverlay();
    }

    // ToggleSwitch otherwise shows On/Off in the Windows display language, not the app language.
    private ToggleSwitch LocalizedToggle(string header) => new() { Header = header, OnContent = T("Common.On2"), OffContent = T("Common.Off2") };

    private Button ShortcutButton(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }

    private void BuildShortcutPane()
    {
        if (_shortcutModPicker is not null) return;
        var pane = ShortcutPaneContent;
        pane.Children.Add(new TextBlock { Text = T("Common.ModShortcuts"), FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        pane.Children.Add(new TextBlock
        {
            Text = T("ShortcutPanel.SelectAModAndGameWindow"),
            TextWrapping = TextWrapping.Wrap
        });
        _shortcutModPicker = new ComboBox { Header = T("ShortcutPanel.ModCurrentRepository"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _shortcutModPicker.SelectionChanged += async (_, _) => await RunUiActionAsync("Select shortcut Mod", async () =>
        {
            if (_refreshingShortcutOptions) return;
            if (!_characterAutoSelecting)
            {
                if (_characterFollowToggle is not null) _characterFollowToggle.IsOn = false;
                if (SelectedShortcutRole() is string role && _shortcutModPicker.SelectedItem is ShortcutModOption selected)
                    _characterPreferredMods[role] = selected.Path;
            }
            await LoadShortcutSelectionAsync();
        });
        pane.Children.Add(_shortcutModPicker);
        _shortcutTargetPicker = new ComboBox { Header = T("ShortcutPanel.TargetGameWindow"), PlaceholderText = T("ShortcutPanel.SelectAWindow"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _shortcutTargetPicker.SelectionChanged += (_, _) =>
        {
            // A newly selected target always requires deliberate re-arming.
            _shortcutInputEnabled = false;
            if (_shortcutEnabledToggle is not null) _shortcutEnabledToggle.IsOn = false;
            UpdateShortcutPrivilegeNotice();
            RefreshShortcutOverlay();
        };
        pane.Children.Add(_shortcutTargetPicker);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(ShortcutButton(T("ShortcutPanel.RefreshWindows"), RefreshShortcutTargets));
        tools.Children.Add(ShortcutButton(T("ShortcutPanel.ReloadINI"), () => _ = RefreshOverlayFromDiskAsync()));
        tools.Children.Add(ShortcutButton(T("ShortcutPanel.FloatingPanel"), OpenShortcutOverlay));
        pane.Children.Add(tools);
        _shortcutPrivilegeNotice = new TextBlock { TextWrapping = TextWrapping.Wrap };
        pane.Children.Add(_shortcutPrivilegeNotice);
        _shortcutElevateButton = ShortcutButton(T("ShortcutPanel.RestartManagerAsAdministrator"), RestartManagerElevated);
        pane.Children.Add(_shortcutElevateButton);
        UpdateShortcutPrivilegeNotice();
        var hotkeySettings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var modifiers = new ComboBox { Header = T("ShortcutPanel.ShowHideOverlay") };
        foreach (var item in new[] { ("Ctrl+Alt", 3u), ("Ctrl+Shift", 6u), ("Alt+Shift", 5u) })
            modifiers.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
        modifiers.SelectedItem = modifiers.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == _shortcutPanelHotkeyModifiers);
        var keyPicker = new ComboBox { Header = T("ShortcutPanel.Key2") };
        foreach (uint key in Enumerable.Range('A', 26).Concat(Enumerable.Range(0x70, 12)).Select(k => (uint)k))
            keyPicker.Items.Add(new ComboBoxItem { Content = key >= 0x70 ? "F" + (key - 0x6F) : ((char)key).ToString(), Tag = key });
        keyPicker.SelectedItem = keyPicker.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == _shortcutPanelHotkeyKey);
        hotkeySettings.Children.Add(modifiers);
        hotkeySettings.Children.Add(keyPicker);
        hotkeySettings.Children.Add(ShortcutButton(T("Common.Apply"), () =>
        {
            uint mod = (uint)((ComboBoxItem)modifiers.SelectedItem).Tag;
            uint key = (uint)((ComboBoxItem)keyPicker.SelectedItem).Tag;
            if (_shortcutHotkeyRegistered && mod == _shortcutPanelHotkeyModifiers && key == _shortcutPanelHotkeyKey) return;
            if (_shortcutPanelHotkey?.TrySet(mod, key) == true)
            {
                _shortcutPanelHotkeyModifiers = mod;
                _shortcutPanelHotkeyKey = key;
                _shortcutPanelHotkeyEnabled = true;
                _shortcutHotkeyRegistered = true;
                SaveShellConfig();
                UpdateShortcutHotkeyStatus();
            }
            else _shortcutHotkeyStatus!.Text = T("ShortcutPanel.HotkeyUnavailablePreviousSettingsRetainedChoose");
        }));
        hotkeySettings.Children.Add(ShortcutButton(T("ShortcutPanel.Disable"), () =>
        {
            _shortcutPanelHotkey?.Disable();
            _shortcutPanelHotkeyEnabled = false;
            _shortcutHotkeyRegistered = false;
            SaveShellConfig();
            UpdateShortcutHotkeyStatus();
        }));
        pane.Children.Add(hotkeySettings);
        _shortcutHotkeyStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        pane.Children.Add(_shortcutHotkeyStatus);
        UpdateShortcutHotkeyStatus();
        _shortcutEnabledToggle = LocalizedToggle(T("ShortcutPanel.EnableInputToSelectedWindow"));
        _shortcutEnabledToggle.Toggled += (_, _) =>
        {
            _shortcutInputEnabled = _shortcutEnabledToggle.IsOn && _shortcutTargetPicker.SelectedItem is ShortcutTarget;
            if (!_shortcutInputEnabled) _shortcutEnabledToggle.IsOn = false;
            RefreshShortcutOverlay();
        };
        pane.Children.Add(_shortcutEnabledToggle);
        _shortcutInputModePicker = new ComboBox { Header = T("ShortcutPanel.InputMethod"), HorizontalAlignment = HorizontalAlignment.Left };
        _shortcutInputModePicker.Items.Add(new ComboBoxItem { Content = T("ShortcutPanel.ScanCodesPhysicalKeySimulation"), Tag = ShortcutInputMode.ScanCode });
        _shortcutInputModePicker.Items.Add(new ComboBoxItem { Content = T("ShortcutPanel.VirtualKeysLegacyCompatibility"), Tag = ShortcutInputMode.VirtualKey });
        _shortcutInputModePicker.SelectedIndex = _shortcutInputMode == ShortcutInputMode.VirtualKey ? 1 : 0;
        _shortcutInputModePicker.SelectionChanged += (_, _) =>
        {
            if (_shortcutInputModePicker.SelectedItem is ComboBoxItem { Tag: ShortcutInputMode mode })
            { _shortcutInputMode = mode; SaveShellConfig(); }
        };
        pane.Children.Add(_shortcutInputModePicker);
        _shortcutTapDuration = new NumberBox
        {
            Header = T("ShortcutPanel.TapDurationMs"), Minimum = 100, Maximum = 2000, Value = 200,
            SmallChange = 100, Width = 260, HorizontalAlignment = HorizontalAlignment.Left,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        pane.Children.Add(_shortcutTapDuration);
        pane.Children.Add(ShortcutButton(T("ShortcutPanel.ToggleNumLock"), async () => await SendNumLockAsync()));
        _shortcutHoldDuration = new NumberBox
        {
            Header = T("ShortcutPanel.HoldTypeDurationMs"),
            Minimum = 100, Maximum = 2000, Value = 300, SmallChange = 100, Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        pane.Children.Add(_shortcutHoldDuration);
        _shortcutOpacitySlider = CreateOverlayOpacitySlider(T("ShortcutPanel.OverlayOpacity"));
        _shortcutOpacitySlider.Width = 260;
        _shortcutOpacitySlider.HorizontalAlignment = HorizontalAlignment.Left;
        pane.Children.Add(_shortcutOpacitySlider);
        pane.Children.Add(BuildOverlayTextColorButton());
        var hideUnsupported = new ToggleSwitch
        {
            OnContent = T("Common.On2"), OffContent = T("Common.Off2"),
            Header = T("ShortcutPanel.HideBindingsThatCannotBeSent"),
            IsOn = _shortcutHideUnsupported
        };
        hideUnsupported.Toggled += (_, _) =>
        {
            _shortcutHideUnsupported = hideUnsupported.IsOn;
            SaveShellConfig();
            RenderShortcutButtons();
        };
        pane.Children.Add(hideUnsupported);
        AddOverlaySlotSwitchSetting(pane);
        BuildCharacterFollowControls(pane);
        pane.Children.Add(new TextBlock
        {
            Text = T("ShortcutPanel.TheOverlaySupportsWindowedBorderlessMode"),
            TextWrapping = TextWrapping.Wrap, Opacity = .7
        });
        // Fixed height: status changes must not move the shortcut list below it.
        _shortcutStatus = FixedStatusText(lines: 3, fontSize: 14);
        pane.Children.Add(_shortcutStatus);
        _shortcutButtons = new StackPanel { Spacing = 8 };
        pane.Children.Add(_shortcutButtons);
        RefreshShortcutTargets();
    }

    private async Task RefreshShortcutModsAsync()
    {
        if (_shortcutModPicker is null) return;
        string repository = GetSelectedRepository()?.SourcePath ?? string.Empty;
        string? previous = (_shortcutModPicker.SelectedItem as ShortcutModOption)?.Path ?? _shortcutRestoreModPath ?? _currentSecondLevelPath;
        _shortcutRestoreModPath = null;
        int revision = ++_shortcutScanRevision;
        _shortcutInputEnabled = false;
        _shortcutEnabledToggle!.IsOn = false;
        _shortcutDetected = [];
        _shortcutLoadedPath = string.Empty;
        RenderShortcutButtons();
        _shortcutStatus!.Text = T("ShortcutPanel.ReadingCurrentRepository");
        ShortcutModOption[] options = await Task.Run(() => ReadShortcutModOptions(repository));
        if (_onlineWindowClosed || revision != _shortcutScanRevision) return;
        _shortcutModPicker.ItemsSource = options;
        _ = RefreshShortcutDeployedModsAsync();
        LoadCharacterFollowProfile(repository);
        _shortcutModPicker.SelectedItem = options.FirstOrDefault(p => string.Equals(p.Path, previous, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        if (options.Length == 0) _shortcutStatus.Text = T("ShortcutPanel.NoModsFoundSelectARepository");
    }

    private async Task RefreshShortcutPaneSafelyAsync()
    {
        try { await RefreshShortcutModsAsync(); }
        catch (Exception ex) { if (!_onlineWindowClosed && _shortcutStatus is not null) _shortcutStatus.Text = ex.Message; }
    }

    private async Task LoadShortcutSelectionAsync(bool force = false)
    {
        int revision = ++_shortcutScanRevision;
        _shortcutDetected = [];
        _shortcutLoadedPath = string.Empty;
        RenderShortcutButtons();
        if (_shortcutModPicker?.SelectedItem is not ShortcutModOption mod) { RefreshShortcutOverlay(bindingsChanged: true); return; }
        try
        {
            _shortcutStatus!.Text = T("ShortcutPanel.ReadingINI");
            var detected = await Task.Run(() => _shortcutScanCache.Scan(mod.Path, force));
            if (_onlineWindowClosed || revision != _shortcutScanRevision) return;
            _shortcutDetected = detected;
            _shortcutLoadedPath = mod.Path;
            _shortcutStatus.Text = T("ShortcutPanel.ReadBindingsInGameConditionsAre", detected.Count);
            RenderShortcutButtons();
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Read shortcut panel INI", ex);
            if (!_onlineWindowClosed && revision == _shortcutScanRevision) _shortcutStatus!.Text = ex.Message;
        }
    }

    private void RefreshShortcutTargets()
    {
        if (_shortcutTargetPicker is null) return;
        var previous = _shortcutTargetPicker.SelectedItem as ShortcutTarget;
        var targets = ShortcutInputService.ListTargets();
        _shortcutTargetPicker.ItemsSource = targets;
        ShortcutTarget? kept = targets.FirstOrDefault(t => t.Handle == previous?.Handle && t.StartTicks == previous.StartTicks);
        // Without a (still running) chosen window, pick the game window. Input stays off until the
        // user turns it on: selecting a target never arms it.
        int detected = kept is null
            ? GameWindowDetection.Find(targets.Select(t => new GameWindowCandidate(t.ProcessName, t.Label)).ToArray(),
                PreferredGameProcesses(GetSelectedRepository()))
            : -1;
        _shortcutTargetPicker.SelectedItem = detected >= 0 ? targets[detected] : kept;
        if (detected >= 0 && _shortcutStatus is not null)
            _shortcutStatus.Text = T("ShortcutPanel.GameWindowDetected", targets[detected].Label);
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

    private DetectedModShortcut[] VisibleShortcuts() => _shortcutHideUnsupported
        ? _shortcutDetected.Where(s => ShortcutKeyPlan.TryCreate(s.RawKey, out _)).ToArray()
        : _shortcutDetected.ToArray();

    private void RenderShortcutButtons()
    {
        RefreshShortcutOverlay(bindingsChanged: true);
        if (_shortcutButtons is null || _currentPrimarySection != PrimarySection.Shortcuts) return;
        DetectedModShortcut[] visible = VisibleShortcuts();
        if (_mainBindings.SequenceEqual(visible)) return;
        _mainBindings = visible;
        _shortcutButtons.Children.Clear();
        int hidden = _shortcutDetected.Count - visible.Length;
        if (hidden > 0)
            _shortcutButtons.Children.Add(new TextBlock
            {
                Text = T("ShortcutPanel.BindingsThatCannotBeSentAre", hidden),
                Opacity = .65, TextWrapping = TextWrapping.Wrap
            });
        foreach (var shortcut in visible)
        {
            if (!_mainShortcutControls.TryGetValue(shortcut, out Button? control))
                _mainShortcutControls[shortcut] = control = CreateShortcutAction(shortcut);
            _shortcutButtons.Children.Add(control);
        }
        if (_mainShortcutControls.Count > 768) _mainShortcutControls.Clear();
    }

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

    private string OverlayTextColorValue => $"#{_overlayTextBrush.Color.R:X2}{_overlayTextBrush.Color.G:X2}{_overlayTextBrush.Color.B:X2}";

    private Button BuildOverlayTextColorButton()
    {
        var picker = new ColorPicker
        {
            Color = _overlayTextBrush.Color, IsAlphaEnabled = false, IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false, IsHexInputVisible = true
        };
        picker.ColorChanged += (_, e) => _overlayTextBrush.Color = e.NewColor;
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
            SmallChange = 5, Value = _shortcutOverlayOpacity
        };
        // WinUI raises ValueChanged while the template loads (observed resetting to the minimum);
        // only react to changes after the slider is live, and restore the saved value first.
        bool live = false;
        slider.Loaded += (_, _) =>
        {
            if (live) return;
            slider.Value = _shortcutOverlayOpacity;
            live = true;
        };
        slider.ValueChanged += (_, e) =>
        {
            if (!live || _shortcutOpacitySyncing || (int)Math.Round(e.NewValue) == _shortcutOverlayOpacity) return;
            _shortcutOverlayOpacity = (int)Math.Round(e.NewValue);
            _shortcutOpacitySyncing = true;
            try
            {
                foreach (Slider other in new[] { _shortcutOpacitySlider, _overlayOpacitySlider }.OfType<Slider>())
                    if (other != slider) other.Value = _shortcutOverlayOpacity;
            }
            finally { _shortcutOpacitySyncing = false; }
            _shortcutOverlay?.SetBackgroundOpacity(_shortcutOverlayOpacity);
            SaveShellConfig();
        };
        return slider;
    }

    private Button CreateShortcutAction(DetectedModShortcut shortcut, bool floating = false)
    {
        ShortcutUnsupportedReason reason = ShortcutKeyPlan.Explain(shortcut.RawKey);
        bool supported = reason == ShortcutUnsupportedReason.None;
        var content = new StackPanel { Spacing = 3 };
        var title = new TextBlock { Text = shortcut.Shortcut, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var description = new TextBlock { Text = DescribeDetectedShortcut(shortcut), TextWrapping = TextWrapping.Wrap };
        string source = $"{shortcut.SourceFile} · [{shortcut.Section}]";
        if (shortcut.Condition.Length > 0) source += " · " + T("ShortcutPanel.Condition") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + T("ShortcutPanel.HoldForConfiguredDuration");
        var sourceText = new TextBlock { Text = source, FontSize = 11, Opacity = .65, TextWrapping = TextWrapping.Wrap };
        if (floating) title.Foreground = description.Foreground = sourceText.Foreground = _overlayTextBrush;
        content.Children.Add(title); content.Children.Add(description); content.Children.Add(sourceText);
        if (!supported)
            content.Children.Add(new TextBlock
            {
                Text = T("ShortcutPanel.CannotSend") + UnsupportedReasonText(reason) + T("ShortcutPanel.Key", shortcut.RawKey),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 150, 60))
            });
        var button = new Button { Content = content, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 10, 14, 10) };
        if (floating) button.AllowFocusOnInteraction = false;
        button.Click += async (_, _) => await RunUiActionAsync("Send shortcut", () => SendShortcutFromPanelAsync(shortcut));
        return button;
    }

    private async Task SendShortcutFromPanelAsync(DetectedModShortcut shortcut)
    {
        if (_shortcutSendInProgress) return;
        _shortcutSendInProgress = true;
        try
        {
            if (!_shortcutInputEnabled || _shortcutTargetPicker?.SelectedItem is not ShortcutTarget target)
                throw new InvalidOperationException(T("ShortcutPanel.SelectATargetWindowAndEnable"));
            if (!Directory.Exists(_shortcutLoadedPath) || Path.GetFileName(_shortcutLoadedPath).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("ShortcutPanel.TheModFolderChangedReloadIt"));
            if (!ShortcutKeyPlan.TryCreate(shortcut.RawKey, out var plan)) return;
            int hold = shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)
                ? (int)(double.IsFinite(_shortcutHoldDuration!.Value) ? _shortcutHoldDuration.Value : 300)
                : (int)(double.IsFinite(_shortcutTapDuration!.Value) ? _shortcutTapDuration.Value : 200);
            string report = await _shortcutInput.SendAsync(target, plan!, hold, _shortcutInputMode, IsEnglishUi);
            if (!_onlineWindowClosed) _shortcutStatus!.Text = shortcut.Shortcut + " · " + report;
        }
        catch (Exception ex)
        {
            if (!_onlineWindowClosed) _shortcutStatus!.Text = ex.Message;
        }
        finally { _shortcutSendInProgress = false; }
        if (!_onlineWindowClosed) RefreshShortcutOverlay();
    }

    private void OpenShortcutOverlay()
    {
        try
        {
            if (_shortcutOverlay is null)
            {
                var overlay = new ShortcutOverlayWindow(BuildShortcutOverlayContent(), _overlayTextBrush, T("Common.ModShortcuts"));
                _shortcutOverlay = overlay;
                _shortcutOverlay.Closed += (_, _) =>
                {
                    // A language rebuild may already have opened a replacement overlay.
                    if (!ReferenceEquals(_shortcutOverlay, overlay)) return;
                    _shortcutOverlay = null; _shortcutOverlayVisible = false;
                    _overlayButtonList = null; _overlayScroll = null; _overlayBindings = []; _overlayOpacitySlider = null;
                };
                _shortcutOverlay.SetBackgroundOpacity(_shortcutOverlayOpacity);
            }
            // The game may have started after the window list was read.
            if (_shortcutTargetPicker?.SelectedItem is not ShortcutTarget) RefreshShortcutTargets();
            _shortcutOverlay.ShowPanel();
            _shortcutOverlayVisible = true;
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
        // The overlay window draws the (translucent) background; this content stays transparent.
        var root = new Grid { Padding = new Thickness(14, 4, 14, 14), RowSpacing = 10, RequestedTheme = ElementTheme.Dark };
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
            if (_overlaySyncing || _shortcutModPicker is null || _overlayModPicker.SelectedItem is not ShortcutModOption chosen) return;
            if (!ReferenceEquals(_shortcutModPicker.SelectedItem, chosen)) _shortcutModPicker.SelectedItem = chosen;
        };
        _overlayTargetText = FixedStatusText(lines: 1, fontSize: 11);
        _overlayStatusText = FixedStatusText(lines: 2, fontSize: 11);
        _overlayTargetText.Foreground = _overlayStatusText.Foreground = _overlayTextBrush;
        panel.Children.Add(_overlayModPicker); panel.Children.Add(_overlayTargetText);
        var actions = new Grid { ColumnSpacing = 6 };
        for (int i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _overlayInputToggle = new ToggleButton { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = _overlayTextBrush };
        ToolTipService.SetToolTip(_overlayInputToggle, T("ShortcutPanel.ArmOrPauseInputToThe"));
        _overlayInputToggle.Click += (_, _) => SetShortcutInputFromOverlay(_overlayInputToggle.IsChecked == true);
        var savePreset = ShortcutButton(T("ShortcutPanel.SavePreset"), () => _ = QuickSaveCombinationFromOverlayAsync());
        ToolTipService.SetToolTip(savePreset, T("ShortcutPanel.SaveTheEnabledModCombinationAnd"));
        var hide = ShortcutButton(T("ShortcutPanel.Hide"), () => { _shortcutOverlay?.AppWindow.Hide(); _shortcutOverlayVisible = false; });
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
        opacityRow.Children.Add(opacityLabel); opacityRow.Children.Add(_overlayOpacitySlider);
        panel.Children.Add(opacityRow);
        AddOverlaySlotSection(panel);
        panel.Children.Add(_overlayStatusText);
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
        if (_shortcutModPicker is null) return;
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
        _shortcutDeployedMods = deployed;
        _overlayModOptionsSource = null;
        var current = _shortcutModPicker.SelectedItem as ShortcutModOption;
        var previous = _shortcutModPicker.ItemsSource as ShortcutModOption[] ?? [];
        // Retain record identity so unchanged lists and selections do not trigger a rescan.
        var byPath = previous.ToDictionary(option => option.Path, StringComparer.OrdinalIgnoreCase);
        var options = fresh.Select(option => byPath.GetValueOrDefault(option.Path) ?? option).ToArray();
        ShortcutModOption? chosen = current is null ? null : options.FirstOrDefault(option => option.Path == current.Path);
        if (current is not null && (chosen is null || !IsShortcutModDeployed(chosen)))
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
            if (!previous.SequenceEqual(options)) _shortcutModPicker.ItemsSource = options;
            _shortcutModPicker.SelectedItem = chosen;
        }
        finally { _refreshingShortcutOptions = false; }
        if (!ReferenceEquals(current, chosen)) await LoadShortcutSelectionAsync();
        if (chosen is null && current is not null) _shortcutStatus!.Text = T("ShortcutCapture.CurrentRemoved");
        RefreshShortcutOverlay(bindingsChanged: true);
    }

    private bool IsShortcutModDeployed(ShortcutModOption option) =>
        _shortcutDeployedMods?.Contains(option.Label) != false;

    private void SetShortcutInputFromOverlay(bool enable)
    {
        if (_shortcutEnabledToggle is null) return;
        // Arming needs a target: look for the game window first; the overlay cannot pick other windows.
        if (enable && _shortcutTargetPicker?.SelectedItem is not ShortcutTarget) RefreshShortcutTargets();
        if (enable && _shortcutTargetPicker?.SelectedItem is not ShortcutTarget)
            _shortcutStatus!.Text = T("ShortcutPanel.SelectTheTargetGameWindowIn");
        else
            _shortcutEnabledToggle.IsOn = enable;
        RefreshShortcutOverlay();
    }

    /// <summary>Flush loader memory before capturing; offer an explicit disk-only fallback when input is unavailable.</summary>
    private async Task QuickSaveCombinationFromOverlayAsync()
    {
        if (_savingOverlaySinglePreset || _applyingCombinationProfile || (_shortcutSendInProgress && !_restoringPersistentRuntime)) return;
        if (GetSelectedRepository() is not { } repository)
        {
            _shortcutStatus!.Text = T("ShortcutPanel.SelectARepositoryInTheMain");
            RefreshShortcutOverlay();
            return;
        }
        _savingOverlaySinglePreset = true;
        if (!await WaitForOverlayCaptureAsync()) { _savingOverlaySinglePreset = false; return; }
        string? userIni = repository.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        bool includeState = userIni is not null && File.Exists(userIni);
        string name = T("ShortcutPanel.InGame", DateTime.Now);
        _shortcutStatus!.Text = T("ShortcutPanel.SavingCombinationPreset");
        RefreshShortcutOverlay();
        SetCombinationBusy(true);
        try
        {
            if (!includeState) throw new InvalidDataException(T("HotReload.SetTheUserIniFirst"));
            if (!await PrepareOverlayCaptureAsync(userIni!)) return;
            (int Mods, int Parameters)? saved = await CaptureCombinationProfileAsync(repository, name, null, includeState, userIni ?? string.Empty,
                () => Task.FromResult(false));
            _shortcutStatus.Text = saved is (int mods, int parameters)
                ? T("ShortcutPanel.SavedPresetModsParameters", name, mods, parameters)
                : T("ShortcutPanel.NoEnabledModsThePresetWas");
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay combination capture", ex);
            if (!_onlineWindowClosed) _shortcutStatus.Text = T("ShortcutPanel.PresetNotSaved") + OverlayCaptureError(ex);
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
        if (_shortcutOverlay is not null && _shortcutOverlayVisible) UpdateShortcutOverlayControls(bindingsChanged);
    }

    private void UpdateShortcutOverlayControls(bool bindingsChanged = true)
    {
        if (_overlayButtonList is null) return;
        _overlaySyncing = true;
        try
        {
            // In game only enabled Mods matter, so the overlay lists those; it forwards choices to the main picker.
            object? source = _shortcutModPicker?.ItemsSource;
            if (!ReferenceEquals(_overlayModOptionsSource, source))
            {
                _overlayModOptionsSource = source;
                _overlayModOptions = (source as ShortcutModOption[] ?? []).Where(IsShortcutModDeployed).ToArray();
                _overlayModPicker!.ItemsSource = _overlayModOptions;
            }
            var current = _shortcutModPicker?.SelectedItem as ShortcutModOption;
            _overlayModPicker!.SelectedItem = current is not null && _overlayModOptions.Contains(current) ? current : null;
            _overlayModPicker.PlaceholderText = _overlayModOptions.Length == 0
                ? T("ShortcutPanel.NoEnabledMods")
                : current is null ? T("ShortcutPanel.SelectAMod") : T("ShortcutPanel.CurrentModNotEnabledPickAn");
            _overlayInputToggle!.IsChecked = _shortcutInputEnabled;
            _overlayInputToggle.Content = _shortcutInputEnabled ? T("ShortcutPanel.InputOn") : T("ShortcutPanel.InputOff");
        }
        finally { _overlaySyncing = false; }
        _overlayTargetText!.Text = (_shortcutTargetPicker?.SelectedItem as ShortcutTarget)?.Label ?? T("ShortcutPanel.NoTargetWindowSelectItIn");
        _overlayStatusText!.Text = _shortcutStatus?.Text ?? string.Empty;
        // Status-only updates never enumerate bindings or rebuild slots.
        if (!bindingsChanged) return;
        UpdateOverlaySlotSection();
        bool enabled = _shortcutModPicker?.SelectedItem is ShortcutModOption selected && IsShortcutModDeployed(selected);
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
        if (_shortcutSendInProgress) return;
        _shortcutSendInProgress = true;
        try
        {
            if (!_shortcutInputEnabled || _shortcutTargetPicker?.SelectedItem is not ShortcutTarget target)
                throw new InvalidOperationException(T("ShortcutPanel.SelectATargetWindowAndEnable"));
            _shortcutStatus!.Text = await _shortcutInput.SendAsync(target, new ShortcutKeyPlan(new ushort[] { 0x90 }, Array.Empty<ushort>()), 150, ShortcutInputMode.VirtualKey, IsEnglishUi);
        }
        catch (Exception ex) { if (!_onlineWindowClosed) _shortcutStatus!.Text = ex.Message; }
        finally { _shortcutSendInProgress = false; if (!_onlineWindowClosed) RefreshShortcutOverlay(); }
    }

    private void CloseShortcutPanel()
    {
        ++_shortcutScanRevision;
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
            if (_shortcutPanelHotkeyEnabled)
                _shortcutHotkeyRegistered = _shortcutPanelHotkey.TrySet(_shortcutPanelHotkeyModifiers, _shortcutPanelHotkeyKey);
            UpdateShortcutHotkeyStatus();
        }
        catch (Exception ex) { LogApplicationIssue("Shortcut panel hotkey", ex); }
    }

    private async void ToggleShortcutOverlay()
    {
        try
        {
            if (_onlineWindowClosed || _savingOverlaySinglePreset) return;
            if (_shortcutOverlayVisible)
            {
                _shortcutOverlay?.AppWindow.Hide();
                _shortcutOverlayVisible = false;
                return;
            }
            if (_shortcutModPicker is null)
            {
                BuildShortcutPane();
                await RefreshShortcutModsAsync();
                if (_onlineWindowClosed) return;
            }
            OpenShortcutOverlay();
        }
        catch (Exception ex) { LogApplicationIssue("Toggle shortcut overlay", ex); }
    }

    private void UpdateShortcutPrivilegeNotice()
    {
        if (_shortcutPrivilegeNotice is null || _shortcutElevateButton is null) return;
        ShortcutSecurity own = ShortcutProcessSecurity.Current;
        bool elevated = own.UiAccess || ShortcutPrivilegePolicy.IsElevated(own.Integrity);
        _shortcutElevateButton.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;
        if (elevated)
        {
            _shortcutPrivilegeNotice.Text = T("ShortcutPanel.TheManagerIsElevatedAndCan");
            return;
        }
        if (_shortcutTargetPicker?.SelectedItem is not ShortcutTarget target)
        {
            _shortcutPrivilegeNotice.Text = T("ShortcutPanel.EndfieldRunsAsAdministratorInputFrom");
            return;
        }
        ShortcutSecurity destination = ShortcutProcessSecurity.Read(target.ProcessId);
        _shortcutPrivilegeNotice.Text = ShortcutPrivilegePolicy.Evaluate(own.Integrity, own.UiAccess, destination.Integrity, destination.Error) == ShortcutPrivilegeVerdict.Allowed
            ? T("ShortcutPanel.TheSelectedWindowRunsAtThe")
            : T("ShortcutPanel.TheSelectedWindowRunsAsAdministrator");
    }

    private void RestartManagerElevated()
    {
        try
        {
            // The new process waits for this one to exit before taking the single-instance key.
            Process.Start(new ProcessStartInfo(LaunchExecutablePath, $"{Program.RestartAfterArgument} {Environment.ProcessId}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _shortcutStatus!.Text = T("ShortcutPanel.AdministratorApprovalWasDeclinedTheManager");
            return;
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(RestartManagerElevated), ex); return; }
        Close();
    }

    private void UpdateShortcutHotkeyStatus()
    {
        if (_shortcutHotkeyStatus is null) return;
        string mod = _shortcutPanelHotkeyModifiers switch { 6 => "Ctrl+Shift", 5 => "Alt+Shift", _ => "Ctrl+Alt" };
        string key = _shortcutPanelHotkeyKey >= 0x70 ? "F" + (_shortcutPanelHotkeyKey - 0x6F) : ((char)_shortcutPanelHotkeyKey).ToString();
        _shortcutHotkeyStatus.Text = _shortcutHotkeyRegistered
            ? T("ShortcutPanel.GlobalHotkeyShowHideOverlay", mod, key)
            : T("ShortcutPanel.GlobalHotkeyDisabledOrUnavailableChoose");
    }
}
