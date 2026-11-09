using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Shortcuts page (stage J5, docs/development/UI-Structure-Plan.md and UI-Fluent-Design-Plan.md): which Mod and
/// game window to send to, the Mod's key bindings and the settings of the overlay, character follow and sending. It
/// shows <see cref="ShortcutPageViewModel"/>, opens the overlay through <see cref="IShortcutOverlay"/> and sends
/// everything else to the window as requests. Since stage J5b the overlay's look and behaviour, character follow and
/// slot capture are view model settings too, shown here as setting cards.
/// </summary>
public sealed partial class ShortcutsPage : UserControl
{
    private readonly AppServices _services;
    private readonly Dictionary<DetectedModShortcut, SettingsCard> _bindingCards = new();
    private DetectedModShortcut[] _shownBindings = [];
    private bool _built;
    private bool _syncing;
    // Sliders raise ValueChanged while their template loads (observed resetting to the minimum); only live ones count.
    private readonly HashSet<Slider> _liveSliders = [];

    internal ShortcutsPage(AppServices services)
    {
        _services = services;
        ViewModel = services.Shortcuts;
        InitializeComponent();
        foreach (var item in new[] { ("Ctrl+Alt", 3u), ("Ctrl+Shift", 6u), ("Alt+Shift", 5u) })
            ShortcutHotkeyModifiersPicker.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
        foreach (uint key in Enumerable.Range('A', 26).Concat(Enumerable.Range(0x70, 12)).Select(k => (uint)k))
            ShortcutHotkeyKeyPicker.Items.Add(new ComboBoxItem { Content = key >= 0x70 ? "F" + (key - 0x6F) : ((char)key).ToString(), Tag = key });
        ViewModel.PropertyChanged += OnViewModelChanged;
        // Binding rows are recreated so their colours follow the theme.
        services.Appearance.ThemeChanged += (_, _) =>
        {
            _bindingCards.Clear();
            _shownBindings = [];
            RenderBindings();
        };
        Loaded += (_, _) => RenderBindings();
        // The loader's d3dx.ini may have changed while the page was away.
        Loaded += (_, _) => { if (_built) ViewModel.Request(ShortcutPageAction.RefreshLoaderAutoSave); };
        ShortcutOverlayOpacitySlider.Minimum = ShortcutOverlayWindow.MinimumOpacity;
        foreach (Slider slider in new[] { ShortcutOverlayOpacitySlider, ShortcutOverlayElementOpacitySlider })
        {
            slider.Loaded += (_, _) =>
            {
                if (_liveSliders.Contains(slider)) return;
                SyncOverlaySettings();
                _liveSliders.Add(slider);
            };
        }
        foreach (string hex in new[] { "#FFFFFF", "#FFE8A3", "#B8F5C0", "#A8D8FF", "#FFC0DA", "#202020" })
        {
            Windows.UI.Color color = ShortcutOverlayController.ParseTextColor(hex);
            var swatch = new Button
            {
                Width = 30, Height = 30, Padding = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(120, 128, 128, 128)), BorderThickness = new Thickness(1)
            };
            ToolTipService.SetToolTip(swatch, hex);
            AutomationProperties.SetName(swatch, hex);
            swatch.Click += (_, _) => ShortcutOverlayTextColorPicker.Color = color;
            ShortcutOverlayTextColorPresets.Children.Add(swatch);
        }
    }

    public ShortcutPageViewModel ViewModel { get; }

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    /// <summary>Fills the page once; returns true when it was filled now.</summary>
    internal bool EnsureBuilt()
    {
        if (_built) return false;
        Build();
        return true;
    }

    /// <summary>A language switch relabels the page and rebuilds the window's settings blocks in the new language.</summary>
    internal void RebuildForLanguage()
    {
        if (!_built) return;
        _bindingCards.Clear();
        _shownBindings = [];
        Build();
    }

    private void Build()
    {
        _built = true;
        ApplyLanguage();

        SyncAll();
        RenderBindings();
    }

    private void ApplyLanguage()
    {
        ShortcutsPageHeader.Title = T("Common.ModShortcuts");
        ShortcutsPageHeader.Description = T("ShortcutPanel.SelectAModAndGameWindow");
        ShortcutsRefreshWindowsButton.Content = T("ShortcutPanel.RefreshWindows");
        ShortcutsReloadIniButton.Content = T("ShortcutPanel.ReloadINI");
        ShortcutsElevateButton.Content = T("ShortcutPanel.RestartManagerAsAdministrator");

        ShortcutsModCard.Header = T("ShortcutPanel.ModCurrentRepository");
        ShortcutsTargetCard.Header = T("ShortcutPanel.TargetGameWindow");
        ShortcutTargetPicker.PlaceholderText = T("ShortcutPanel.SelectAWindow");
        ToolTipService.SetToolTip(ShortcutsRefreshTargetsButton, T("ShortcutPanel.RefreshWindows"));
        AutomationProperties.SetName(ShortcutsRefreshTargetsButton, T("ShortcutPanel.RefreshWindows"));
        ShortcutsInputCard.Header = T("ShortcutPanel.EnableInputToSelectedWindow");
        SetToggleText(ShortcutInputToggle, T("ShortcutPanel.EnableInputToSelectedWindow"));
        AutomationProperties.SetName(ShortcutModPicker, T("ShortcutPanel.ModCurrentRepository"));
        AutomationProperties.SetName(ShortcutTargetPicker, T("ShortcutPanel.TargetGameWindow"));

        ShortcutsBindingsTitleTextBlock.Text = T("ShortcutPanel.KeyBindings");
        SetToggleText(ShortcutHideUnsupportedToggle, T("ShortcutPanel.HideBindingsThatCannotBeSent"));
        ShortcutHideUnsupportedToggle.OnContent = ShortcutHideUnsupportedToggle.OffContent = T("ShortcutPanel.HideBindingsThatCannotBeSent");
        ToolTipService.SetToolTip(ShortcutsBindingsMoreButton, T("ShortcutPanel.MoreActions"));
        AutomationProperties.SetName(ShortcutsBindingsMoreButton, T("ShortcutPanel.MoreActions"));
        ShortcutsOpenIniMenuItem.Text = T("ShortcutPanel.OpenIni");
        ShortcutsReadOnlyHintTextBlock.Text = T("ShortcutPanel.BindingsReadOnlyHint");

        ShortcutsOverlayExpander.Header = T("ShortcutPanel.OverlaySettings");
        ShortcutsOpenOverlayButton.Content = T("ShortcutPanel.OpenOverlay");
        ShortcutsHotkeyCard.Header = T("ShortcutPanel.GlobalHotkey");
        SetToggleText(ShortcutHotkeyToggle, T("ShortcutPanel.GlobalHotkey"));
        ShortcutsHotkeyChordCard.Header = T("ShortcutPanel.ShowHideOverlay");
        AutomationProperties.SetName(ShortcutHotkeyModifiersPicker, T("ShortcutPanel.ShowHideOverlay"));
        AutomationProperties.SetName(ShortcutHotkeyKeyPicker, T("ShortcutPanel.Key2"));
        ShortcutsStatusRefreshButton.Content = T("ShortcutPanel.RefreshWindows");
        ShortcutsOverlayBehaviorNoteTextBlock.Text = T("ShortcutPanel.TheOverlaySupportsWindowedBorderlessMode");
        ShortcutsOverlayPresetCard.Header = T("ShortcutPanel.AppearanceGroup");
        ShortcutsOverlayPresetButtons.Children.Clear();
        foreach (OverlayAppearancePreset preset in OverlayAppearancePreset.All)
        {
            string label = preset.Id switch
            {
                "clear" => T("OverlayView.Preset.clear"),
                "balanced" => T("OverlayView.Preset.balanced"),
                _ => T("OverlayView.Preset.minimal")
            };
            var button = new Button { Content = label };
            button.Click += (_, _) => ViewModel.ApplyOverlayPreset(preset);
            ShortcutsOverlayPresetButtons.Children.Add(button);
        }
        ShortcutsOverlayOpacityCard.Header = T("ShortcutPanel.OverlayOpacity");
        AutomationProperties.SetName(ShortcutOverlayOpacitySlider, T("ShortcutPanel.OverlayOpacity"));
        ShortcutsOverlayElementOpacityCard.Header = T("ShortcutPanel.OverlayElementOpacity");
        AutomationProperties.SetName(ShortcutOverlayElementOpacitySlider, T("ShortcutPanel.OverlayElementOpacity"));
        ShortcutsOverlayTextColorCard.Header = T("ShortcutPanel.OverlayTextColor");
        AutomationProperties.SetName(ShortcutOverlayTextColorButton, T("ShortcutPanel.OverlayTextColor"));
        ShortcutOverlayTextColorPresetsTextBlock.Text = T("ShortcutPanel.Presets");
        ShortcutsOverlayAutoContrastCard.Header = T("OverlayView.AutoContrast");
        ShortcutsOverlayAutoContrastCard.Description = ShortcutOverlayWindow.CanExcludeFromCapture ? T("OverlayView.AutoContrastHint") : T("OverlayView.AutoContrastUnsupported");
        ShortcutOverlayAutoContrastToggle.IsEnabled = ShortcutOverlayWindow.CanExcludeFromCapture;
        SetToggleText(ShortcutOverlayAutoContrastToggle, T("OverlayView.AutoContrast"));
        ShortcutsSlotSwitchCard.Header = T("HotReload.SwitchSlotsInTheOverlay");
        SetToggleText(ShortcutSlotSwitchToggle, T("HotReload.SwitchSlotsInTheOverlay"));
        ShortcutsCompactSlotsCard.Header = T("PresetPreview.CompactSlotList");
        SetToggleText(ShortcutCompactSlotsToggle, T("PresetPreview.CompactSlotList"));
        ShortcutsPresetPicturesCard.Header = T("PresetPreview.CaptureWhenSaving");
        SetToggleText(ShortcutPresetPicturesToggle, T("PresetPreview.CaptureWhenSaving"));
        ShortcutsClearRegionCard.Header = T("PresetPreview.ClearDefaultRegion");
        ShortcutClearRegionButton.Content = T("PresetPreview.ClearDefaultRegion");
        ShortcutsKeepMenuStateCard.Header = T("SlotCapture.KeepMenuState");
        ShortcutsKeepMenuStateCard.Description = T("SlotCapture.KeepMenuStateHint");
        SetToggleText(ShortcutKeepMenuStateToggle, T("SlotCapture.KeepMenuState"));
        ShortcutsAutoSaveCard.Header = T("SlotCapture.AutoSaveInterval");
        AutomationProperties.SetName(ShortcutAutoSavePicker, T("SlotCapture.AutoSaveInterval"));

        ShortcutsFollowExpander.Header = T("CharacterFollow.Title");
        SetToggleText(ShortcutCharacterFollowToggle, T("CharacterFollow.AutomaticallyFollowCharacterShortcuts"));
        ToolTipService.SetToolTip(ShortcutCharacterFollowToggle, T("CharacterFollow.AutomaticallyFollowCharacterShortcuts"));
        ShortcutsFollowSetupCard.Description = T("CharacterFollow.SelectAHUDRegionThatChanges");
        ShortcutSelectRegionButton.Content = T("CharacterFollow.SelectHUDRegion");
        ShortcutSaveReferenceButton.Content = T("CharacterFollow.SaveCharacterReference");

        ShortcutsSendingExpander.Header = T("ShortcutPanel.SendingSettings");
        ShortcutsInputModeCard.Header = T("ShortcutPanel.InputMethod");
        AutomationProperties.SetName(ShortcutInputModePicker, T("ShortcutPanel.InputMethod"));
        _syncing = true;
        try
        {
            ShortcutInputModePicker.Items.Clear();
            ShortcutInputModePicker.Items.Add(new ComboBoxItem { Content = T("ShortcutPanel.ScanCodesPhysicalKeySimulation"), Tag = ShortcutInputMode.ScanCode });
            ShortcutInputModePicker.Items.Add(new ComboBoxItem { Content = T("ShortcutPanel.VirtualKeysLegacyCompatibility"), Tag = ShortcutInputMode.VirtualKey });
            ShortcutInputModePicker.SelectedIndex = ViewModel.InputMode == ShortcutInputMode.VirtualKey ? 1 : 0;
        }
        finally { _syncing = false; }
        ShortcutsTapDurationCard.Header = T("ShortcutPanel.TapDurationMs");
        ShortcutsHoldDurationCard.Header = T("ShortcutPanel.HoldTypeDurationMs");
        AutomationProperties.SetName(ShortcutTapDurationBox, T("ShortcutPanel.TapDurationMs"));
        AutomationProperties.SetName(ShortcutHoldDurationBox, T("ShortcutPanel.HoldTypeDurationMs"));
        ShortcutsNumLockCard.Header = T("ShortcutPanel.ToggleNumLock");
        ShortcutNumLockButton.Content = T("ShortcutPanel.ToggleNumLock");
    }

    // ToggleSwitch otherwise shows On/Off in the Windows display language, not the app language; the name is for Narrator.
    private void SetToggleText(ToggleSwitch toggle, string name)
    {
        toggle.OnContent = T("Common.On2");
        toggle.OffContent = T("Common.Off2");
        AutomationProperties.SetName(toggle, name);
    }

    private void OnRefreshWindowsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.RefreshWindows);

    private void OnReloadIniClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.ReloadIni);

    private void OnElevateClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.RestartElevated);

    private void OnOpenIniClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.OpenIni);

    private void OnOpenOverlayClicked(object sender, RoutedEventArgs e) => _services.Overlay.Open();

    private void OnNumLockClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.SendNumLock);

    private void OnClearRegionClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.ClearPresetPreviewRegion);

    private void OnSelectRegionClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.SelectCharacterRegion);

    private void OnSaveReferenceClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.SaveCharacterReference);

    private void OnOverlayOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing || !_liveSliders.Contains(ShortcutOverlayOpacitySlider) || (int)Math.Round(e.NewValue) == ViewModel.OverlayOpacity) return;
        ViewModel.OverlayOpacity = (int)Math.Round(e.NewValue);
    }

    private void OnOverlayElementOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing || !_liveSliders.Contains(ShortcutOverlayElementOpacitySlider) || (int)Math.Round(e.NewValue) == ViewModel.OverlayElementOpacity) return;
        ViewModel.OverlayElementOpacity = (int)Math.Round(e.NewValue);
    }

    private void OnTextColorPicked(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!_syncing) ViewModel.OverlayTextColor = ShortcutOverlayController.FormatTextColor(args.NewColor);
    }

    // Saved once the picker closes, not for every colour it passes through.
    private void OnTextColorFlyoutClosed(object sender, object e) => ViewModel.Request(ShortcutPageAction.SaveSettings);

    private void OnOverlaySettingToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not ToggleSwitch toggle) return;
        if (toggle == ShortcutOverlayAutoContrastToggle) ViewModel.OverlayAutoContrast = toggle.IsOn;
        else if (toggle == ShortcutSlotSwitchToggle) ViewModel.OverlaySlotSwitchEnabled = toggle.IsOn;
        else if (toggle == ShortcutCompactSlotsToggle) ViewModel.OverlayCompactSlots = toggle.IsOn;
        else if (toggle == ShortcutKeepMenuStateToggle) ViewModel.KeepMenuStateVariables = toggle.IsOn;
    }

    // Pictures can show a UID, a nickname or chat: the window asks first and turns them on only when the user agrees.
    private void OnPresetPicturesToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (ShortcutPresetPicturesToggle.IsOn && !ViewModel.CapturePresetPreviews) ViewModel.Request(ShortcutPageAction.EnablePresetPreviews);
        else ViewModel.CapturePresetPreviews = ShortcutPresetPicturesToggle.IsOn;
        SyncOverlaySettings();
    }

    private void OnAutoSaveChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ShortcutAutoSavePicker.SelectedItem is LoaderAutoSaveChoice choice && choice.Seconds != ViewModel.LoaderAutoSaveSeconds)
            ViewModel.Request(ShortcutPageAction.SetLoaderAutoSave, choice.Seconds);
    }

    // The window turns it back off when no region or reference is set up yet.
    private void OnCharacterFollowToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        ViewModel.CharacterFollowEnabled = ShortcutCharacterFollowToggle.IsOn;
        SyncOverlaySettings();
    }

    // Turning the switch on registers the chord shown; off releases it.
    private void OnHotkeyToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (ShortcutHotkeyToggle.IsOn) ApplyHotkeyChoice();
        else ViewModel.Request(ShortcutPageAction.DisableHotkey);
        SyncHotkeyStatus();
    }

    private void OnEmptyStateActionClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ModOptions.Count == 0) ViewModel.Request(ShortcutPageAction.OpenDashboard);
        else ViewModel.Request(ShortcutPageAction.OpenIni);
    }

    private void OnModPickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing) ViewModel.SelectedMod = ShortcutModPicker.SelectedItem as ShortcutModOption;
    }

    private void OnTargetPickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing) ViewModel.SelectedTarget = ShortcutTargetPicker.SelectedItem as ShortcutTarget;
    }

    private void OnInputToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        // Without a target the switch springs back: the view model never arms input without one.
        ViewModel.InputEnabled = ShortcutInputToggle.IsOn;
        SyncInputToggle();
    }

    private void OnHideUnsupportedToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) ViewModel.HideUnsupported = ShortcutHideUnsupportedToggle.IsOn;
    }

    private void OnInputModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ShortcutInputModePicker.SelectedItem is ComboBoxItem { Tag: ShortcutInputMode mode }) ViewModel.InputMode = mode;
    }

    private void OnDurationChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing) return;
        if (sender == ShortcutTapDurationBox) ViewModel.TapDuration = sender.Value;
        else ViewModel.HoldDuration = sender.Value;
    }

    // A chord takes effect as soon as it is chosen, and turns the hotkey on; there is nothing to apply.
    private void OnHotkeyPickerChanged(object sender, SelectionChangedEventArgs e) => ApplyHotkeyChoice();

    private void ApplyHotkeyChoice()
    {
        if (_syncing || ShortcutHotkeyModifiersPicker.SelectedItem is not ComboBoxItem { Tag: uint modifiers }
            || ShortcutHotkeyKeyPicker.SelectedItem is not ComboBoxItem { Tag: uint key }) return;
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
                SyncStatus();
                break;
            case nameof(ShortcutPageViewModel.InputEnabled):
                SyncInputToggle();
                SyncStatus();
                break;
            case nameof(ShortcutPageViewModel.StatusText):
            case nameof(ShortcutPageViewModel.OverlayVisible):
                SyncStatus();
                break;
            case nameof(ShortcutPageViewModel.Detected):
            case nameof(ShortcutPageViewModel.HideUnsupported):
            case nameof(ShortcutPageViewModel.ModsRead):
                SyncBindingsHeader();
                RenderBindings();
                SyncEmptyState();
                break;
            case nameof(ShortcutPageViewModel.PrivilegeText):
            case nameof(ShortcutPageViewModel.IsElevated):
            case nameof(ShortcutPageViewModel.PrivilegeWarning):
                SyncPrivilege();
                break;
            case nameof(ShortcutPageViewModel.HotkeyRegistered):
            case nameof(ShortcutPageViewModel.HotkeyRejected):
            case nameof(ShortcutPageViewModel.HotkeyModifiers):
            case nameof(ShortcutPageViewModel.HotkeyKey):
                SyncHotkeyStatus();
                break;
            case nameof(ShortcutPageViewModel.OverlayOpacity):
            case nameof(ShortcutPageViewModel.OverlayElementOpacity):
            case nameof(ShortcutPageViewModel.OverlayTextColor):
            case nameof(ShortcutPageViewModel.OverlayAutoContrast):
            case nameof(ShortcutPageViewModel.OverlaySlotSwitchEnabled):
            case nameof(ShortcutPageViewModel.OverlayCompactSlots):
            case nameof(ShortcutPageViewModel.CapturePresetPreviews):
            case nameof(ShortcutPageViewModel.KeepMenuStateVariables):
            case nameof(ShortcutPageViewModel.CharacterFollowEnabled):
            case nameof(ShortcutPageViewModel.CharacterFollowStatus):
            case nameof(ShortcutPageViewModel.CharacterFollowSummary):
            case nameof(ShortcutPageViewModel.LoaderAutoSaveChoices):
            case nameof(ShortcutPageViewModel.LoaderAutoSaveSeconds):
            case nameof(ShortcutPageViewModel.LoaderAutoSaveNote):
                SyncOverlaySettings();
                break;
            case nameof(ShortcutPageViewModel.InputMode):
            case nameof(ShortcutPageViewModel.TapDuration):
            case nameof(ShortcutPageViewModel.HoldDuration):
                SyncSending();
                SyncStatus();
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
        SyncSending();
        SyncStatus();
        SyncBindingsHeader();
        SyncEmptyState();
        SyncOverlaySettings();
    }

    private void SyncOverlaySettings()
    {
        _syncing = true;
        try
        {
            ShortcutOverlayOpacitySlider.Value = ViewModel.OverlayOpacity;
            ShortcutOverlayElementOpacitySlider.Value = ViewModel.OverlayElementOpacity;
            Windows.UI.Color color = ShortcutOverlayController.ParseTextColor(ViewModel.OverlayTextColor);
            ShortcutOverlayTextColorSwatch.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
            ShortcutOverlayTextColorTextBlock.Text = ViewModel.OverlayTextColor;
            if (ShortcutOverlayTextColorPicker.Color != color) ShortcutOverlayTextColorPicker.Color = color;
            ShortcutOverlayAutoContrastToggle.IsOn = ViewModel.OverlayAutoContrast;
            ShortcutSlotSwitchToggle.IsOn = ViewModel.OverlaySlotSwitchEnabled;
            ShortcutCompactSlotsToggle.IsOn = ViewModel.OverlayCompactSlots;
            ShortcutPresetPicturesToggle.IsOn = ViewModel.CapturePresetPreviews;
            ShortcutKeepMenuStateToggle.IsOn = ViewModel.KeepMenuStateVariables;
            ShortcutCharacterFollowToggle.IsOn = ViewModel.CharacterFollowEnabled;
            ShortcutsFollowExpander.Description = ViewModel.CharacterFollowSummary;
            ShortcutsFollowStatusCard.Header = ViewModel.CharacterFollowStatus;
            if (!ReferenceEquals(ShortcutAutoSavePicker.ItemsSource, ViewModel.LoaderAutoSaveChoices)) ShortcutAutoSavePicker.ItemsSource = ViewModel.LoaderAutoSaveChoices;
            ShortcutAutoSavePicker.SelectedItem = ViewModel.LoaderAutoSaveChoices.FirstOrDefault(choice => choice.Seconds == ViewModel.LoaderAutoSaveSeconds);
            ShortcutAutoSavePicker.IsEnabled = ViewModel.LoaderAutoSaveSeconds is not null;
            ShortcutsAutoSaveCard.Description = ViewModel.LoaderAutoSaveNote;
        }
        finally { _syncing = false; }
    }

    private void SyncModPicker()
    {
        _syncing = true;
        try
        {
            if (!ReferenceEquals(ShortcutModPicker.ItemsSource, ViewModel.ModOptions)) ShortcutModPicker.ItemsSource = ViewModel.ModOptions;
            ShortcutModPicker.SelectedItem = ViewModel.SelectedMod;
        }
        finally { _syncing = false; }
    }

    private void SyncTargetPicker()
    {
        _syncing = true;
        try
        {
            if (!ReferenceEquals(ShortcutTargetPicker.ItemsSource, ViewModel.Targets)) ShortcutTargetPicker.ItemsSource = ViewModel.Targets;
            ShortcutTargetPicker.SelectedItem = ViewModel.SelectedTarget;
        }
        finally { _syncing = false; }
    }

    private void SyncInputToggle()
    {
        _syncing = true;
        try { ShortcutInputToggle.IsOn = ViewModel.InputEnabled; }
        finally { _syncing = false; }
    }

    // One line on where keys go now, then the last result: ready (input on), waiting for the user, or no window.
    private void SyncStatus()
    {
        string mode = ViewModel.InputMode == ShortcutInputMode.VirtualKey ? T("ShortcutPanel.VirtualKeys") : T("ShortcutPanel.ScanCodes");
        ShortcutsStatusInfoBar.Title = T("ShortcutPanel.StatusSummary",
            ViewModel.SelectedTarget?.Label ?? T("ShortcutPanel.NoTargetYet"),
            ViewModel.InputEnabled ? T("Common.On2") : T("Common.Off2"),
            mode,
            ViewModel.OverlayVisible ? T("ShortcutPanel.OverlayShown") : T("ShortcutPanel.OverlayHidden"));
        ShortcutsStatusInfoBar.Message = ViewModel.StatusText;
        ShortcutsStatusInfoBar.Severity = ViewModel.SelectedTarget is null ? InfoBarSeverity.Warning
            : ViewModel.InputEnabled ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        // Without a window the next step is to look for it again.
        ShortcutsStatusRefreshButton.Visibility = ViewModel.SelectedTarget is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // No Mods in the repository, or a Mod without bindings: say what to do instead of showing an empty list.
    private void SyncEmptyState()
    {
        bool noMods = ViewModel.ModsRead && ViewModel.ModOptions.Count == 0;
        bool noBindings = !noMods && ViewModel.SelectedMod is { } mod && ViewModel.LoadedPath == mod.Path && ViewModel.Detected.Count == 0;
        ShortcutsEmptyState.Visibility = noMods || noBindings ? Visibility.Visible : Visibility.Collapsed;
        if (noMods)
        {
            ShortcutsEmptyState.Title = T("ShortcutPanel.NoModsTitle");
            ShortcutsEmptyState.Hint = T("ShortcutPanel.NoModsHint");
            ShortcutsEmptyStateButton.Content = T("ShortcutPanel.OpenDashboard");
        }
        else if (noBindings)
        {
            ShortcutsEmptyState.Title = T("ShortcutPanel.NoBindingsTitle");
            ShortcutsEmptyState.Hint = T("ShortcutPanel.NoBindingsHint");
            ShortcutsEmptyStateButton.Content = T("ShortcutPanel.OpenIni");
        }
    }

    private void SyncBindingsHeader()
    {
        _syncing = true;
        try { ShortcutHideUnsupportedToggle.IsOn = ViewModel.HideUnsupported; }
        finally { _syncing = false; }
        ShortcutsOpenIniMenuItem.IsEnabled = ViewModel.Detected.Count > 0;
    }

    private void SyncPrivilege()
    {
        ShortcutsPrivilegeInfoBar.Message = ViewModel.PrivilegeText;
        ShortcutsPrivilegeInfoBar.Severity = ViewModel.IsElevated ? InfoBarSeverity.Success
            : ViewModel.PrivilegeWarning ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        ShortcutsElevateButton.Visibility = ViewModel.IsElevated ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncHotkeyPickers()
    {
        _syncing = true;
        try
        {
            ShortcutHotkeyModifiersPicker.SelectedItem = ShortcutHotkeyModifiersPicker.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == ViewModel.HotkeyModifiers);
            ShortcutHotkeyKeyPicker.SelectedItem = ShortcutHotkeyKeyPicker.Items.Cast<ComboBoxItem>().First(i => (uint)i.Tag == ViewModel.HotkeyKey);
        }
        finally { _syncing = false; }
    }

    private void SyncHotkeyStatus()
    {
        SyncHotkeyPickers();
        _syncing = true;
        try { ShortcutHotkeyToggle.IsOn = ViewModel.HotkeyRegistered; }
        finally { _syncing = false; }
        (string modifiers, string key) = ViewModel.HotkeyLabel();
        // A working hotkey is already on the expander's description; the card explains only problems.
        ShortcutsHotkeyCard.Description = ViewModel.HotkeyRejected
            ? T("ShortcutPanel.HotkeyUnavailablePreviousSettingsRetainedChoose")
            : ViewModel.HotkeyRegistered ? null! : T("ShortcutPanel.HotkeyOffTurnOn");
        ShortcutsOverlayExpander.Description = ViewModel.HotkeyRegistered
            ? T("ShortcutPanel.OverlayHotkeySummary", modifiers, key)
            : T("ShortcutPanel.OverlayHotkeyOff");
    }

    private void SyncSending()
    {
        _syncing = true;
        try
        {
            ShortcutInputModePicker.SelectedIndex = ViewModel.InputMode == ShortcutInputMode.VirtualKey ? 1 : 0;
            if (!SameNumber(ShortcutTapDurationBox.Value, ViewModel.TapDuration)) ShortcutTapDurationBox.Value = ViewModel.TapDuration;
            if (!SameNumber(ShortcutHoldDurationBox.Value, ViewModel.HoldDuration)) ShortcutHoldDurationBox.Value = ViewModel.HoldDuration;
        }
        finally { _syncing = false; }
        string mode = ViewModel.InputMode == ShortcutInputMode.VirtualKey ? T("ShortcutPanel.VirtualKeys") : T("ShortcutPanel.ScanCodes");
        // A cleared box reads NaN; sending then falls back to the defaults, so the summary does too.
        ShortcutsSendingExpander.Description = T("ShortcutPanel.SendingSummary", mode, ViewModel.TapMilliseconds, ViewModel.HoldMilliseconds);
    }

    private static bool SameNumber(double a, double b) => a.Equals(b) || (double.IsNaN(a) && double.IsNaN(b));

    // Only while the page is on screen; it renders again when shown.
    private void RenderBindings()
    {
        if (!_built || !IsLoaded) return;
        DetectedModShortcut[] visible = ViewModel.VisibleShortcuts();
        if (_shownBindings.SequenceEqual(visible)) return;
        _shownBindings = visible;
        ShortcutBindingsList.Children.Clear();
        int hidden = ViewModel.Detected.Count - visible.Length;
        ShortcutsHiddenNoteTextBlock.Text = hidden > 0 ? T("ShortcutPanel.BindingsThatCannotBeSentAre", hidden) : string.Empty;
        ShortcutsHiddenNoteTextBlock.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var shortcut in visible)
        {
            if (!_bindingCards.TryGetValue(shortcut, out SettingsCard? card))
                _bindingCards[shortcut] = card = CreateBindingRow(shortcut);
            ShortcutBindingsList.Children.Add(card);
        }
        if (_bindingCards.Count > 768) _bindingCards.Clear();
    }

    // One row per binding: what it does and where it comes from, its keys as key caps, and Send.
    // A binding that cannot be sent is greyed out and says why.
    private SettingsCard CreateBindingRow(DetectedModShortcut shortcut)
    {
        ShortcutUnsupportedReason reason = ShortcutKeyPlan.Explain(shortcut.RawKey);
        bool supported = reason == ShortcutUnsupportedReason.None;
        string source = $"{shortcut.SourceFile} · [{shortcut.Section}]";
        if (shortcut.Condition.Length > 0) source += " · " + T("ShortcutPanel.Condition") + shortcut.Condition;
        if (shortcut.Behavior.Equals("hold", StringComparison.OrdinalIgnoreCase)) source += " · " + T("ShortcutPanel.HoldForConfiguredDuration");
        if (!supported) source += Environment.NewLine + T("ShortcutPanel.CannotSend") + UnsupportedReasonText(reason) + T("ShortcutPanel.Key", shortcut.RawKey);
        string description = ModShortcutDescriptionFormatter.Describe(shortcut, useEnglish: !_services.Language.IsChinese);

        var send = new Button { Content = T("ShortcutPanel.SendKey"), IsEnabled = supported };
        AutomationProperties.SetName(send, T("ShortcutPanel.SendKey") + " " + shortcut.Shortcut);
        send.Click += (_, _) => ViewModel.Request(ShortcutPageAction.SendShortcut, shortcut);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        content.Children.Add(new KeyVisual { Keys = shortcut.Shortcut, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(send);
        return new SettingsCard
        {
            Header = description,
            Description = source,
            Content = content,
            IsEnabled = supported
        };
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
