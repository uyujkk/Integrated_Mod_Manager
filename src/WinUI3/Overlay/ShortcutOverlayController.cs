using System.ComponentModel;
using System.Numerics;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Shortcuts overlay (stage J5b, docs/development/UI-Structure-Plan.md): its window, its content and how it
/// looks. It shows <see cref="ShortcutPageViewModel"/>, and everything that reaches the game, the loader files or
/// the presets goes through <see cref="IShortcutOverlayActions"/>; it never calls the main window. The window still
/// uses <see cref="Window"/> for captures and layers that must exclude or cover the overlay.
/// Keeps overlay text readable when the window background is very transparent: each control and label has its own
/// backing, free-standing text gets a shadow while the background is below 60%, and optionally the text turns dark
/// over bright scenes (automatic contrast).
/// </summary>
internal sealed class ShortcutOverlayController : IShortcutOverlay
{
    private static readonly Color AutoDarkText = Color.FromArgb(255, 32, 32, 32), AutoLightText = Color.FromArgb(255, 245, 245, 245);

    private readonly ShortcutPageViewModel _shortcuts;
    private readonly LanguageService _language;
    private readonly IShortcutOverlayActions _actions;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<DetectedModShortcut, Button> _bindingControls = new();
    private readonly List<(SpriteVisual Visual, DropShadow Shadow)> _textShadows = [];
    private readonly SolidColorBrush _elementHoverBrush = new();
    private Slider? _opacitySlider;
    private bool _opacitySyncing;
    private TextBlock? _statusText;
    private ComboBox? _modPicker, _targetPicker;
    private ToggleSwitch? _sendSwitch;
    // The title bar: status dot, the Mod's name and close (hide).
    private Grid? _titleBar;
    private Ellipse? _stateDot;
    private TextBlock? _titleText;
    private Button? _closeButton;
    private Button? _refreshTargets;
    private Border? _emptyLabel;
    private TextBlock? _emptyText;
    private Grid? _contentRoot;
    private StackPanel? _headerPanel, _mainPanel;
    private FrameworkElement? _footer;
    private bool _fitQueued;
    // Green when keys go to the game, amber when a window is chosen but sending is off, grey without a window.
    private static readonly Color SendingColor = Color.FromArgb(255, 76, 175, 80), ReadyColor = Color.FromArgb(255, 230, 162, 60),
        NoTargetColor = Color.FromArgb(255, 140, 140, 140);
    private bool _syncing;
    private ShortcutModOption[] _modOptions = [];
    private object? _modOptionsSource;
    private StackPanel? _buttonList;
    private ScrollViewer? _scroll;
    private DetectedModShortcut[] _bindings = [];
    private ToggleSwitch? _compactToggle;
    private Button? _moreButton;
    private Flyout? _moreFlyout;
    private Button? _clickThroughButton;
    private FrameworkElement[] _detailRows = [];
    private bool _clickThrough;
    // Slots of the overlay's Mod, switched in game when hot switching is on.
    private StackPanel? _slotList;
    private ScrollViewer? _slotScroll;
    private TextBlock? _slotHeader;
    private Border? _slotHeaderLabel;
    private Button? _frameButton, _galleryButton;
    private Grid? _slotActions;
    private string _slotSignature = string.Empty;
    // Automatic contrast: the user's colour comes back as soon as the condition ends.
    private Color _userTextColor = Color.FromArgb(255, 255, 255, 255);
    private Color? _autoTextColor;
    private DispatcherQueueTimer? _contrastTimer;

    public ShortcutOverlayController(ShortcutPageViewModel shortcuts, LanguageService language, IShortcutOverlayActions actions, DispatcherQueue dispatcher)
    {
        _shortcuts = shortcuts;
        _language = language;
        _actions = actions;
        _dispatcher = dispatcher;
        _shortcuts.PropertyChanged += OnSettingChanged;
    }

    /// <summary>The overlay window while it exists (shown or hidden).</summary>
    public ShortcutOverlayWindow? Window { get; private set; }

    /// <summary>One shared brush: changing its colour recolours every overlay text at once.</summary>
    public SolidColorBrush TextBrush { get; } = new(Color.FromArgb(255, 255, 255, 255));

    public SolidColorBrush ElementBrush { get; } = new();

    /// <summary>The state slot the overlay marks as current.</summary>
    public string? CurrentSlotId { get; private set; }

    public bool IsVisible => _shortcuts.OverlayVisible;

    public bool IsClickThrough => _clickThrough;

    private string T(string key) => _language.T(key);

    private string T(string key, params object?[] values) => _language.T(key, values);

    internal static Color ParseTextColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && uint.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out uint rgb)
            ? Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : Color.FromArgb(255, 255, 255, 255);

    internal static string FormatTextColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public void Open()
    {
        try
        {
            if (Window is null)
            {
                UIElement content = BuildContent();
                var overlay = new ShortcutOverlayWindow(content, _titleBar!, TitleBarControls, TextBrush, ElementBrush, T("Common.ModShortcuts"),
                    _shortcuts.OverlayPlacements);
                // Moved or resized by the user: remembered for that display (saved with the settings).
                overlay.PlacementChanged += (bounds, display, maxHeight) =>
                    _shortcuts.OverlayPlacements = OverlayLayoutPolicy.Remember(_shortcuts.OverlayPlacements, bounds, display, maxHeight);
                Window = overlay;
                Window.Closed += (_, _) =>
                {
                    // A language rebuild may already have opened a replacement overlay.
                    if (!ReferenceEquals(Window, overlay)) return;
                    Window = null; _shortcuts.OverlayVisible = false; _clickThrough = false;
                    _buttonList = null; _scroll = null; _bindings = []; _opacitySlider = null;
                };
                Window.SetBackgroundOpacity(_shortcuts.OverlayOpacity);
            }
            // The game may have started after the window list was read.
            if (_shortcuts.SelectedTarget is not ShortcutTarget) _actions.RefreshTargets();
            Window.ShowPanel();
            _shortcuts.OverlayVisible = true;
            Refresh(bindingsChanged: true);
            // Mods may have been enabled or disabled since the list was read.
            _ = _actions.RefreshDeployedModsAsync();
        }
        catch (Exception ex) { _actions.ReportFailure("OpenShortcutOverlay", ex); }
    }

    public void Hide()
    {
        Window?.AppWindow.Hide();
        _shortcuts.OverlayVisible = false;
    }

    public async void Toggle()
    {
        try
        {
            if (_actions.IsClosing || _actions.IsSavingFromOverlay) return;
            // The hotkey is the only way back once the overlay ignores the mouse: first press restores it.
            if (_clickThrough)
            {
                SetClickThrough(false);
                return;
            }
            if (_shortcuts.OverlayVisible)
            {
                Hide();
                return;
            }
            await _actions.EnsureShortcutsReadAsync();
            if (_actions.IsClosing) return;
            Open();
        }
        catch (Exception ex) { _actions.LogIssue("Toggle shortcut overlay", ex); }
    }

    public void Refresh(bool bindingsChanged = false)
    {
        if (Window is not null && _shortcuts.OverlayVisible) UpdateControls(bindingsChanged);
    }

    /// <summary>The overlay is built with localized text; a language switch rebuilds it, open again if it was.</summary>
    public void RebuildForLanguage()
    {
        bool visible = _shortcuts.OverlayVisible;
        ShortcutOverlayWindow? old = Window;
        Window = null; _shortcuts.OverlayVisible = false;
        _buttonList = null; _scroll = null; _bindings = []; _opacitySlider = null;
        old?.Close();
        _bindingControls.Clear();
        if (visible) Open();
    }

    public void Close()
    {
        Window?.Close();
        Window = null;
        _contrastTimer?.Stop();
    }

    /// <summary>The slot row is rebuilt on the next refresh even when the slots look the same.</summary>
    public void InvalidateSlots() => _slotSignature = string.Empty;

    /// <summary>The Mod list is filtered again on the next refresh (enabled Mods changed).</summary>
    public void InvalidateModOptions() => _modOptionsSource = null;

    /// <summary>The Mod chosen on the overlay: the page's Mod when it is enabled in game, else none (slots and presets
    /// from the overlay only ever use an enabled Mod).</summary>
    public ShortcutModOption? ModOnOverlay => _modPicker?.SelectedItem is OverlayModChoice { Enabled: true } choice ? choice.Option : null;

    /// <summary>A Mod in the overlay's list: the page's Mod is always there, marked when it is not enabled in game.</summary>
    private sealed record OverlayModChoice(ShortcutModOption Option, string Label, bool Enabled)
    {
        public override string ToString() => Label;
    }

    // The page's settings, applied to the open overlay; the window saves them.
    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShortcutPageViewModel.OverlayOpacity):
                _opacitySyncing = true;
                try { if (_opacitySlider is not null) _opacitySlider.Value = _shortcuts.OverlayOpacity; }
                finally { _opacitySyncing = false; }
                Window?.SetBackgroundOpacity(_shortcuts.OverlayOpacity);
                ApplyAppearance();
                break;
            case nameof(ShortcutPageViewModel.OverlayElementOpacity):
                ApplyAppearance();
                break;
            case nameof(ShortcutPageViewModel.OverlayTextColor):
                _userTextColor = ParseTextColor(_shortcuts.OverlayTextColor);
                _autoTextColor = null;
                TextBrush.Color = _userTextColor;
                ApplyAppearance();
                break;
            case nameof(ShortcutPageViewModel.OverlayCompactView):
                ApplyCompactView();
                if (_compactToggle is not null) _compactToggle.IsOn = _shortcuts.OverlayCompactView;
                break;
            case nameof(ShortcutPageViewModel.OverlayAutoContrast):
                UpdateContrastTimer();
                if (!_shortcuts.OverlayAutoContrast) RestoreTextColor();
                break;
            case nameof(ShortcutPageViewModel.OverlaySlotSwitchEnabled):
            case nameof(ShortcutPageViewModel.OverlayCompactSlots):
                InvalidateSlots();
                Refresh(bindingsChanged: true);
                break;
            // The window list, the chosen window and sending show in the overlay as they change on the page.
            case nameof(ShortcutPageViewModel.Targets):
            case nameof(ShortcutPageViewModel.SelectedTarget):
            case nameof(ShortcutPageViewModel.InputEnabled):
                Refresh();
                break;
            case nameof(ShortcutPageViewModel.HotkeyEnabled):
            case nameof(ShortcutPageViewModel.HotkeyModifiers):
            case nameof(ShortcutPageViewModel.HotkeyKey):
                UpdateCloseTooltip();
                break;
        }
    }

    // The panel's own title bar: status dot, the Mod's name, close. The window makes it the drag area.
    private Grid BuildTitleBar()
    {
        _titleBar = new Grid { Padding = new Thickness(12, 0, 4, 0), ColumnSpacing = 8, RequestedTheme = ElementTheme.Dark };
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _stateDot = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(NoTargetColor) };
        AutomationProperties.SetAutomationId(_stateDot, "OverlayStateDot");
        _titleText = new TextBlock
        {
            FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = TextBrush
        };
        AutomationProperties.SetAutomationId(_titleText, "OverlayTitleText");
        _closeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 }, Width = 36, Height = 32, Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0),
            Foreground = TextBrush, AllowFocusOnInteraction = false, VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(_closeButton, "OverlayCloseButton");
        _closeButton.Click += (_, _) => Hide();
        UpdateCloseTooltip();
        _moreButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE712", FontSize = 14 }, Width = 36, Height = 32, Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0),
            Foreground = TextBrush, AllowFocusOnInteraction = false, VerticalAlignment = VerticalAlignment.Center,
            Flyout = BuildMoreFlyout()
        };
        AutomationProperties.SetAutomationId(_moreButton, "OverlayMoreButton");
        AutomationProperties.SetName(_moreButton, T("Overlay.More"));
        ToolTipService.SetToolTip(_moreButton, T("Overlay.More"));
        Grid.SetColumn(_titleText, 1);
        Grid.SetColumn(_moreButton, 2);
        Grid.SetColumn(_closeButton, 3);
        _titleBar.Children.Add(_stateDot);
        FrameworkElement titleHost = WithTextShadow(_titleText);
        Grid.SetColumn(titleHost, 1);
        _titleBar.Children.Add(titleHost);
        _titleBar.Children.Add(_moreButton);
        _titleBar.Children.Add(_closeButton);
        return _titleBar;
    }

    private IEnumerable<FrameworkElement> TitleBarControls()
    {
        if (_moreButton is not null) yield return _moreButton;
        if (_closeButton is not null) yield return _closeButton;
    }

    // The settings used now and then: compact view, click-through, background opacity, reading the files again.
    private Flyout BuildMoreFlyout()
    {
        var menu = new StackPanel { Spacing = 8, Width = 280, RequestedTheme = ElementTheme.Dark };
        _compactToggle = new ToggleSwitch
        {
            Header = T("OverlayView.Compact"), IsOn = _shortcuts.OverlayCompactView, OnContent = T("Overlay.On"), OffContent = T("Overlay.Off")
        };
        ToolTipService.SetToolTip(_compactToggle, T("OverlayView.CompactHint"));
        AutomationProperties.SetAutomationId(_compactToggle, "OverlayCompactButton");
        AutomationProperties.SetName(_compactToggle, T("OverlayView.Compact"));
        _compactToggle.Toggled += (_, _) => _shortcuts.OverlayCompactView = _compactToggle.IsOn;
        menu.Children.Add(_compactToggle);
        _clickThroughButton = new Button
        {
            Content = T("OverlayView.ClickThrough"), HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false
        };
        ToolTipService.SetToolTip(_clickThroughButton, T("OverlayView.ClickThroughHint"));
        AutomationProperties.SetAutomationId(_clickThroughButton, "OverlayClickThroughButton");
        _clickThroughButton.Click += (_, _) =>
        {
            _moreFlyout?.Hide();
            SetClickThrough(true);
        };
        menu.Children.Add(_clickThroughButton);
        _opacitySlider = CreateOpacitySlider();
        _opacitySlider.Header = T("ShortcutPanel.Opacity");
        AutomationProperties.SetAutomationId(_opacitySlider, "OverlayOpacitySlider");
        ToolTipService.SetToolTip(_opacitySlider, T("ShortcutPanel.OverlayBackgroundOpacity"));
        menu.Children.Add(_opacitySlider);
        var refresh = new Button { Content = T("Overlay.RefreshFromDisk"), HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false };
        AutomationProperties.SetAutomationId(refresh, "OverlayRefreshButton");
        refresh.Click += (_, _) =>
        {
            _moreFlyout?.Hide();
            _ = _actions.RunAsync("Refresh overlay", _actions.RefreshFromDiskAsync);
        };
        menu.Children.Add(refresh);
        _moreFlyout = new Flyout { Content = menu, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        return _moreFlyout;
    }

    // Close only hides the panel; the hotkey brings it back, so the tooltip says which one.
    private void UpdateCloseTooltip()
    {
        if (_closeButton is null) return;
        (string modifiers, string key) = _shortcuts.HotkeyLabel();
        string text = _shortcuts.HotkeyEnabled ? T("Overlay.HideWithHotkey", modifiers + "+" + key) : T("Overlay.Hide");
        ToolTipService.SetToolTip(_closeButton, text);
        AutomationProperties.SetName(_closeButton, text);
    }

    // Three parts below the title bar: the controls (Mod, window, sending), the main area (state slots and keys, which
    // scrolls) and the footer (status and one "Save preset" button for both kinds of preset).
    private UIElement BuildContent()
    {
        // A closed window still owns its controls. Never reparent cached buttons into a new XamlRoot.
        _bindingControls.Clear();
        _textShadows.Clear();
        BuildTitleBar();
        // The overlay window draws the (translucent) background; this content stays transparent.
        // Spacing on the 4/8/12 grid of the J1T tokens (docs/development/UI-Fluent-Design-Plan.md).
        var root = new Grid { Padding = new Thickness(12, 0, 12, 12), RowSpacing = 12, RequestedTheme = ElementTheme.Dark };
        _contentRoot = root;
        ApplyElementResources(root);
        ApplyElementResources(_titleBar!);
        ApplyAppearance();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- Controls: only in-game-safe actions live here: switching Mod and window and arming input. Restoring
        // presets needs the game closed and stays in the main window.
        var panel = new StackPanel { Spacing = 8 };
        _headerPanel = panel;
        _modPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = TextBrush,
            PlaceholderText = T("ShortcutPanel.SelectAMod") };
        AutomationProperties.SetAutomationId(_modPicker, "OverlayModPicker");
        ToolTipService.SetToolTip(_modPicker, T("ShortcutPanel.SwitchTheModWhoseShortcutsAre"));
        _modPicker.SelectionChanged += (_, _) =>
        {
            if (_syncing || _modPicker.SelectedItem is not OverlayModChoice chosen) return;
            if (!ReferenceEquals(_shortcuts.SelectedMod, chosen.Option)) _shortcuts.SelectedMod = chosen.Option;
        };
        panel.Children.Add(_modPicker);
        // The window keys go to, chosen here like on the Shortcuts page, with the list read again on demand.
        var targetRow = new Grid { ColumnSpacing = 8 };
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _targetPicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = TextBrush, DisplayMemberPath = nameof(ShortcutTarget.Label),
            PlaceholderText = T("Overlay.ChooseTargetWindow")
        };
        AutomationProperties.SetAutomationId(_targetPicker, "OverlayTargetPicker");
        AutomationProperties.SetName(_targetPicker, T("Overlay.TargetWindow"));
        _targetPicker.SelectionChanged += (_, _) =>
        {
            if (_syncing || _targetPicker.SelectedItem is not ShortcutTarget target || ReferenceEquals(target, _shortcuts.SelectedTarget)) return;
            _shortcuts.SelectedTarget = target;
        };
        _refreshTargets = new Button
        {
            Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 }, Width = 36, Height = 32, Padding = new Thickness(0),
            Foreground = TextBrush, AllowFocusOnInteraction = false
        };
        AutomationProperties.SetAutomationId(_refreshTargets, "OverlayRefreshTargetsButton");
        AutomationProperties.SetName(_refreshTargets, T("Overlay.RefreshTargets"));
        ToolTipService.SetToolTip(_refreshTargets, T("Overlay.RefreshTargets"));
        _refreshTargets.Click += (_, _) => { _actions.RefreshTargets(); Refresh(); };
        Grid.SetColumn(_refreshTargets, 1);
        targetRow.Children.Add(_targetPicker); targetRow.Children.Add(_refreshTargets);
        panel.Children.Add(targetRow);
        // Sending is a switch: on (green) the keys go to the chosen window, off they go nowhere.
        _sendSwitch = new ToggleSwitch
        {
            OnContent = T("ShortcutPanel.InputOn"), OffContent = T("ShortcutPanel.InputOff"), Foreground = TextBrush,
            MinWidth = 0, VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(_sendSwitch, "OverlaySendSwitch");
        AutomationProperties.SetName(_sendSwitch, T("Overlay.SendKeys"));
        ToolTipService.SetToolTip(_sendSwitch, T("ShortcutPanel.ArmOrPauseInputToThe"));
        var sending = new SolidColorBrush(SendingColor);
        foreach (string key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOn" })
            _sendSwitch.Resources[key] = sending;
        _sendSwitch.Toggled += (_, _) =>
        {
            if (_syncing || _sendSwitch.IsOn == _shortcuts.InputEnabled) return;
            _actions.SetInputEnabled(_sendSwitch.IsOn);
            Refresh();
        };
        var sendRow = new Grid();
        sendRow.Children.Add(_sendSwitch);
        panel.Children.Add(sendRow);

        // ---- Main area: state slots, then the keys (or what to do next when there are none). It scrolls.
        var main = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        _mainPanel = main;
        AddSlotSection(main);
        _emptyText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = TextBrush };
        AutomationProperties.SetAutomationId(_emptyText, "OverlayEmptyText");
        _emptyLabel = Label(_emptyText);
        _emptyLabel.Padding = new Thickness(12, 10, 12, 10);
        _buttonList = new StackPanel { Spacing = 6 };
        main.Children.Add(_buttonList);
        _scroll = new ScrollViewer { Content = main, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(_scroll, "OverlayMainScroll");
        Grid.SetRow(_scroll, 1);

        // ---- Footer: the status line and one button for both kinds of preset.
        _statusText = FixedStatusText(lines: 2, fontSize: 12);
        _statusText.Foreground = TextBrush;
        var statusRow = Label(_statusText);
        var savePreset = new DropDownButton
        {
            Content = T("ShortcutPanel.SavePreset"), Foreground = TextBrush, HorizontalAlignment = HorizontalAlignment.Stretch,
            AllowFocusOnInteraction = false
        };
        AutomationProperties.SetAutomationId(savePreset, "OverlaySavePresetButton");
        var saveMenu = new MenuFlyout { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft };
        var saveCombination = new MenuFlyoutItem { Text = T("Overlay.SaveCombinationPreset") };
        ToolTipService.SetToolTip(saveCombination, T("ShortcutPanel.SaveTheEnabledModCombinationAnd"));
        saveCombination.Click += (_, _) => _ = _actions.SaveCombinationPresetAsync();
        var saveSingle = new MenuFlyoutItem { Text = T("ShortcutPanel.SaveSingleModPreset") };
        saveSingle.Click += (_, _) => _ = _actions.SaveSingleModPresetAsync();
        saveMenu.Items.Add(saveCombination);
        saveMenu.Items.Add(saveSingle);
        savePreset.Flyout = saveMenu;
        var footer = new StackPanel { Spacing = 8 };
        footer.Children.Add(statusRow);
        footer.Children.Add(savePreset);
        _footer = footer;
        Grid.SetRow(footer, 2);

        _detailRows = [targetRow, sendRow, footer];
        ApplyCompactView();
        root.Children.Add(panel); root.Children.Add(_scroll); root.Children.Add(footer);
        // The window's height follows these (ShortcutOverlayWindow.FitContentHeight).
        panel.SizeChanged += (_, _) => QueueFit();
        main.SizeChanged += (_, _) => QueueFit();
        footer.SizeChanged += (_, _) => QueueFit();
        _bindings = [];
        _listSignature = string.Empty;
        _modOptionsSource = null;
        UpdateControls();
        return root;
    }

    // After a layout pass: the three parts and the spacing between them, so the panel is as tall as what it shows.
    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _fitQueued = false;
            if (Window is null || _contentRoot is null || _headerPanel is null || _mainPanel is null || _footer is null) return;
            // The grid keeps the spacing between its three rows even when a row is empty or collapsed (the compact view).
            double height = _contentRoot.Padding.Top + _headerPanel.ActualHeight + _mainPanel.ActualHeight
                + 2 * _contentRoot.RowSpacing + _contentRoot.Padding.Bottom;
            if (_footer.Visibility == Visibility.Visible) height += _footer.ActualHeight;
            Window.FitContentHeight(height);
        });
    }

    private static Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock FixedStatusText(int lines, double fontSize)
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

    private Slider CreateOpacitySlider()
    {
        var slider = new Slider
        {
            Minimum = ShortcutOverlayWindow.MinimumOpacity, Maximum = 100, StepFrequency = 5,
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
            if (!live || _opacitySyncing || (int)Math.Round(e.NewValue) == _shortcuts.OverlayOpacity) return;
            _shortcuts.OverlayOpacity = (int)Math.Round(e.NewValue);
        };
        return slider;
    }

    private void UpdateControls(bool bindingsChanged = true)
    {
        if (_buttonList is null) return;
        ShortcutModOption? current = _shortcuts.SelectedMod;
        bool currentEnabled = current is not null && _actions.IsModDeployed(current);
        _syncing = true;
        try
        {
            // The overlay shows the same Mod as the page. In game only enabled Mods do anything, so the list holds those,
            // plus the page's Mod marked "not enabled" when it is not (its keys are shown but cannot be sent).
            object? source = _shortcuts.ModOptions;
            ShortcutModOption[] all = source as ShortcutModOption[] ?? [];
            if (!ReferenceEquals(_modOptionsSource, source) || (current is not null && !_modOptions.Contains(current)))
            {
                _modOptionsSource = source;
                _modOptions = all.Where(_actions.IsModDeployed).ToArray();
                var choices = _modOptions.Select(option => new OverlayModChoice(option, option.Label, true)).ToList();
                if (current is not null && !currentEnabled)
                    choices.Insert(0, new OverlayModChoice(current, T("Overlay.NotEnabledSuffix", current.Label), false));
                _modPicker!.ItemsSource = choices;
            }
            _modPicker!.SelectedItem = (_modPicker.ItemsSource as List<OverlayModChoice>)?.FirstOrDefault(choice => ReferenceEquals(choice.Option, current));
            _modPicker.PlaceholderText = _modOptions.Length == 0 ? T("ShortcutPanel.NoEnabledMods") : T("ShortcutPanel.SelectAMod");
            if (!ReferenceEquals(_targetPicker!.ItemsSource, _shortcuts.Targets)) _targetPicker.ItemsSource = _shortcuts.Targets;
            _targetPicker.SelectedItem = _shortcuts.SelectedTarget is { } target ? _shortcuts.Targets.FirstOrDefault(item => ReferenceEquals(item, target)) : null;
            _sendSwitch!.IsOn = _shortcuts.InputEnabled;
        }
        finally { _syncing = false; }
        UpdateTitle(current);
        _statusText!.Text = _shortcuts.StatusText;
        // Status-only updates never enumerate bindings or rebuild slots.
        if (!bindingsChanged) return;
        UpdateSlotSection();
        DetectedModShortcut[] visible = current is null ? [] : _shortcuts.VisibleShortcuts();
        string? empty = current is null ? (_modOptions.Length == 0 ? T("Overlay.EmptyNoEnabledMods") : T("Overlay.EmptyPickMod"))
            : !currentEnabled ? T("Overlay.ModNotEnabled", current.Label)
            : visible.Length == 0 ? T("Overlay.EmptyNoBindings") : null;
        _emptyText!.Text = empty ?? string.Empty;
        string listSignature = $"{currentEnabled}|{empty}";
        if (_bindings.SequenceEqual(visible) && listSignature == _listSignature) return;
        _bindings = visible;
        _listSignature = listSignature;
        _buttonList.Children.Clear();
        if (empty is not null) _buttonList.Children.Add(_emptyLabel!);
        foreach (var shortcut in _bindings)
        {
            if (!_bindingControls.TryGetValue(shortcut, out Button? control))
                _bindingControls[shortcut] = control = CreateBindingButton(shortcut);
            // A Mod that is not enabled shows its keys, but they cannot be sent.
            control.IsEnabled = currentEnabled && ShortcutKeyPlan.Explain(shortcut.RawKey) == ShortcutUnsupportedReason.None;
            _buttonList.Children.Add(control);
        }
        if (_bindingControls.Count > 768) _bindingControls.Clear();
        _scroll?.ChangeView(null, 0, null, true);
        QueueFit();
    }

    private string _listSignature = string.Empty;

    // The Mod's name in the title bar and the dot: green sending, amber a window but not sending, grey no window.
    private void UpdateTitle(ShortcutModOption? current)
    {
        if (_titleText is null || _stateDot is null) return;
        _titleText.Text = current?.Label ?? T("Overlay.NoModTitle");
        (Color color, string state) = _shortcuts.SelectedTarget is not { } target ? (NoTargetColor, T("Overlay.StateNoTarget"))
            : _shortcuts.InputEnabled ? (SendingColor, T("Overlay.StateSending", target.Label))
            : (ReadyColor, T("Overlay.StateReady", target.Label));
        ((SolidColorBrush)_stateDot.Fill).Color = color;
        ToolTipService.SetToolTip(_stateDot, state);
        AutomationProperties.SetName(_stateDot, state);
    }

    // A binding on the overlay: its keys as key caps (as on the Shortcuts page) beside what it does; the row sends it.
    private Button CreateBindingButton(DetectedModShortcut shortcut)
    {
        ShortcutUnsupportedReason reason = ShortcutKeyPlan.Explain(shortcut.RawKey);
        bool supported = reason == ShortcutUnsupportedReason.None;
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var keys = new KeyVisual { Keys = shortcut.Shortcut, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var description = new TextBlock { Text = ModShortcutDescriptionFormatter.Describe(shortcut, useEnglish: !_language.IsChinese), TextWrapping = TextWrapping.Wrap };
        string source = $"{shortcut.SourceFile} · [{shortcut.Section}]";
        if (shortcut.Condition.Length > 0) source += " · " + T("ShortcutPanel.Condition") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + T("ShortcutPanel.HoldForConfiguredDuration");
        var sourceText = new TextBlock { Text = source, FontSize = 12, Opacity = .65, TextWrapping = TextWrapping.Wrap };
        description.Foreground = sourceText.Foreground = TextBrush;
        text.Children.Add(WithTextShadow(description));
        text.Children.Add(sourceText);
        if (!supported)
            text.Children.Add(new TextBlock
            {
                Text = T("ShortcutPanel.CannotSend") + UnsupportedReasonText(reason) + T("ShortcutPanel.Key", shortcut.RawKey),
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 150, 60))
            });
        Grid.SetColumn(text, 1);
        row.Children.Add(keys);
        row.Children.Add(text);
        var button = new Button
        {
            Content = row, IsEnabled = supported, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8, 12, 8),
            AllowFocusOnInteraction = false
        };
        AutomationProperties.SetName(button, shortcut.Shortcut + " · " + description.Text);
        button.Click += async (_, _) => await _actions.RunAsync("Send shortcut", () => _actions.SendShortcutAsync(shortcut));
        return button;
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

    // ---- Appearance: backings, shadows, compact view and click-through ----

    private bool UsesLightBacking => OverlayAppearancePolicy.UsesLightBacking(TextBrush.Color.R, TextBrush.Color.G, TextBrush.Color.B);

    // The brushes are shared, so changing them restyles everything in the open overlay.
    private void ApplyAppearance()
    {
        bool light = UsesLightBacking;
        byte fill = light ? (byte)235 : (byte)45;
        byte hover = light ? (byte)215 : (byte)68;
        int elementOpacity = _shortcuts.OverlayElementOpacity;
        ElementBrush.Color = Color.FromArgb(OverlayAppearancePolicy.ElementAlpha(elementOpacity), fill, fill, (byte)(fill + (light ? 0 : 10)));
        _elementHoverBrush.Color = Color.FromArgb(OverlayAppearancePolicy.HoverAlpha(elementOpacity), hover, hover, (byte)(hover + (light ? 0 : 10)));
        bool show = OverlayAppearancePolicy.ShowTextShadow(_shortcuts.OverlayOpacity);
        Color shadowColor = light ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        foreach ((SpriteVisual visual, DropShadow shadow) in _textShadows)
        {
            visual.IsVisible = show;
            shadow.Color = shadowColor;
        }
    }

    // Lightweight styling: the controls' own templates pick these up in every visual state. Also for the layers
    // the window shows inside the overlay (the slot gallery).
    public void ApplyElementResources(FrameworkElement root)
    {
        foreach (string control in new[] { "Button", "ToggleButton", "ComboBox" })
        {
            root.Resources[control + "Background"] = ElementBrush;
            root.Resources[control + "BackgroundDisabled"] = ElementBrush;
            root.Resources[control + "BackgroundPointerOver"] = _elementHoverBrush;
            root.Resources[control + "BackgroundPressed"] = _elementHoverBrush;
        }
        root.Resources["ComboBoxPlaceHolderForeground"] = TextBrush;
        root.Resources["ComboBoxBackgroundFocused"] = _elementHoverBrush;
        root.Resources["ComboBoxBackgroundUnfocused"] = ElementBrush;
    }

    /// <summary>A text on its own backing, readable over any scene.</summary>
    public Border Label(TextBlock text) => new()
    {
        Child = WithTextShadow(text), Background = ElementBrush, CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
        Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = text.VerticalAlignment
    };

    private FrameworkElement WithTextShadow(TextBlock text)
    {
        var anchor = new Canvas { IsHitTestVisible = false };
        var host = new Grid { VerticalAlignment = text.VerticalAlignment };
        host.Children.Add(anchor);
        host.Children.Add(text);
        Compositor compositor = ElementCompositionPreview.GetElementVisual(anchor).Compositor;
        DropShadow shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = 3;
        shadow.Offset = new Vector3(1, 1, 0);
        shadow.Opacity = .95f;
        SpriteVisual visual = compositor.CreateSpriteVisual();
        visual.Shadow = shadow;
        ElementCompositionPreview.SetElementChildVisual(anchor, visual);
        // The mask is a snapshot of the text, so it must follow text and size changes.
        void Refresh()
        {
            if (text.ActualWidth <= 0 || text.ActualHeight <= 0) return;
            visual.Size = new Vector2((float)text.ActualWidth, (float)text.ActualHeight);
            shadow.Mask = text.GetAlphaMask();
        }
        text.SizeChanged += (_, _) => Refresh();
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Refresh());
        _textShadows.Add((visual, shadow));
        visual.IsVisible = OverlayAppearancePolicy.ShowTextShadow(_shortcuts.OverlayOpacity);
        shadow.Color = UsesLightBacking ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        return host;
    }

    private void ApplyCompactView()
    {
        Visibility visibility = _shortcuts.OverlayCompactView ? Visibility.Collapsed : Visibility.Visible;
        foreach (FrameworkElement row in _detailRows) row.Visibility = visibility;
    }

    private void SetClickThrough(bool enabled)
    {
        if (Window is null) return;
        // Without the hotkey the user could not get the mouse back, short of closing the app.
        if (enabled && (!_shortcuts.HotkeyEnabled || !_actions.HotkeyAvailable))
        {
            _shortcuts.StatusText = T("OverlayView.ClickThroughNeedsHotkey");
            if (_shortcuts.OverlayCompactView) _shortcuts.OverlayCompactView = false;
            Refresh(bindingsChanged: true);
            return;
        }
        _clickThrough = enabled;
        Window.SetClickThrough(enabled);
        // The status row is hidden in the compact view, and this is the one thing that must stay visible.
        _clickThroughButton!.Content = enabled ? T("OverlayView.ClickThroughActive") : T("OverlayView.ClickThrough");
        _shortcuts.StatusText = enabled ? T("OverlayView.ClickThroughOn") : T("OverlayView.ClickThroughOff");
        Refresh(bindingsChanged: true);
    }

    // ---- Automatic contrast ----

    // While the overlay is very transparent, look at what is behind it every couple of seconds and use dark or
    // light text to match.
    private void UpdateContrastTimer()
    {
        bool wanted = _shortcuts.OverlayAutoContrast && ShortcutOverlayWindow.CanExcludeFromCapture;
        if (wanted && _contrastTimer is null)
        {
            _contrastTimer = _dispatcher.CreateTimer();
            _contrastTimer.Interval = TimeSpan.FromSeconds(1.5);
            _contrastTimer.Tick += (_, _) => CheckContrast();
        }
        if (wanted) _contrastTimer!.Start();
        else _contrastTimer?.Stop();
    }

    private void CheckContrast()
    {
        // Only when the scene shows through and nothing else is going on in the overlay.
        if (Window is not { } overlay || !_shortcuts.OverlayVisible || overlay.ShowsLayer || _actions.IsSavingFromOverlay
            || !OverlayAppearancePolicy.ShowTextShadow(_shortcuts.OverlayOpacity) || overlay.ClientArea is not { } area)
        {
            RestoreTextColor();
            return;
        }
        byte[]? pixels;
        overlay.BeginCaptureExclusion();
        try { pixels = CharacterScreenCapture.CaptureScreenArea(area); }
        finally { overlay.EndCaptureExclusion(); }
        if (pixels is null) return;
        bool darkNow = _autoTextColor == AutoDarkText;
        Color wanted = OverlayAppearancePolicy.PrefersDarkText(OverlayAppearancePolicy.AverageLuminance(pixels), darkNow) ? AutoDarkText : AutoLightText;
        if (_autoTextColor == wanted) return;
        if (_autoTextColor is null) _userTextColor = TextBrush.Color;
        _autoTextColor = wanted;
        TextBrush.Color = wanted;
        ApplyAppearance();
    }

    private void RestoreTextColor()
    {
        if (_autoTextColor is null) return;
        _autoTextColor = null;
        TextBrush.Color = _userTextColor;
        ApplyAppearance();
    }

    // ---- State slots (hot switching) ----

    private void AddSlotSection(StackPanel panel)
    {
        _slotHeader = new TextBlock { FontSize = 12, Foreground = TextBrush, Opacity = .8 };
        _slotList = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _slotScroll = new ScrollViewer
        {
            Content = _slotList, Height = 40,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled
        };
        _slotHeaderLabel = Label(_slotHeader);
        panel.Children.Add(_slotHeaderLabel);
        panel.Children.Add(_slotScroll);
        _galleryButton = ActionButton(T("PresetPreview.PickByPicture"), () => _ = _actions.RunAsync("Overlay slot gallery", _actions.ShowSlotGalleryAsync));
        _frameButton = ActionButton(T("PresetPreview.FrameCharacter"), () => _ = _actions.RunAsync("Frame slot preview", _actions.FrameCurrentSlotPictureAsync));
        ToolTipService.SetToolTip(_frameButton, T("PresetPreview.FrameCharacterHint"));
        _slotActions = new Grid { ColumnSpacing = 8 };
        _slotActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _slotActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_frameButton, 1);
        foreach (Button action in new[] { _galleryButton, _frameButton })
        {
            action.Foreground = TextBrush;
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            _slotActions.Children.Add(action);
        }
        panel.Children.Add(_slotActions);
        _slotSignature = string.Empty;
    }

    // Slots of the Mod chosen in the overlay; rebuilt only when they change.
    private void UpdateSlotSection()
    {
        if (_slotList is null || _slotHeader is null || _slotScroll is null) return;
        ModPersistentSlot[] slots = _actions.OverlaySlots(out string? modPath);
        string? relative = _actions.ModRelativePath(modPath);
        ModPersistentSlot? current = _actions.CurrentSlot(slots);
        bool switching = _shortcuts.OverlaySlotSwitchEnabled, compactSlots = _shortcuts.OverlayCompactSlots;
        string signature = $"{switching}|{compactSlots}|{relative}|{current?.Id}|" + string.Join(",", slots.Select(slot => slot.Id + slot.Name + slot.PreviewFile));
        if (signature == _slotSignature) return;
        _slotSignature = signature;
        CurrentSlotId = current?.Id;

        Visibility visibility = switching ? Visibility.Visible : Visibility.Collapsed;
        _slotHeaderLabel!.Visibility = visibility;
        _slotScroll.Visibility = visibility;
        _slotActions!.Visibility = visibility;
        _galleryButton!.IsEnabled = slots.Length > 0;
        _frameButton!.IsEnabled = current is not null;
        _slotHeader.Text = slots.Length == 0 ? T("HotReload.NoSlotsForThisMod") : T("HotReload.StateSlotsHotSwitch");
        _slotList.Children.Clear();
        bool anyPreview = false;
        foreach (ModPersistentSlot slot in slots)
        {
            string label = ReferenceEquals(slot, current) ? "● " + slot.Name : slot.Name;
            var button = new Button
            {
                Content = label,
                Foreground = TextBrush, AllowFocusOnInteraction = false, Padding = new Thickness(10, 4, 10, 5), FontSize = 13,
                VerticalAlignment = VerticalAlignment.Top
            };
            if (!compactSlots && _actions.CreatePresetPreviewImage(slot.PreviewFile, 120, 96) is { } thumbnail)
            {
                anyPreview = true;
                thumbnail.HorizontalAlignment = HorizontalAlignment.Center;
                var card = new StackPanel { Spacing = 3 };
                card.Children.Add(thumbnail);
                card.Children.Add(new TextBlock { Text = label, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis });
                button.Content = card;
                AutomationProperties.SetName(button, slot.Name);
                ToolTipService.SetToolTip(button, _actions.CreatePresetPreviewImage(slot.PreviewFile, 320, 320));
            }
            button.Click += async (_, _) => await _actions.RunAsync("Overlay slot switch", () => _actions.SwitchSlotAsync(slot.Id, modPath!));
            _slotList.Children.Add(button);
        }
        _slotScroll.Height = anyPreview ? 156 : 40;
    }
}
