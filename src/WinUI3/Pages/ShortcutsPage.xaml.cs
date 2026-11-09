using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Shortcuts page (stage J5, docs/development/UI-Structure-Plan.md and UI-Fluent-Design-Plan.md): which Mod and
/// game window to send to, the Mod's key bindings and the settings of the overlay, character follow and sending. It
/// shows <see cref="ShortcutPageViewModel"/>, opens the overlay through <see cref="IShortcutOverlay"/> and sends
/// everything else to the window as requests. The settings blocks the window still builds (IShortcutPageSettings)
/// are added on first use, like the whole page before the move.
/// </summary>
public sealed partial class ShortcutsPage : UserControl
{
    private readonly AppServices _services;
    private readonly Dictionary<DetectedModShortcut, SettingsCard> _bindingCards = new();
    private DetectedModShortcut[] _shownBindings = [];
    private bool _built;
    private bool _syncing;
    private Slider? _opacitySlider;

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

        // The blocks the window still builds (stage J5b moves them into the view model).
        ShortcutsOverlayAppearancePanel.Children.Clear();
        ShortcutsOverlayBehaviorPanel.Children.Clear();
        ShortcutsFollowPanel.Children.Clear();
        _opacitySlider = CreateOpacitySlider(T("ShortcutPanel.OverlayOpacity"));
        _services.ShortcutSettings.AddOverlayAppearance(ShortcutsOverlayAppearancePanel, _opacitySlider);
        _services.ShortcutSettings.AddOverlayBehavior(ShortcutsOverlayBehaviorPanel);
        (ToggleSwitch followToggle, TextBlock followSummary) = _services.ShortcutSettings.AddCharacterFollow(ShortcutsFollowPanel);
        ShortcutsFollowToggleHost.Content = followToggle;
        // The summary is kept up to date by the window; the expander shows its text.
        ShortcutsFollowExpander.Description = followSummary.Text;
        followSummary.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => ShortcutsFollowExpander.Description = followSummary.Text);

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
        ShortcutsHotkeyCard.Header = T("ShortcutPanel.HotkeyGroup");
        ShortcutHotkeyModifiersPicker.Header = T("ShortcutPanel.ShowHideOverlay");
        ShortcutHotkeyKeyPicker.Header = T("ShortcutPanel.Key2");
        ShortcutHotkeyApplyButton.Content = T("Common.Apply");
        ShortcutHotkeyDisableButton.Content = T("ShortcutPanel.Disable");
        ShortcutsOverlayAppearanceTitleTextBlock.Text = T("ShortcutPanel.AppearanceGroup");
        ShortcutsOverlayBehaviorTitleTextBlock.Text = T("ShortcutPanel.BehaviorGroup");
        ShortcutsOverlayBehaviorNoteTextBlock.Text = T("ShortcutPanel.TheOverlaySupportsWindowedBorderlessMode");

        ShortcutsFollowExpander.Header = T("CharacterFollow.Title");

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

    private void OnRefreshWindowsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.RefreshWindows);

    private void OnReloadIniClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.ReloadIni);

    private void OnElevateClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.RestartElevated);

    private void OnOpenIniClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.OpenIni);

    private void OnOpenOverlayClicked(object sender, RoutedEventArgs e) => _services.Overlay.Open();

    private void OnNumLockClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.SendNumLock);

    private void OnHotkeyDisableClicked(object sender, RoutedEventArgs e) => ViewModel.Request(ShortcutPageAction.DisableHotkey);

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

    // A choice takes effect at once; Apply remains to turn the hotkey back on after Disable.
    private void OnHotkeyPickerChanged(object sender, SelectionChangedEventArgs e) => ApplyHotkeyChoice();

    private void OnHotkeyApplyClicked(object sender, RoutedEventArgs e) => ApplyHotkeyChoice();

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
                break;
            case nameof(ShortcutPageViewModel.InputEnabled):
                SyncInputToggle();
                break;
            case nameof(ShortcutPageViewModel.StatusText):
                SyncStatus();
                break;
            case nameof(ShortcutPageViewModel.Detected):
            case nameof(ShortcutPageViewModel.HideUnsupported):
                SyncBindingsHeader();
                RenderBindings();
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
                _syncing = true;
                try { if (_opacitySlider is not null) _opacitySlider.Value = ViewModel.OverlayOpacity; }
                finally { _syncing = false; }
                break;
            case nameof(ShortcutPageViewModel.InputMode):
            case nameof(ShortcutPageViewModel.TapDuration):
            case nameof(ShortcutPageViewModel.HoldDuration):
                SyncSending();
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

    private void SyncStatus()
    {
        ShortcutsStatusTextBlock.Text = ViewModel.StatusText;
        // The full message stays available when trimmed.
        ToolTipService.SetToolTip(ShortcutsStatusTextBlock, string.IsNullOrEmpty(ViewModel.StatusText) ? null : ViewModel.StatusText);
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
        (string modifiers, string key) = ViewModel.HotkeyLabel();
        // A working hotkey is already on the expander's description; the line below explains only problems.
        ShortcutHotkeyStatusTextBlock.Text = ViewModel.HotkeyRejected
            ? T("ShortcutPanel.HotkeyUnavailablePreviousSettingsRetainedChoose")
            : ViewModel.HotkeyRegistered ? string.Empty : T("ShortcutPanel.GlobalHotkeyDisabledOrUnavailableChoose");
        ShortcutHotkeyStatusTextBlock.Visibility = ShortcutHotkeyStatusTextBlock.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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
