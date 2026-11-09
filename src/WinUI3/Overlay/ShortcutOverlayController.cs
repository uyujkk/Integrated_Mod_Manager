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
    private TextBlock? _targetText, _statusText;
    private ComboBox? _modPicker;
    private ToggleButton? _inputToggle;
    private bool _syncing;
    private ShortcutModOption[] _modOptions = [];
    private object? _modOptionsSource;
    private StackPanel? _buttonList;
    private ScrollViewer? _scroll;
    private DetectedModShortcut[] _bindings = [];
    private ToggleButton? _compactToggle;
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
                var overlay = new ShortcutOverlayWindow(BuildContent(), TextBrush, ElementBrush, T("Common.ModShortcuts"));
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

    /// <summary>The Mod chosen on the overlay: the page's Mod when it is enabled in game, else none.</summary>
    public ShortcutModOption? ModOnOverlay => _modPicker?.SelectedItem as ShortcutModOption;

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
                if (_compactToggle is not null) _compactToggle.IsChecked = _shortcuts.OverlayCompactView;
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
        }
    }

    private UIElement BuildContent()
    {
        // A closed window still owns its controls. Never reparent cached buttons into a new XamlRoot.
        _bindingControls.Clear();
        _textShadows.Clear();
        // The overlay window draws the (translucent) background; this content stays transparent.
        var root = new Grid { Padding = new Thickness(14, 4, 14, 14), RowSpacing = 10, RequestedTheme = ElementTheme.Dark };
        ApplyElementResources(root);
        ApplyAppearance();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var panel = new StackPanel { Spacing = 6 };
        // Every header row has a fixed height so status changes never shift the list (row 1).
        // Only in-game-safe actions live here: switching Mod, arming input and a read-only preset save.
        // Restoring presets needs the game closed and stays in the main window. State slots can be
        // switched here only when the user turns on hot switching.
        _modPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = TextBrush,
            PlaceholderText = T("ShortcutPanel.SelectAMod") };
        ToolTipService.SetToolTip(_modPicker, T("ShortcutPanel.SwitchTheModWhoseShortcutsAre"));
        _modPicker.SelectionChanged += (_, _) =>
        {
            if (_syncing || _modPicker.SelectedItem is not ShortcutModOption chosen) return;
            if (!ReferenceEquals(_shortcuts.SelectedMod, chosen)) _shortcuts.SelectedMod = chosen;
        };
        _targetText = FixedStatusText(lines: 1, fontSize: 11);
        _statusText = FixedStatusText(lines: 2, fontSize: 11);
        _targetText.Foreground = _statusText.Foreground = TextBrush;
        var targetRow = Label(_targetText);
        panel.Children.Add(_modPicker); panel.Children.Add(CreateViewRow()); panel.Children.Add(targetRow);
        var actions = new Grid { ColumnSpacing = 6 };
        for (int i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _inputToggle = new ToggleButton { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = TextBrush };
        ToolTipService.SetToolTip(_inputToggle, T("ShortcutPanel.ArmOrPauseInputToThe"));
        _inputToggle.Click += (_, _) => _actions.SetInputEnabled(_inputToggle.IsChecked == true);
        var savePreset = ActionButton(T("ShortcutPanel.SavePreset"), () => _ = _actions.SaveCombinationPresetAsync());
        ToolTipService.SetToolTip(savePreset, T("ShortcutPanel.SaveTheEnabledModCombinationAnd"));
        var hide = ActionButton(T("ShortcutPanel.Hide"), Hide);
        savePreset.Foreground = hide.Foreground = TextBrush;
        savePreset.HorizontalAlignment = hide.HorizontalAlignment = HorizontalAlignment.Stretch;
        // Three equal columns at the default 420 DIP width: default padding cut "Input: off" / "Save preset".
        foreach (Control action in new Control[] { _inputToggle, savePreset, hide })
        {
            action.Padding = new Thickness(4, 5, 4, 6);
            action.FontSize = 13;
        }
        Grid.SetColumn(savePreset, 1); Grid.SetColumn(hide, 2);
        actions.Children.Add(_inputToggle); actions.Children.Add(savePreset); actions.Children.Add(hide);
        panel.Children.Add(actions);
        var saveSingle = ActionButton(T("ShortcutPanel.SaveSingleModPreset"), () => _ = _actions.SaveSingleModPresetAsync());
        saveSingle.Foreground = TextBrush;
        saveSingle.HorizontalAlignment = HorizontalAlignment.Stretch;
        var refresh = ActionButton(T("ShortcutCapture.Refresh"), () => _ = _actions.RunAsync("Refresh overlay", _actions.RefreshFromDiskAsync));
        refresh.Foreground = TextBrush;
        var secondaryActions = new Grid { ColumnSpacing = 6 };
        secondaryActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        secondaryActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(refresh, 1);
        secondaryActions.Children.Add(saveSingle); secondaryActions.Children.Add(refresh);
        panel.Children.Add(secondaryActions);
        var opacityRow = new Grid { ColumnSpacing = 8 };
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var opacityLabel = new TextBlock { Text = T("ShortcutPanel.Opacity"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = TextBrush };
        _opacitySlider = CreateOpacitySlider();
        _opacitySlider.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(_opacitySlider, T("ShortcutPanel.OverlayBackgroundOpacity"));
        Grid.SetColumn(_opacitySlider, 1);
        opacityRow.Children.Add(Label(opacityLabel)); opacityRow.Children.Add(_opacitySlider);
        panel.Children.Add(opacityRow);
        AddSlotSection(panel);
        var statusRow = Label(_statusText);
        panel.Children.Add(statusRow);
        _detailRows = [targetRow, actions, secondaryActions, opacityRow, statusRow];
        ApplyCompactView();
        _buttonList = new StackPanel { Spacing = 8 };
        _scroll = new ScrollViewer { Content = _buttonList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_scroll, 1);
        root.Children.Add(panel); root.Children.Add(_scroll);
        _bindings = [];
        _modOptionsSource = null;
        UpdateControls();
        return root;
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
        _syncing = true;
        try
        {
            // In game only enabled Mods matter, so the overlay lists those; it forwards choices to the page.
            object? source = _shortcuts.ModOptions;
            if (!ReferenceEquals(_modOptionsSource, source))
            {
                _modOptionsSource = source;
                _modOptions = (source as ShortcutModOption[] ?? []).Where(_actions.IsModDeployed).ToArray();
                _modPicker!.ItemsSource = _modOptions;
            }
            ShortcutModOption? current = _shortcuts.SelectedMod;
            _modPicker!.SelectedItem = current is not null && _modOptions.Contains(current) ? current : null;
            _modPicker.PlaceholderText = _modOptions.Length == 0
                ? T("ShortcutPanel.NoEnabledMods")
                : current is null ? T("ShortcutPanel.SelectAMod") : T("ShortcutPanel.CurrentModNotEnabledPickAn");
            _inputToggle!.IsChecked = _shortcuts.InputEnabled;
            _inputToggle.Content = _shortcuts.InputEnabled ? T("ShortcutPanel.InputOn") : T("ShortcutPanel.InputOff");
        }
        finally { _syncing = false; }
        _targetText!.Text = _shortcuts.SelectedTarget?.Label ?? T("ShortcutPanel.NoTargetWindowSelectItIn");
        _statusText!.Text = _shortcuts.StatusText;
        // Status-only updates never enumerate bindings or rebuild slots.
        if (!bindingsChanged) return;
        UpdateSlotSection();
        bool enabled = _shortcuts.SelectedMod is ShortcutModOption selected && _actions.IsModDeployed(selected);
        DetectedModShortcut[] visible = enabled ? _shortcuts.VisibleShortcuts() : [];
        if (_bindings.SequenceEqual(visible)) return;
        _bindings = visible;
        _buttonList.Children.Clear();
        foreach (var shortcut in _bindings)
        {
            if (!_bindingControls.TryGetValue(shortcut, out Button? control))
                _bindingControls[shortcut] = control = CreateBindingButton(shortcut);
            _buttonList.Children.Add(control);
        }
        if (_bindingControls.Count > 768) _bindingControls.Clear();
        _scroll?.ChangeView(null, 0, null, true);
    }

    // A binding on the overlay; the page draws its own rows (ShortcutsPage).
    private Button CreateBindingButton(DetectedModShortcut shortcut)
    {
        ShortcutUnsupportedReason reason = ShortcutKeyPlan.Explain(shortcut.RawKey);
        bool supported = reason == ShortcutUnsupportedReason.None;
        var content = new StackPanel { Spacing = 3 };
        var title = new TextBlock { Text = shortcut.Shortcut, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var description = new TextBlock { Text = ModShortcutDescriptionFormatter.Describe(shortcut, useEnglish: !_language.IsChinese), TextWrapping = TextWrapping.Wrap };
        string source = $"{shortcut.SourceFile} · [{shortcut.Section}]";
        if (shortcut.Condition.Length > 0) source += " · " + T("ShortcutPanel.Condition") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + T("ShortcutPanel.HoldForConfiguredDuration");
        var sourceText = new TextBlock { Text = source, FontSize = 11, Opacity = .65, TextWrapping = TextWrapping.Wrap };
        title.Foreground = description.Foreground = sourceText.Foreground = TextBrush;
        content.Children.Add(WithTextShadow(title));
        content.Children.Add(WithTextShadow(description));
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
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 10, 14, 10),
            AllowFocusOnInteraction = false
        };
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
        Child = WithTextShadow(text), Background = ElementBrush, CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = text.VerticalAlignment
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

    // Two ways to keep the overlay out of the way in game: fewer rows, and ignoring the mouse.
    private Grid CreateViewRow()
    {
        _compactToggle = new ToggleButton
        {
            Content = T("OverlayView.Compact"), IsChecked = _shortcuts.OverlayCompactView, Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false, FontSize = 13
        };
        ToolTipService.SetToolTip(_compactToggle, T("OverlayView.CompactHint"));
        AutomationProperties.SetAutomationId(_compactToggle, "OverlayCompactButton");
        _compactToggle.Click += (_, _) => _shortcuts.OverlayCompactView = _compactToggle.IsChecked == true;
        _clickThroughButton = new Button
        {
            Content = T("OverlayView.ClickThrough"), Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false, FontSize = 13
        };
        ToolTipService.SetToolTip(_clickThroughButton, T("OverlayView.ClickThroughHint"));
        AutomationProperties.SetAutomationId(_clickThroughButton, "OverlayClickThroughButton");
        _clickThroughButton.Click += (_, _) => SetClickThrough(true);
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_clickThroughButton, 1);
        row.Children.Add(_compactToggle);
        row.Children.Add(_clickThroughButton);
        return row;
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
        _slotActions = new Grid { ColumnSpacing = 6 };
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
