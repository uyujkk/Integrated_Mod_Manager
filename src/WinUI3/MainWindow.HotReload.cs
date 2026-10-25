using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Experimental hot apply: after writing d3dx_user.ini while the game runs, send the loader's
// reload key (F10) to the game window chosen on the shortcut page, with the file kept read-only
// (LoaderHotReload). It reuses the shortcut input service, so the same target, privilege and
// held-key checks apply. The overlay can switch state slots this way when the user enables it.
public sealed partial class MainWindow
{
    private const ushort LoaderReloadKey = 0x79; // F10, the 3DMigoto/XXMI default reload_config key.
    private bool _overlaySlotSwitchEnabled;
    private StackPanel? _overlaySlotList;
    private ScrollViewer? _overlaySlotScroll;
    private TextBlock? _overlaySlotHeader;
    private string _overlaySlotSignature = string.Empty;

    private bool CanHotReloadLoader => _shortcutInputEnabled && _shortcutTargetPicker?.SelectedItem is ShortcutTarget;

    // Sends F10 with d3dx_user.ini kept read-only and describes the outcome.
    private async Task<string> HotReloadLoaderAsync(string userIni)
    {
        if (!CanHotReloadLoader || _shortcutTargetPicker?.SelectedItem is not ShortcutTarget target)
        {
            return T("HotReload.NotArmed");
        }

        var plan = new ShortcutKeyPlan([LoaderReloadKey], []);
        LoaderHotReloadResult result = await LoaderHotReload.ReloadAsync(userIni,
            _ => _shortcutInput.SendAsync(target, plan, 120, _shortcutInputMode, IsEnglishUi),
            LoaderHotReload.DefaultHold);
        return result switch
        {
            { ReloadSent: false } => T("HotReload.KeyNotSent", result.SendError ?? string.Empty),
            { FileKept: false } => T("HotReload.FileOverwritten"),
            _ => T("HotReload.ReloadSent")
        };
    }

    // The checkbox restore dialogs show next to "I have fully closed the game".
    private CheckBox CreateHotReloadOption(CheckBox closedCheck, ContentDialog dialog)
    {
        var hot = new CheckBox
        {
            Content = CanHotReloadLoader ? T("HotReload.ApplyWhileRunning") : T("HotReload.ApplyWhileRunningUnavailable"),
            IsEnabled = CanHotReloadLoader
        };
        void Update(object sender, RoutedEventArgs e) => dialog.IsPrimaryButtonEnabled = closedCheck.IsChecked == true || hot.IsChecked == true;
        hot.Checked += Update;
        hot.Unchecked += Update;
        return hot;
    }

    private void AddOverlaySlotSwitchSetting(StackPanel pane)
    {
        var toggle = new ToggleSwitch
        {
            OnContent = T("Common.On2"), OffContent = T("Common.Off2"),
            Header = T("HotReload.SwitchSlotsInTheOverlay"),
            IsOn = _overlaySlotSwitchEnabled
        };
        toggle.Toggled += (_, _) =>
        {
            _overlaySlotSwitchEnabled = toggle.IsOn;
            SaveShellConfig();
            _overlaySlotSignature = string.Empty;
            RefreshShortcutOverlay(bindingsChanged: true);
        };
        pane.Children.Add(toggle);
    }

    private void AddOverlaySlotSection(StackPanel panel)
    {
        _overlaySlotHeader = new TextBlock { FontSize = 12, Foreground = _overlayTextBrush, Opacity = .8 };
        _overlaySlotList = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _overlaySlotScroll = new ScrollViewer
        {
            Content = _overlaySlotList, Height = 40,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled
        };
        panel.Children.Add(_overlaySlotHeader);
        panel.Children.Add(_overlaySlotScroll);
        _overlaySlotSignature = string.Empty;
    }

    // Slots of the Mod chosen in the overlay; rebuilt only when they change.
    private void UpdateOverlaySlotSection()
    {
        if (_overlaySlotList is null || _overlaySlotHeader is null || _overlaySlotScroll is null) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        string? modPath = (_shortcutModPicker?.SelectedItem as ShortcutModOption)?.Path;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
        ModPersistentSlot[] slots = !_overlaySlotSwitchEnabled || repository is null || relative is null ? [] : _modPersistentSlots
            .Where(slot => slot.RepositoryId == repository.Id && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))
            .OrderBy(slot => slot.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        ModPersistentSlot? current = CurrentPersistentSlot(slots);
        string signature = $"{_overlaySlotSwitchEnabled}|{relative}|{current?.Id}|" + string.Join(",", slots.Select(slot => slot.Id + slot.Name));
        if (signature == _overlaySlotSignature) return;
        _overlaySlotSignature = signature;

        Visibility visibility = _overlaySlotSwitchEnabled ? Visibility.Visible : Visibility.Collapsed;
        _overlaySlotHeader.Visibility = visibility;
        _overlaySlotScroll.Visibility = visibility;
        _overlaySlotHeader.Text = slots.Length == 0 ? T("HotReload.NoSlotsForThisMod") : T("HotReload.StateSlotsHotSwitch");
        _overlaySlotList.Children.Clear();
        foreach (ModPersistentSlot slot in slots)
        {
            var button = new Button
            {
                Content = ReferenceEquals(slot, current) ? "● " + slot.Name : slot.Name,
                Foreground = _overlayTextBrush, AllowFocusOnInteraction = false, Padding = new Thickness(10, 4, 10, 5), FontSize = 13
            };
            button.Click += async (_, _) => await RunUiActionAsync("Overlay slot switch", () => SwitchSlotFromOverlayAsync(slot.Id, modPath!));
            _overlaySlotList.Children.Add(button);
        }
    }

    // In game, without dialogs: write the slot's keys into d3dx_user.ini, then reload with F10.
    private async Task SwitchSlotFromOverlayAsync(string slotId, string modPath)
    {
        if (_savingOverlaySinglePreset || _restoringPersistentRuntime || _applyingCombinationProfile || _shortcutSendInProgress) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        ModPersistentSlot? slot = _modPersistentSlots.FirstOrDefault(item => item.Id == slotId);
        string? userIni = repository?.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        if (repository is null || slot is null || userIni is null || !File.Exists(userIni))
        {
            _shortcutStatus!.Text = T("HotReload.SetTheUserIniFirst");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        if (!CanHotReloadLoader)
        {
            _shortcutStatus!.Text = T("HotReload.NotArmed");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }

        _restoringPersistentRuntime = true;
        _shortcutSendInProgress = true;
        try
        {
            string deployedName = ModDeploymentLayout.DeployedName(modPath);
            if (string.IsNullOrWhiteSpace(repository.TargetPath) || !Directory.Exists(ModDeploymentLayout.DeployedPath(repository.TargetPath, modPath)))
                throw new InvalidDataException(T("PersistentSlots.DeployThisModUnderItsMatching"));
            ModPersistentRuntimeRestorePlan plan = await Task.Run(() => ModPersistentPresetEngine.PlanRuntimeRestore(modPath, deployedName, userIni, slot.Values));
            await Task.Run(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
            slot.ActivatedAtUtc = DateTimeOffset.UtcNow;
            SaveShellConfig();
            _shortcutStatus!.Text = $"{slot.Name} · " + await HotReloadLoaderAsync(userIni);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay slot switch", ex);
            _shortcutStatus!.Text = T("HotReload.SwitchFailed", slot.Name) + ex.Message;
        }
        finally
        {
            _restoringPersistentRuntime = false;
            _shortcutSendInProgress = false;
            _overlaySlotSignature = string.Empty;
            if (!_onlineWindowClosed)
            {
                RefreshShortcutOverlay(bindingsChanged: true);
                RefreshPersistentSlotControls();
            }
        }
    }
}
