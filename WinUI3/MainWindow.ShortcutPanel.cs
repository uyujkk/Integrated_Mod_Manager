using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private readonly ShortcutInputService _shortcutInput = new();
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
    private TextBlock? _overlayModText, _overlayTargetText, _overlayEnabledText, _overlayStatusText;
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
        pane.Children.Add(new TextBlock { Text = L("Mod 快捷键", "Mod shortcuts"), FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        pane.Children.Add(new TextBlock
        {
            Text = L("选择 Mod 和游戏窗口，点击按键按钮即可发送。INI 条件仍由游戏内加载器判断。",
                "Select a Mod and game window, then click a shortcut to send it. INI conditions are still evaluated by the in-game loader."),
            TextWrapping = TextWrapping.Wrap
        });
        _shortcutModPicker = new ComboBox { Header = L("Mod（当前仓库）", "Mod (current repository)"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _shortcutModPicker.SelectionChanged += async (_, _) =>
        {
            if (!_characterAutoSelecting)
            {
                if (_characterFollowToggle is not null) _characterFollowToggle.IsOn = false;
                if (SelectedShortcutRole() is string role && _shortcutModPicker.SelectedItem is ShortcutModOption selected)
                    _characterPreferredMods[role] = selected.Path;
            }
            await LoadShortcutSelectionAsync();
        };
        pane.Children.Add(_shortcutModPicker);
        _shortcutTargetPicker = new ComboBox { Header = L("目标游戏窗口", "Target game window"), PlaceholderText = L("请选择窗口", "Select a window"), HorizontalAlignment = HorizontalAlignment.Stretch };
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
        tools.Children.Add(ShortcutButton(L("刷新窗口", "Refresh windows"), RefreshShortcutTargets));
        tools.Children.Add(ShortcutButton(L("重新读取 INI", "Reload INI"), () => _ = LoadShortcutSelectionAsync()));
        tools.Children.Add(ShortcutButton(L("独立置顶面板", "Floating panel"), OpenShortcutOverlay));
        pane.Children.Add(tools);
        _shortcutPrivilegeNotice = new TextBlock { TextWrapping = TextWrapping.Wrap };
        pane.Children.Add(_shortcutPrivilegeNotice);
        _shortcutElevateButton = ShortcutButton(L("以管理员身份重启管理器", "Restart manager as administrator"), RestartManagerElevated);
        pane.Children.Add(_shortcutElevateButton);
        UpdateShortcutPrivilegeNotice();
        var hotkeySettings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var modifiers = new ComboBox { Header = L("显示／隐藏浮窗", "Show/hide overlay") };
        foreach (var item in new[] { ("Ctrl+Alt", 3u), ("Ctrl+Shift", 6u), ("Alt+Shift", 5u) })
            modifiers.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
        modifiers.SelectedItem = modifiers.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == _shortcutPanelHotkeyModifiers);
        var keyPicker = new ComboBox { Header = L("按键", "Key") };
        foreach (uint key in Enumerable.Range('A', 26).Concat(Enumerable.Range(0x70, 12)).Select(k => (uint)k))
            keyPicker.Items.Add(new ComboBoxItem { Content = key >= 0x70 ? "F" + (key - 0x6F) : ((char)key).ToString(), Tag = key });
        keyPicker.SelectedItem = keyPicker.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == _shortcutPanelHotkeyKey);
        hotkeySettings.Children.Add(modifiers);
        hotkeySettings.Children.Add(keyPicker);
        hotkeySettings.Children.Add(ShortcutButton(L("应用", "Apply"), () =>
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
            else _shortcutHotkeyStatus!.Text = L("快捷键被占用，已保留原设置，请换一个组合。", "Hotkey unavailable. Previous settings retained; choose another chord.");
        }));
        hotkeySettings.Children.Add(ShortcutButton(L("禁用", "Disable"), () =>
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
        _shortcutEnabledToggle = new ToggleSwitch { Header = L("允许向所选窗口发送按键", "Enable input to selected window") };
        _shortcutEnabledToggle.Toggled += (_, _) =>
        {
            _shortcutInputEnabled = _shortcutEnabledToggle.IsOn && _shortcutTargetPicker.SelectedItem is ShortcutTarget;
            if (!_shortcutInputEnabled) _shortcutEnabledToggle.IsOn = false;
            RefreshShortcutOverlay();
        };
        pane.Children.Add(_shortcutEnabledToggle);
        _shortcutInputModePicker = new ComboBox { Header = L("输入方式", "Input method"), HorizontalAlignment = HorizontalAlignment.Left };
        _shortcutInputModePicker.Items.Add(new ComboBoxItem { Content = L("扫描码（模拟物理按键）", "Scan codes (physical-key simulation)"), Tag = ShortcutInputMode.ScanCode });
        _shortcutInputModePicker.Items.Add(new ComboBoxItem { Content = L("虚拟键（旧版兼容模式）", "Virtual keys (legacy compatibility)"), Tag = ShortcutInputMode.VirtualKey });
        _shortcutInputModePicker.SelectedIndex = _shortcutInputMode == ShortcutInputMode.VirtualKey ? 1 : 0;
        _shortcutInputModePicker.SelectionChanged += (_, _) =>
        {
            if (_shortcutInputModePicker.SelectedItem is ComboBoxItem { Tag: ShortcutInputMode mode })
            { _shortcutInputMode = mode; SaveShellConfig(); }
        };
        pane.Children.Add(_shortcutInputModePicker);
        _shortcutTapDuration = new NumberBox
        {
            Header = L("单次按压时间（毫秒）", "Tap duration (ms)"), Minimum = 100, Maximum = 2000, Value = 200,
            SmallChange = 100, Width = 260, HorizontalAlignment = HorizontalAlignment.Left,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        pane.Children.Add(_shortcutTapDuration);
        pane.Children.Add(ShortcutButton(L("切换 Num Lock（小键盘数字锁）", "Toggle Num Lock"), async () => await SendNumLockAsync()));
        _shortcutHoldDuration = new NumberBox
        {
            Header = L("长按类按键持续时间（毫秒）", "Hold-type duration (ms)"),
            Minimum = 100, Maximum = 2000, Value = 300, SmallChange = 100, Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        pane.Children.Add(_shortcutHoldDuration);
        _shortcutOpacitySlider = CreateOverlayOpacitySlider(L("浮窗不透明度（%）", "Overlay opacity (%)"));
        _shortcutOpacitySlider.Width = 260;
        _shortcutOpacitySlider.HorizontalAlignment = HorizontalAlignment.Left;
        pane.Children.Add(_shortcutOpacitySlider);
        pane.Children.Add(BuildOverlayTextColorButton());
        var hideUnsupported = new ToggleSwitch
        {
            Header = L("隐藏无法发送的绑定（鼠标、手柄等）", "Hide bindings that cannot be sent (mouse, controller, ...)"),
            IsOn = _shortcutHideUnsupported
        };
        hideUnsupported.Toggled += (_, _) =>
        {
            _shortcutHideUnsupported = hideUnsupported.IsOn;
            SaveShellConfig();
            RenderShortcutButtons();
        };
        pane.Children.Add(hideUnsupported);
        BuildCharacterFollowControls(pane);
        pane.Children.Add(new TextBlock
        {
            Text = L("浮窗支持窗口化／无边框全屏；独占全屏可能遮住浮窗。无法发送的绑定会显示原因；相同按键可能同时触发多个 Mod。",
                "The overlay supports windowed/borderless mode; exclusive fullscreen may cover it. Bindings that cannot be sent show the reason. A shared key can trigger multiple Mods."),
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
        string? previous = (_shortcutModPicker.SelectedItem as ShortcutModOption)?.Path ?? _currentSecondLevelPath;
        int revision = ++_shortcutScanRevision;
        _shortcutInputEnabled = false;
        _shortcutEnabledToggle!.IsOn = false;
        _shortcutDetected = [];
        _shortcutLoadedPath = string.Empty;
        RenderShortcutButtons();
        _shortcutStatus!.Text = L("正在读取当前仓库…", "Reading current repository…");
        ShortcutModOption[] options = await Task.Run(() =>
        {
            if (!Directory.Exists(repository)) return Array.Empty<ShortcutModOption>();
            var enumeration = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return Directory.EnumerateDirectories(repository, "*", enumeration)
                .Where(p => !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal))
                .SelectMany(category => Directory.EnumerateDirectories(category, "*", enumeration)
                    .Where(p => !Path.GetFileName(p).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)
                        && !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal))
                    .Select(p => new ShortcutModOption(p, Path.GetRelativePath(repository, p))))
                .Take(3000).OrderBy(p => p.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        });
        if (_onlineWindowClosed || revision != _shortcutScanRevision) return;
        _shortcutModPicker.ItemsSource = options;
        LoadCharacterFollowProfile(repository);
        _shortcutModPicker.SelectedItem = options.FirstOrDefault(p => string.Equals(p.Path, previous, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        if (options.Length == 0) _shortcutStatus.Text = L("当前仓库没有可读取的 Mod，请先在仓库模块选择仓库。", "No Mods found. Select a repository in Repositories first.");
    }

    private async Task RefreshShortcutPaneSafelyAsync()
    {
        try { await RefreshShortcutModsAsync(); }
        catch (Exception ex) { if (!_onlineWindowClosed && _shortcutStatus is not null) _shortcutStatus.Text = ex.Message; }
    }

    private async Task LoadShortcutSelectionAsync()
    {
        int revision = ++_shortcutScanRevision;
        _shortcutDetected = [];
        _shortcutLoadedPath = string.Empty;
        RenderShortcutButtons();
        if (_shortcutModPicker?.SelectedItem is not ShortcutModOption mod) return;
        try
        {
            _shortcutStatus!.Text = L("正在读取 INI…", "Reading INI…");
            var detected = await Task.Run(() => ModShortcutScanner.ScanDirectory(mod.Path, activeOnly: true));
            if (_onlineWindowClosed || revision != _shortcutScanRevision) return;
            _shortcutDetected = detected;
            _shortcutLoadedPath = mod.Path;
            _shortcutStatus.Text = L($"已读取 {detected.Count} 个绑定；不能确定当前游戏内条件是否成立。", $"Read {detected.Count} bindings; in-game conditions are not known.");
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
        _shortcutTargetPicker.SelectedItem = targets.FirstOrDefault(t => t.Handle == previous?.Handle && t.StartTicks == previous.StartTicks);
    }

    private DetectedModShortcut[] VisibleShortcuts() => _shortcutHideUnsupported
        ? _shortcutDetected.Where(s => ShortcutKeyPlan.TryCreate(s.RawKey, out _)).ToArray()
        : _shortcutDetected.ToArray();

    private void RenderShortcutButtons()
    {
        if (_shortcutButtons is null) return;
        _shortcutButtons.Children.Clear();
        DetectedModShortcut[] visible = VisibleShortcuts();
        int hidden = _shortcutDetected.Count - visible.Length;
        if (hidden > 0)
            _shortcutButtons.Children.Add(new TextBlock
            {
                Text = L($"已隐藏 {hidden} 个无法发送的绑定。", $"{hidden} bindings that cannot be sent are hidden."),
                Opacity = .65, TextWrapping = TextWrapping.Wrap
            });
        foreach (var shortcut in visible) _shortcutButtons.Children.Add(CreateShortcutAction(shortcut));
        RefreshShortcutOverlay();
    }

    private string UnsupportedReasonText(ShortcutUnsupportedReason reason) => reason switch
    {
        ShortcutUnsupportedReason.Mouse => L("鼠标按键：这是 Mod 菜单的点击／拖动操作，需要在游戏内用鼠标直接操作", "Mouse button: a Mod-menu click or drag; use the mouse in game"),
        ShortcutUnsupportedReason.Controller => L("手柄按键：键盘输入无法模拟手柄，请用手柄或同一节里的键盘绑定", "Controller button: keyboard input cannot emulate a controller; use a controller or the section's keyboard binding"),
        ShortcutUnsupportedReason.UnknownKey => L("含无法识别的按键名", "Contains an unrecognized key name"),
        ShortcutUnsupportedReason.Contradictory => L("按键条件互相矛盾（同一个键既要求按下又要求不按）", "Contradictory key constraints"),
        ShortcutUnsupportedReason.TooManyKeys => L("需要同时按住的键过多", "Too many keys held at once"),
        ShortcutUnsupportedReason.SystemShortcut => L("会触发系统操作（如 Alt+F4、Win 键），已禁止发送", "Would trigger a system action (such as Alt+F4 or Win); blocked"),
        _ => L("没有可按下的键", "No key to press")
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
        flyoutContent.Children.Add(new TextBlock { Text = L("常用颜色", "Presets") });
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
        label.Children.Add(new TextBlock { Text = L("浮窗字体颜色", "Overlay text colour"), VerticalAlignment = VerticalAlignment.Center });
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
        if (shortcut.Condition.Length > 0) source += " · " + L("条件：", "Condition: ") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + L("按设定时长长按", "Hold for configured duration");
        var sourceText = new TextBlock { Text = source, FontSize = 11, Opacity = .65, TextWrapping = TextWrapping.Wrap };
        if (floating) title.Foreground = description.Foreground = sourceText.Foreground = _overlayTextBrush;
        content.Children.Add(title); content.Children.Add(description); content.Children.Add(sourceText);
        if (!supported)
            content.Children.Add(new TextBlock
            {
                Text = L("无法发送：", "Cannot send: ") + UnsupportedReasonText(reason) + $"（key = {shortcut.RawKey}）",
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 150, 60))
            });
        var button = new Button { Content = content, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 10, 14, 10) };
        if (floating) button.AllowFocusOnInteraction = false;
        button.Click += async (_, _) => await SendShortcutFromPanelAsync(shortcut);
        return button;
    }

    private async Task SendShortcutFromPanelAsync(DetectedModShortcut shortcut)
    {
        if (_shortcutSendInProgress) return;
        _shortcutSendInProgress = true;
        try
        {
            if (!_shortcutInputEnabled || _shortcutTargetPicker?.SelectedItem is not ShortcutTarget target)
                throw new InvalidOperationException(L("请先选择目标窗口并开启按键发送。", "Select a target window and enable input first."));
            if (!Directory.Exists(_shortcutLoadedPath) || Path.GetFileName(_shortcutLoadedPath).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L("Mod 目录已变化，请重新读取。", "The Mod folder changed; reload it first."));
            if (!ShortcutKeyPlan.TryCreate(shortcut.RawKey, out var plan)) return;
            int hold = shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)
                ? (int)(double.IsFinite(_shortcutHoldDuration!.Value) ? _shortcutHoldDuration.Value : 300)
                : (int)(double.IsFinite(_shortcutTapDuration!.Value) ? _shortcutTapDuration.Value : 200);
            string report = await _shortcutInput.SendAsync(target, plan!, hold, _shortcutInputMode);
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
                _shortcutOverlay = new ShortcutOverlayWindow(BuildShortcutOverlayContent(), _overlayTextBrush);
                _shortcutOverlay.Closed += (_, _) =>
                {
                    _shortcutOverlay = null; _shortcutOverlayVisible = false;
                    _overlayButtonList = null; _overlayScroll = null; _overlayBindings = []; _overlayOpacitySlider = null;
                };
                _shortcutOverlay.SetBackgroundOpacity(_shortcutOverlayOpacity);
            }
            _shortcutOverlay.ShowPanel();
            _shortcutOverlayVisible = true;
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OpenShortcutOverlay), ex); }
    }

    private UIElement BuildShortcutOverlayContent()
    {
        // The overlay window draws the (translucent) background; this content stays transparent.
        var root = new Grid { Padding = new Thickness(14, 4, 14, 14), RowSpacing = 10, RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var panel = new StackPanel { Spacing = 6 };
        // Every header row has a fixed height so status changes never shift the list (row 1).
        _overlayModText = FixedStatusText(lines: 1, fontSize: 17);
        _overlayTargetText = FixedStatusText(lines: 1, fontSize: 11);
        _overlayEnabledText = FixedStatusText(lines: 1, fontSize: 14);
        _overlayStatusText = FixedStatusText(lines: 2, fontSize: 11);
        foreach (TextBlock text in new[] { _overlayModText, _overlayTargetText, _overlayEnabledText, _overlayStatusText })
            text.Foreground = _overlayTextBrush;
        panel.Children.Add(_overlayModText); panel.Children.Add(_overlayTargetText); panel.Children.Add(_overlayEnabledText);
        // One compact row keeps the header short so the list keeps most of the panel.
        var actions = new Grid { ColumnSpacing = 8 };
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var pause = ShortcutButton(L("暂停", "Pause"), () => { _shortcutInputEnabled = false; _shortcutEnabledToggle!.IsOn = false; RefreshShortcutOverlay(); });
        var hide = ShortcutButton(L("隐藏", "Hide"), () => { _shortcutOverlay?.AppWindow.Hide(); _shortcutOverlayVisible = false; });
        pause.Foreground = hide.Foreground = _overlayTextBrush;
        var opacityLabel = new TextBlock { Text = L("不透明", "Opacity"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = _overlayTextBrush };
        _overlayOpacitySlider = CreateOverlayOpacitySlider(string.Empty);
        _overlayOpacitySlider.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(_overlayOpacitySlider, L("浮窗不透明度", "Overlay opacity"));
        Grid.SetColumn(hide, 1); Grid.SetColumn(opacityLabel, 2); Grid.SetColumn(_overlayOpacitySlider, 3);
        actions.Children.Add(pause); actions.Children.Add(hide); actions.Children.Add(opacityLabel); actions.Children.Add(_overlayOpacitySlider);
        panel.Children.Add(actions);
        panel.Children.Add(_overlayStatusText);
        _overlayButtonList = new StackPanel { Spacing = 8 };
        _overlayScroll = new ScrollViewer { Content = _overlayButtonList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_overlayScroll, 1);
        root.Children.Add(panel); root.Children.Add(_overlayScroll);
        _overlayBindings = [];
        UpdateShortcutOverlayControls();
        return root;
    }

    private void RefreshShortcutOverlay()
    {
        if (_shortcutOverlay is not null) UpdateShortcutOverlayControls();
    }

    private void UpdateShortcutOverlayControls()
    {
        if (_overlayButtonList is null) return;
        _overlayModText!.Text = (_shortcutModPicker?.SelectedItem as ShortcutModOption)?.Label ?? L("请选择 Mod", "Select a Mod");
        _overlayTargetText!.Text = (_shortcutTargetPicker?.SelectedItem as ShortcutTarget)?.Label ?? L("未选择目标窗口", "No target window");
        _overlayEnabledText!.Text = _shortcutInputEnabled ? L("按键发送已开启", "Input enabled") : L("已暂停，请在主窗口开启发送", "Paused; enable input in main window");
        _overlayStatusText!.Text = _shortcutStatus?.Text ?? string.Empty;
        // Status, pause and target changes must not replace the scroll container or buttons.
        DetectedModShortcut[] visible = VisibleShortcuts();
        if (_overlayBindings.SequenceEqual(visible)) return;
        _overlayBindings = visible;
        _overlayButtonList.Children.Clear();
        foreach (var shortcut in _overlayBindings) _overlayButtonList.Children.Add(CreateShortcutAction(shortcut, floating: true));
        _overlayScroll?.ChangeView(null, 0, null, true);
    }

    private async Task SendNumLockAsync()
    {
        if (_shortcutSendInProgress) return;
        _shortcutSendInProgress = true;
        try
        {
            if (!_shortcutInputEnabled || _shortcutTargetPicker?.SelectedItem is not ShortcutTarget target)
                throw new InvalidOperationException(L("请先选择目标窗口并开启按键发送。", "Select a target window and enable input first."));
            _shortcutStatus!.Text = await _shortcutInput.SendAsync(target, new ShortcutKeyPlan(new ushort[] { 0x90 }, Array.Empty<ushort>()), 150, ShortcutInputMode.VirtualKey);
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
            if (_onlineWindowClosed) return;
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
            _shortcutPrivilegeNotice.Text = L("管理器以管理员权限运行，可以向管理员权限的游戏发送按键。",
                "The manager is elevated and can send input to an elevated game.");
            return;
        }
        if (_shortcutTargetPicker?.SelectedItem is not ShortcutTarget target)
        {
            _shortcutPrivilegeNotice.Text = L("终末地以管理员权限运行；普通权限的管理器发送的按键会被 Windows 静默丢弃，需要以管理员身份重启管理器。",
                "Endfield runs as administrator; input from a non-elevated manager is silently dropped, so restart the manager as administrator.");
            return;
        }
        ShortcutSecurity destination = ShortcutProcessSecurity.Read(target.ProcessId);
        _shortcutPrivilegeNotice.Text = ShortcutPrivilegePolicy.Evaluate(own.Integrity, own.UiAccess, destination.Integrity, destination.Error) == ShortcutPrivilegeVerdict.Allowed
            ? L("所选窗口与管理器权限相同，可以发送。", "The selected window runs at the manager's permission level.")
            : L("所选窗口以管理员权限运行，当前管理器无法向它发送按键。请以管理员身份重启管理器。",
                "The selected window runs as administrator; this manager cannot send input to it. Restart the manager as administrator.");
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
            _shortcutStatus!.Text = L("已取消管理员授权，管理器保持当前权限。", "Administrator approval was declined; the manager keeps its current permissions.");
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
            ? L($"全局快捷键：{mod}+{key}（显示／隐藏浮窗）", $"Global hotkey: {mod}+{key} (show/hide overlay)")
            : L("全局快捷键未启用或已被占用；可更换组合后应用。", "Global hotkey disabled or unavailable; choose a chord and Apply.");
    }
}
