using System.ComponentModel;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Shortcuts page (stage J5, docs/development/UI-Structure-Plan.md): which Mod and game window to send to,
/// the Mod's key bindings and the settings of the overlay, character follow and sending. It shows
/// <see cref="ShortcutPageViewModel"/>, opens the overlay through <see cref="IShortcutOverlay"/> and sends
/// everything else to the window as requests. The page is built on first use, like before the move.
/// </summary>
public sealed partial class ShortcutsPage : UserControl
{
    private readonly AppServices _services;
    private readonly Dictionary<DetectedModShortcut, Button> _bindingControls = new();
    private readonly List<Expander> _modules = [];
    private DetectedModShortcut[] _shownBindings = [];
    private bool _built;
    private bool _syncing;
    private int _layoutMode = -1;
    private int _bindingColumns = -1;

    private ComboBox? _modPicker, _targetPicker, _inputModePicker, _hotkeyModifiers, _hotkeyKey;
    private ToggleSwitch? _enabledToggle;
    private TextBlock? _privilegeNotice, _status, _hiddenNote, _overlaySummary, _hotkeyStatus, _sendingSummary;
    private Button? _elevateButton, _openIniButton;
    private CheckBox? _hideUnsupported;
    private Grid? _toolbarGrid, _bindingButtons, _bodyGrid;
    private Border? _bindingsCard;
    private StackPanel? _settingsColumn;
    private NumberBox? _tapDuration, _holdDuration;
    private Slider? _opacitySlider;

    internal ShortcutsPage(AppServices services)
    {
        _services = services;
        ViewModel = services.Shortcuts;
        InitializeComponent();
        ShortcutsScrollViewer.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width > 0) ApplyLayout();
        };
        ViewModel.PropertyChanged += OnViewModelChanged;
        // Binding cards take their key-cap colours when created; drop them so they are rebuilt.
        services.Appearance.ThemeChanged += (_, _) =>
        {
            _bindingControls.Clear();
            _shownBindings = [];
            RenderBindings();
        };
        Loaded += (_, _) => RenderBindings();
    }

    public ShortcutPageViewModel ViewModel { get; }

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private static Style Resource(string key) => (Style)Application.Current.Resources[key];

    /// <summary>Builds the page once; returns true when it was built now.</summary>
    internal bool EnsureBuilt()
    {
        if (_built) return false;
        Build();
        return true;
    }

    /// <summary>The page is built with localized text; a language switch rebuilds it from the view model.</summary>
    internal void RebuildForLanguage()
    {
        if (!_built) return;
        ShortcutPaneContent.Children.Clear();
        _bindingControls.Clear();
        _shownBindings = [];
        Build();
    }

    // ToggleSwitch otherwise shows On/Off in the Windows display language, not the app language.
    // The header doubles as the automation name: the toolbar may show the label in its own text block.
    private ToggleSwitch LocalizedToggle(string header)
    {
        var toggle = new ToggleSwitch { Header = header, OnContent = T("Common.On2"), OffContent = T("Common.Off2") };
        AutomationProperties.SetName(toggle, header);
        return toggle;
    }

    private static Button ShortcutButton(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }

    // The page is a toolbar (what to send to which window), the bindings, and a column of module
    // rows: beside the bindings on wide pages, above them on narrow ones (ApplyLayout).
    private void Build()
    {
        _built = true;
        var pane = ShortcutPaneContent;
        pane.Margin = new Thickness(6);
        pane.Spacing = 12;
        _layoutMode = -1;
        _bindingColumns = -1;
        _modules.Clear();

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(ShortcutButton(T("ShortcutPanel.RefreshWindows"), () => ViewModel.Request(ShortcutPageAction.RefreshWindows)));
        tools.Children.Add(ShortcutButton(T("ShortcutPanel.ReloadINI"), () => ViewModel.Request(ShortcutPageAction.ReloadIni)));
        pane.Children.Add(new PageHeader
        {
            Title = T("Common.ModShortcuts"),
            Description = T("ShortcutPanel.SelectAModAndGameWindow"),
            Actions = tools
        });

        // Toolbar: the Mod, the game window and the switch that arms sending.
        _modPicker = new ComboBox { Header = T("ShortcutPanel.ModCurrentRepository"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _modPicker.SelectionChanged += (_, _) =>
        {
            if (!_syncing) ViewModel.SelectedMod = _modPicker.SelectedItem as ShortcutModOption;
        };
        _targetPicker = new ComboBox { Header = T("ShortcutPanel.TargetGameWindow"), PlaceholderText = T("ShortcutPanel.SelectAWindow"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _targetPicker.SelectionChanged += (_, _) =>
        {
            if (!_syncing) ViewModel.SelectedTarget = _targetPicker.SelectedItem as ShortcutTarget;
        };
        _enabledToggle = LocalizedToggle(T("ShortcutPanel.EnableInputToSelectedWindow"));
        _enabledToggle.Toggled += (_, _) =>
        {
            if (_syncing) return;
            // Without a target the switch springs back: the view model never arms input without one.
            ViewModel.InputEnabled = _enabledToggle.IsOn;
            SyncInputToggle();
        };
        _toolbarGrid = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
        _toolbarGrid.Children.Add(_modPicker);
        _toolbarGrid.Children.Add(_targetPicker);
        _toolbarGrid.Children.Add(_enabledToggle);
        _privilegeNotice = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = Resource("CaptionTextStyle"), VerticalAlignment = VerticalAlignment.Center };
        _elevateButton = ShortcutButton(T("ShortcutPanel.RestartManagerAsAdministrator"), () => ViewModel.Request(ShortcutPageAction.RestartElevated));
        var privilege = new Grid { ColumnSpacing = 12 };
        privilege.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        privilege.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        privilege.Children.Add(_privilegeNotice);
        Grid.SetColumn(_elevateButton, 1);
        privilege.Children.Add(_elevateButton);
        var toolbar = new StackPanel { Spacing = 10 };
        toolbar.Children.Add(_toolbarGrid);
        toolbar.Children.Add(privilege);
        pane.Children.Add(new Border { Style = Resource("CardBorderStyle"), Child = toolbar });

        // Bindings: the status of the last read or send, then one card per key binding.
        // Fixed height: status changes must not move the bindings below it.
        _status = FixedStatusText(lines: 2, fontSize: 13);
        _hiddenNote = new TextBlock { Opacity = .65, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _bindingButtons = new Grid();
        _bindingButtons.SizeChanged += (_, _) => ReflowBindings();
        // Hiding unsendable bindings filters this list, so the switch sits on its title row.
        _hideUnsupported = new CheckBox
        {
            Content = new TextBlock { Text = T("ShortcutPanel.HideBindingsThatCannotBeSent"), TextWrapping = TextWrapping.Wrap },
            IsChecked = ViewModel.HideUnsupported, HorizontalAlignment = HorizontalAlignment.Right
        };
        _hideUnsupported.Checked += (_, _) => ViewModel.HideUnsupported = true;
        _hideUnsupported.Unchecked += (_, _) => ViewModel.HideUnsupported = false;
        var bindingsTitle = new Grid { ColumnSpacing = 12 };
        bindingsTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bindingsTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bindingsTitle.Children.Add(new TextBlock { Text = T("ShortcutPanel.KeyBindings"), Style = Resource("SectionTitleTextStyle"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_hideUnsupported, 1);
        bindingsTitle.Children.Add(_hideUnsupported);
        var bindings = new StackPanel { Spacing = 10 };
        bindings.Children.Add(bindingsTitle);
        var readOnlyHint = new Grid { ColumnSpacing = 12 };
        readOnlyHint.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readOnlyHint.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        readOnlyHint.Children.Add(new TextBlock { Text = T("ShortcutPanel.BindingsReadOnlyHint"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Style = Resource("CaptionTextStyle") });
        _openIniButton = ShortcutButton(T("ShortcutPanel.OpenIni"), () => ViewModel.Request(ShortcutPageAction.OpenIni));
        _openIniButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_openIniButton, 1);
        readOnlyHint.Children.Add(_openIniButton);
        bindings.Children.Add(readOnlyHint);
        bindings.Children.Add(_status);
        bindings.Children.Add(_hiddenNote);
        bindings.Children.Add(_bindingButtons);
        _bindingsCard = new Border { Style = Resource("CardBorderStyle"), Child = bindings, VerticalAlignment = VerticalAlignment.Top };

        // Settings column: one collapsible row per module, most used first.
        _settingsColumn = new StackPanel { Spacing = 8 };
        _overlaySummary = new TextBlock();
        var openOverlay = ShortcutButton(T("ShortcutPanel.OpenOverlay"), () => _services.Overlay.Open());
        StackPanel overlay = AddModule("\uE7F4", T("ShortcutPanel.OverlaySettings"), _overlaySummary, openOverlay);
        overlay.Children.Add(GroupLabel(T("ShortcutPanel.HotkeyGroup")));
        _hotkeyModifiers = new ComboBox { Header = T("ShortcutPanel.ShowHideOverlay") };
        foreach (var item in new[] { ("Ctrl+Alt", 3u), ("Ctrl+Shift", 6u), ("Alt+Shift", 5u) })
            _hotkeyModifiers.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
        _hotkeyKey = new ComboBox { Header = T("ShortcutPanel.Key2") };
        foreach (uint key in Enumerable.Range('A', 26).Concat(Enumerable.Range(0x70, 12)).Select(k => (uint)k))
            _hotkeyKey.Items.Add(new ComboBoxItem { Content = key >= 0x70 ? "F" + (key - 0x6F) : ((char)key).ToString(), Tag = key });
        SyncHotkeyPickers();
        var hotkeySettings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hotkeySettings.Children.Add(_hotkeyModifiers);
        hotkeySettings.Children.Add(_hotkeyKey);
        overlay.Children.Add(hotkeySettings);
        // A choice takes effect at once; Apply remains to turn the hotkey back on after Disable.
        _hotkeyModifiers.SelectionChanged += (_, _) => ApplyHotkeyChoice();
        _hotkeyKey.SelectionChanged += (_, _) => ApplyHotkeyChoice();
        var hotkeyActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hotkeyActions.Children.Add(ShortcutButton(T("Common.Apply"), ApplyHotkeyChoice));
        hotkeyActions.Children.Add(ShortcutButton(T("ShortcutPanel.Disable"), () => ViewModel.Request(ShortcutPageAction.DisableHotkey)));
        overlay.Children.Add(hotkeyActions);
        _hotkeyStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = Resource("CaptionTextStyle") };
        overlay.Children.Add(_hotkeyStatus);
        overlay.Children.Add(GroupLabel(T("ShortcutPanel.AppearanceGroup")));
        _opacitySlider = CreateOpacitySlider(T("ShortcutPanel.OverlayOpacity"));
        _services.ShortcutSettings.AddOverlayAppearance(overlay, _opacitySlider);
        overlay.Children.Add(GroupLabel(T("ShortcutPanel.BehaviorGroup")));
        _services.ShortcutSettings.AddOverlayBehavior(overlay);
        overlay.Children.Add(new TextBlock
        {
            Text = T("ShortcutPanel.TheOverlaySupportsWindowedBorderlessMode"),
            TextWrapping = TextWrapping.Wrap, Style = Resource("CaptionTextStyle")
        });

        var follow = new StackPanel { Spacing = 10 };
        (ToggleSwitch followToggle, TextBlock followSummary) = _services.ShortcutSettings.AddCharacterFollow(follow);
        AddModule("\uE77B", T("CharacterFollow.Title"), followSummary, followToggle).Children.Add(follow);

        _sendingSummary = new TextBlock();
        StackPanel send = AddModule("\uE765", T("ShortcutPanel.SendingSettings"), _sendingSummary, null);
        _inputModePicker = new ComboBox { Header = T("ShortcutPanel.InputMethod"), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(_inputModePicker, "ShortcutInputModePicker");
        _inputModePicker.Items.Add(new ComboBoxItem { Content = T("ShortcutPanel.ScanCodesPhysicalKeySimulation"), Tag = ShortcutInputMode.ScanCode });
        _inputModePicker.Items.Add(new ComboBoxItem { Content = T("ShortcutPanel.VirtualKeysLegacyCompatibility"), Tag = ShortcutInputMode.VirtualKey });
        _inputModePicker.SelectedIndex = ViewModel.InputMode == ShortcutInputMode.VirtualKey ? 1 : 0;
        _inputModePicker.SelectionChanged += (_, _) =>
        {
            if (_inputModePicker.SelectedItem is ComboBoxItem { Tag: ShortcutInputMode mode }) ViewModel.InputMode = mode;
        };
        send.Children.Add(_inputModePicker);
        _tapDuration = new NumberBox
        {
            Header = T("ShortcutPanel.TapDurationMs"), Minimum = 100, Maximum = 2000, Value = ViewModel.TapDuration,
            SmallChange = 100, HorizontalAlignment = HorizontalAlignment.Stretch,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        _holdDuration = new NumberBox
        {
            Header = T("ShortcutPanel.HoldTypeDurationMs"),
            Minimum = 100, Maximum = 2000, Value = ViewModel.HoldDuration, SmallChange = 100,
            HorizontalAlignment = HorizontalAlignment.Stretch, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        _tapDuration.ValueChanged += (_, _) => ViewModel.TapDuration = _tapDuration.Value;
        _holdDuration.ValueChanged += (_, _) => ViewModel.HoldDuration = _holdDuration.Value;
        var durations = new Grid { ColumnSpacing = 10 };
        durations.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        durations.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Bottom-aligned so the boxes line up when one header wraps to two lines.
        _tapDuration.VerticalAlignment = VerticalAlignment.Bottom;
        _holdDuration.VerticalAlignment = VerticalAlignment.Bottom;
        durations.Children.Add(_tapDuration);
        Grid.SetColumn(_holdDuration, 1);
        durations.Children.Add(_holdDuration);
        send.Children.Add(durations);
        send.Children.Add(ShortcutButton(T("ShortcutPanel.ToggleNumLock"), () => ViewModel.Request(ShortcutPageAction.SendNumLock)));

        _bodyGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        pane.Children.Add(_bodyGrid);
        SyncAll();
        ApplyLayout();
    }

    // A settings row: icon, title and a one-line summary stay visible while collapsed, and the
    // module's main control sits on the right of the header.
    private StackPanel AddModule(string glyph, string title, TextBlock summary, FrameworkElement? control)
    {
        var header = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 8, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        summary.Style = Resource("CaptionTextStyle");
        // The state may need two lines next to a header control ("Not set up · select a HUD region first").
        summary.TextWrapping = TextWrapping.WrapWholeWords;
        summary.MaxLines = 2;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(summary);
        Grid.SetColumn(text, 1);
        header.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
            header.Children.Add(control);
        }
        var body = new StackPanel { Spacing = 10 };
        var row = new Expander
        {
            Header = header, Content = body,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _modules.Add(row);
        _settingsColumn!.Children.Add(row);
        return body;
    }

    private static TextBlock GroupLabel(string text) => new()
    {
        Text = text, Margin = new Thickness(0, 4, 0, 0),
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Resource("CaptionTextStyle")
    };

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

    private Slider CreateOpacitySlider(string header)
    {
        var slider = new Slider
        {
            Header = header, Minimum = ShortcutOverlayWindow.MinimumOpacity, Maximum = 100, StepFrequency = 5,
            SmallChange = 5, Value = ViewModel.OverlayOpacity
        };
        // WinUI raises ValueChanged while the template loads (observed resetting to the minimum);
        // only react to changes after the slider is live, and restore the saved value first.
        bool live = false;
        slider.Loaded += (_, _) =>
        {
            if (live) return;
            slider.Value = ViewModel.OverlayOpacity;
            live = true;
        };
        slider.ValueChanged += (_, e) =>
        {
            if (!live || _syncing || (int)Math.Round(e.NewValue) == ViewModel.OverlayOpacity) return;
            ViewModel.OverlayOpacity = (int)Math.Round(e.NewValue);
        };
        return slider;
    }

    private void ApplyHotkeyChoice()
    {
        if (_syncing || _hotkeyModifiers?.SelectedItem is not ComboBoxItem { Tag: uint modifiers } || _hotkeyKey?.SelectedItem is not ComboBoxItem { Tag: uint key }) return;
        if (ViewModel.HotkeyRegistered && modifiers == ViewModel.HotkeyModifiers && key == ViewModel.HotkeyKey) return;
        ViewModel.Request(ShortcutPageAction.SetHotkey, (modifiers, key));
        // The pickers must show the hotkey that is in effect, which is the previous one when this one was rejected.
        SyncHotkeyPickers();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_built) return;
        switch (e.PropertyName)
        {
            case nameof(ShortcutPageViewModel.ModOptions):
            case nameof(ShortcutPageViewModel.SelectedMod):
                SyncModPicker();
                break;
            case nameof(ShortcutPageViewModel.Targets):
            case nameof(ShortcutPageViewModel.SelectedTarget):
                SyncTargetPicker();
                break;
            case nameof(ShortcutPageViewModel.InputEnabled):
                SyncInputToggle();
                break;
            case nameof(ShortcutPageViewModel.StatusText):
                _status!.Text = ViewModel.StatusText;
                break;
            case nameof(ShortcutPageViewModel.Detected):
            case nameof(ShortcutPageViewModel.HideUnsupported):
                _openIniButton!.IsEnabled = ViewModel.Detected.Count > 0;
                RenderBindings();
                break;
            case nameof(ShortcutPageViewModel.PrivilegeText):
            case nameof(ShortcutPageViewModel.IsElevated):
                SyncPrivilege();
                break;
            case nameof(ShortcutPageViewModel.HotkeyRegistered):
            case nameof(ShortcutPageViewModel.HotkeyRejected):
            case nameof(ShortcutPageViewModel.HotkeyModifiers):
            case nameof(ShortcutPageViewModel.HotkeyKey):
                SyncHotkeyStatus();
                break;
            case nameof(ShortcutPageViewModel.OverlayOpacity):
                _syncing = true;
                try { _opacitySlider!.Value = ViewModel.OverlayOpacity; }
                finally { _syncing = false; }
                break;
            case nameof(ShortcutPageViewModel.InputMode):
            case nameof(ShortcutPageViewModel.TapDuration):
            case nameof(ShortcutPageViewModel.HoldDuration):
                SyncSendingSummary();
                break;
        }
    }

    private void SyncAll()
    {
        SyncModPicker();
        SyncTargetPicker();
        SyncInputToggle();
        SyncPrivilege();
        SyncHotkeyStatus();
        SyncSendingSummary();
        _status!.Text = ViewModel.StatusText;
        _openIniButton!.IsEnabled = ViewModel.Detected.Count > 0;
    }

    private void SyncModPicker()
    {
        _syncing = true;
        try
        {
            if (!ReferenceEquals(_modPicker!.ItemsSource, ViewModel.ModOptions)) _modPicker.ItemsSource = ViewModel.ModOptions;
            _modPicker.SelectedItem = ViewModel.SelectedMod;
        }
        finally { _syncing = false; }
    }

    private void SyncTargetPicker()
    {
        _syncing = true;
        try
        {
            if (!ReferenceEquals(_targetPicker!.ItemsSource, ViewModel.Targets)) _targetPicker.ItemsSource = ViewModel.Targets;
            _targetPicker.SelectedItem = ViewModel.SelectedTarget;
        }
        finally { _syncing = false; }
    }

    private void SyncInputToggle()
    {
        _syncing = true;
        try { _enabledToggle!.IsOn = ViewModel.InputEnabled; }
        finally { _syncing = false; }
    }

    private void SyncPrivilege()
    {
        _privilegeNotice!.Text = ViewModel.PrivilegeText;
        _elevateButton!.Visibility = ViewModel.IsElevated ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncHotkeyPickers()
    {
        _syncing = true;
        try
        {
            _hotkeyModifiers!.SelectedItem = _hotkeyModifiers.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == ViewModel.HotkeyModifiers);
            _hotkeyKey!.SelectedItem = _hotkeyKey.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == ViewModel.HotkeyKey);
        }
        finally { _syncing = false; }
    }

    private void SyncHotkeyStatus()
    {
        SyncHotkeyPickers();
        (string modifiers, string key) = ViewModel.HotkeyLabel();
        // A working hotkey is already on the module row's summary; the line below explains only problems.
        _hotkeyStatus!.Text = ViewModel.HotkeyRejected
            ? T("ShortcutPanel.HotkeyUnavailablePreviousSettingsRetainedChoose")
            : ViewModel.HotkeyRegistered ? string.Empty : T("ShortcutPanel.GlobalHotkeyDisabledOrUnavailableChoose");
        _hotkeyStatus.Visibility = _hotkeyStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _overlaySummary!.Text = ViewModel.HotkeyRegistered
            ? T("ShortcutPanel.OverlayHotkeySummary", modifiers, key)
            : T("ShortcutPanel.OverlayHotkeyOff");
    }

    private void SyncSendingSummary()
    {
        string mode = ViewModel.InputMode == ShortcutInputMode.VirtualKey ? T("ShortcutPanel.VirtualKeys") : T("ShortcutPanel.ScanCodes");
        // A cleared box reads NaN; sending then falls back to the defaults, so the summary does too.
        _sendingSummary!.Text = T("ShortcutPanel.SendingSummary", mode, ViewModel.TapMilliseconds, ViewModel.HoldMilliseconds);
    }

    // Laid out from the page's own width.
    private void ApplyLayout()
    {
        if (_bodyGrid is null || _toolbarGrid is null || _bindingsCard is null || _settingsColumn is null
            || _enabledToggle is null || _modPicker is null || _targetPicker is null)
        {
            return;
        }

        double width = PageWidth() - 12;
        bool beside = ShortcutsLayoutPolicy.SettingsBesideBindings(width);
        bool oneRow = ShortcutsLayoutPolicy.ToolbarInOneRow(width);
        int mode = (beside ? 2 : 0) + (oneRow ? 1 : 0);
        if (mode == _layoutMode)
        {
            return;
        }
        bool placementChanged = _layoutMode < 0 || (_layoutMode >= 2) != beside;
        _layoutMode = mode;

        // Beside the bindings the overlay row starts open; above them every row folds so the
        // bindings stay on the first screen.
        if (placementChanged)
        {
            for (int i = 0; i < _modules.Count; i++) _modules[i].IsExpanded = beside && i == 0;
        }

        // Toolbar: pickers and the send switch in one row, or the pickers stacked above the switch.
        _toolbarGrid.ColumnDefinitions.Clear();
        _toolbarGrid.RowDefinitions.Clear();
        if (oneRow)
        {
            _toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Place(_modPicker, 0, 0);
            Place(_targetPicker, 0, 1);
            Place(_enabledToggle, 0, 2);
        }
        else
        {
            for (int row = 0; row < 3; row++) _toolbarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_modPicker, 0, 0);
            Place(_targetPicker, 1, 0);
            Place(_enabledToggle, 2, 0);
        }

        // Body: module rows beside the bindings, or above them; collapsed rows are short enough
        // that the bindings still start on the first screen.
        _bodyGrid.Children.Clear();
        _bodyGrid.ColumnDefinitions.Clear();
        _bodyGrid.RowDefinitions.Clear();
        if (beside)
        {
            _bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ShortcutsLayoutPolicy.SettingsColumnWidth) });
            Place(_bindingsCard, 0, 0);
            Place(_settingsColumn, 0, 1);
            _bodyGrid.Children.Add(_bindingsCard);
            _bodyGrid.Children.Add(_settingsColumn);
        }
        else
        {
            _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_settingsColumn, 0, 0);
            Place(_bindingsCard, 1, 0);
            _bodyGrid.Children.Add(_settingsColumn);
            _bodyGrid.Children.Add(_bindingsCard);
        }
    }

    // A hidden page has no size yet, so it estimates from the window minus the navigation.
    private double PageWidth()
        => ShortcutsScrollViewer.ActualWidth > 0 ? ShortcutsScrollViewer.ActualWidth : Math.Max(420, (XamlRoot?.Size.Width ?? 0) - 240);

    private static void Place(FrameworkElement child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        Grid.SetColumnSpan(child, 1);
    }

    private void ReflowBindings()
    {
        if (_bindingButtons is null || _bindingButtons.ActualWidth <= 0) return;
        int columns = ShortcutsLayoutPolicy.BindingColumns(_bindingButtons.ActualWidth);
        if (columns == _bindingColumns && _bindingButtons.RowDefinitions.Count > 0) return;
        _bindingColumns = columns;
        Grid grid = _bindingButtons;
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.RowSpacing = 8;
        grid.ColumnSpacing = 8;
        for (int column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int count = grid.Children.Count;
        for (int row = 0; row < (count + columns - 1) / columns; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int slot = 0; slot < count; slot++)
        {
            var child = (FrameworkElement)grid.Children[slot];
            Grid.SetRow(child, slot / columns);
            Grid.SetColumn(child, slot % columns);
            Grid.SetColumnSpan(child, 1);
            if (child is Button button) button.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    // Only while the page is on screen; it renders again when shown.
    private void RenderBindings()
    {
        if (_bindingButtons is null || !IsLoaded) return;
        DetectedModShortcut[] visible = ViewModel.VisibleShortcuts();
        if (_shownBindings.SequenceEqual(visible)) return;
        _shownBindings = visible;
        _bindingButtons.Children.Clear();
        int hidden = ViewModel.Detected.Count - visible.Length;
        _hiddenNote!.Text = hidden > 0 ? T("ShortcutPanel.BindingsThatCannotBeSentAre", hidden) : string.Empty;
        _hiddenNote.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var shortcut in visible)
        {
            if (!_bindingControls.TryGetValue(shortcut, out Button? control))
                _bindingControls[shortcut] = control = CreateBindingCard(shortcut);
            _bindingButtons.Children.Add(control);
        }
        if (_bindingControls.Count > 768) _bindingControls.Clear();
        // New cards need rows and columns even when the width did not change.
        _bindingColumns = -1;
        ReflowBindings();
    }

    private Button CreateBindingCard(DetectedModShortcut shortcut)
    {
        ShortcutUnsupportedReason reason = ShortcutKeyPlan.Explain(shortcut.RawKey);
        bool supported = reason == ShortcutUnsupportedReason.None;
        var content = new StackPanel { Spacing = 6 };
        var title = new TextBlock { Text = shortcut.Shortcut, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var description = new TextBlock { Text = ModShortcutDescriptionFormatter.Describe(shortcut, useEnglish: !_services.Language.IsChinese), TextWrapping = TextWrapping.Wrap };
        string source = $"{shortcut.SourceFile} · [{shortcut.Section}]";
        if (shortcut.Condition.Length > 0) source += " · " + T("ShortcutPanel.Condition") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + T("ShortcutPanel.HoldForConfiguredDuration");
        var sourceText = new TextBlock { Text = source, FontSize = 11, Opacity = .65, TextWrapping = TextWrapping.Wrap };
        // On the page the key reads as a key cap above what it does.
        string theme = _services.Appearance.ThemeKey;
        content.Children.Add(new Border
        {
            Child = title, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 4),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1, 1, 1, 2),
            Background = ThemeTokens.Get("AppInsetBackgroundBrush", theme), BorderBrush = ThemeTokens.Get("AppStrongBorderBrush", theme)
        });
        content.Children.Add(description);
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
            // Cards in one grid row share its height and keep their text at the top.
            VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top
        };
        button.Click += (_, _) => ViewModel.Request(ShortcutPageAction.SendShortcut, shortcut);
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
}
