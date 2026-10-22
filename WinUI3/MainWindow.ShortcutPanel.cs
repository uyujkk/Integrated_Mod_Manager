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
            RefreshShortcutOverlay();
        };
        pane.Children.Add(_shortcutTargetPicker);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(ShortcutButton(L("刷新窗口", "Refresh windows"), RefreshShortcutTargets));
        tools.Children.Add(ShortcutButton(L("重新读取 INI", "Reload INI"), () => _ = LoadShortcutSelectionAsync()));
        tools.Children.Add(ShortcutButton(L("独立置顶面板", "Floating panel"), OpenShortcutOverlay));
        pane.Children.Add(tools);
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
        _shortcutHoldDuration = new NumberBox
        {
            Header = L("长按类按键持续时间（毫秒）", "Hold-type duration (ms)"),
            Minimum = 100, Maximum = 2000, Value = 300, SmallChange = 100, Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        pane.Children.Add(_shortcutHoldDuration);
        BuildCharacterFollowControls(pane);
        pane.Children.Add(new TextBlock
        {
            Text = L("浮窗支持窗口化／无边框全屏；独占全屏可能遮住浮窗。不支持的绑定会禁用；相同按键可能同时触发多个 Mod。",
                "The overlay supports windowed/borderless mode; exclusive fullscreen may cover it. Unsupported bindings are disabled. A shared key can trigger multiple Mods."),
            TextWrapping = TextWrapping.Wrap, Opacity = .7
        });
        _shortcutStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
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

    private void RenderShortcutButtons()
    {
        if (_shortcutButtons is null) return;
        _shortcutButtons.Children.Clear();
        foreach (var shortcut in _shortcutDetected) _shortcutButtons.Children.Add(CreateShortcutAction(shortcut));
        RefreshShortcutOverlay();
    }

    private Button CreateShortcutAction(DetectedModShortcut shortcut)
    {
        bool supported = ShortcutKeyPlan.TryCreate(shortcut.RawKey, out _);
        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(new TextBlock { Text = shortcut.Shortcut, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = DescribeDetectedShortcut(shortcut), TextWrapping = TextWrapping.Wrap });
        string source = $"{shortcut.SourceFile} · [{shortcut.Section}]";
        if (shortcut.Condition.Length > 0) source += " · " + L("条件：", "Condition: ") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + L("按设定时长长按", "Hold for configured duration");
        if (!supported) source += " · " + L("暂不支持发送此绑定", "Sending this binding is unsupported");
        content.Children.Add(new TextBlock { Text = source, FontSize = 11, Opacity = .65, TextWrapping = TextWrapping.Wrap });
        var button = new Button { Content = content, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 10, 14, 10) };
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
                ? (int)(double.IsFinite(_shortcutHoldDuration!.Value) ? _shortcutHoldDuration.Value : 300) : 80;
            await _shortcutInput.SendAsync(target, plan!, hold);
            if (!_onlineWindowClosed) _shortcutStatus!.Text = L($"已发送 {shortcut.Shortcut}；效果由游戏决定。", $"Sent {shortcut.Shortcut}; the game determines its effect.");
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
                _shortcutOverlay = new ShortcutOverlayWindow(BuildShortcutOverlayContent());
                _shortcutOverlay.Closed += (_, _) => { _shortcutOverlay = null; _shortcutOverlayVisible = false; };
            }
            _shortcutOverlay.ShowPanel();
            _shortcutOverlayVisible = true;
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OpenShortcutOverlay), ex); }
    }

    private UIElement BuildShortcutOverlayContent()
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = (_shortcutModPicker?.SelectedItem as ShortcutModOption)?.Label ?? L("请选择 Mod", "Select a Mod"), FontSize = 17, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = (_shortcutTargetPicker?.SelectedItem as ShortcutTarget)?.Label ?? L("未选择目标窗口", "No target window"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = _shortcutInputEnabled ? L("按键发送已开启", "Input enabled") : L("已暂停，请在主窗口开启发送", "Paused; enable input in main window"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(ShortcutButton(L("暂停发送", "Pause input"), () => { _shortcutInputEnabled = false; _shortcutEnabledToggle!.IsOn = false; RefreshShortcutOverlay(); }));
        panel.Children.Add(ShortcutButton(L("隐藏面板", "Hide panel"), () => { _shortcutOverlay?.AppWindow.Hide(); _shortcutOverlayVisible = false; }));
        panel.Children.Add(new TextBlock { Text = _shortcutStatus?.Text ?? string.Empty, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        foreach (var shortcut in _shortcutDetected) panel.Children.Add(CreateShortcutAction(shortcut));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromArgb(255, 28, 31, 38)), RequestedTheme = ElementTheme.Dark };
    }

    private void RefreshShortcutOverlay()
    {
        if (_shortcutOverlay is not null) _shortcutOverlay.Content = BuildShortcutOverlayContent();
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
