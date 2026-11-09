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
    private bool CanHotReloadLoader => _shortcuts.InputEnabled && _shortcuts.SelectedTarget is ShortcutTarget;

    // Sends F10 with d3dx_user.ini kept read-only and describes the outcome.
    private async Task<string> HotReloadLoaderAsync(string userIni, Action? keySent = null)
    {
        if (!CanHotReloadLoader || _shortcuts.SelectedTarget is not ShortcutTarget target)
        {
            return T("HotReload.NotArmed");
        }

        var plan = new ShortcutKeyPlan([LoaderReloadKey], []);
        LoaderHotReloadResult result = await LoaderHotReload.ReloadAsync(userIni,
            async _ =>
            {
                await _shortcutInput.SendAsync(target, plan, 120, _shortcuts.InputMode, _services.Language.Current);
                keySent?.Invoke();
            },
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

    private ModPersistentSlot[] OverlaySlots(out string? modPath)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        modPath = _shortcuts.SelectedMod?.Path;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
        return !_shortcuts.OverlaySlotSwitchEnabled || repository is null || relative is null ? [] : _modPersistentSlots
            .Where(slot => slot.RepositoryId == repository.Id && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))
            .OrderBy(slot => slot.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    // In game, without dialogs: write the slot's keys into d3dx_user.ini, then reload with F10.
    private async Task SwitchSlotFromOverlayAsync(string slotId, string modPath)
    {
        if (_savingOverlaySinglePreset || _applyingCombinationProfile) return;
        if (_restoringPersistentRuntime || _shortcuts.SendInProgress)
        {
            _shortcuts.StatusText = T("HotReload.StillApplying");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        WorkspaceRepository? repository = GetSelectedRepository();
        ModPersistentSlot? slot = _modPersistentSlots.FirstOrDefault(item => item.Id == slotId);
        string? userIni = repository?.PersistentUserIniPath is { Length: > 0 } configured ? configured : TryGetDefaultPersistentIniPath(repository);
        if (repository is null || slot is null || userIni is null || !File.Exists(userIni))
        {
            _shortcuts.StatusText = T("HotReload.SetTheUserIniFirst");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }
        if (!CanHotReloadLoader)
        {
            _shortcuts.StatusText = T("HotReload.NotArmed");
            RefreshShortcutOverlay(bindingsChanged: true);
            return;
        }

        _restoringPersistentRuntime = true;
        _shortcuts.SendInProgress = true;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string deployedName = ModDeploymentLayout.DeployedName(modPath);
            if (string.IsNullOrWhiteSpace(repository.TargetPath) || !Directory.Exists(ModDeploymentLayout.DeployedPath(repository.TargetPath, modPath)))
                throw new InvalidDataException(T("PersistentSlots.DeployThisModUnderItsMatching"));
            IReadOnlyList<ModPersistentValue> values = await Task.Run(() => ValuesToRestore(slot, modPath));
            ModPersistentRuntimeRestorePlan plan = await Task.Run(() => ModPersistentPresetEngine.PlanRuntimeRestore(modPath, deployedName, userIni, values));
            slot.Values = [.. values];
            await Task.Run(() => ModPersistentPresetEngine.ApplyRuntimeRestore(plan));
            slot.ActivatedAtUtc = DateTimeOffset.UtcNow;
            SaveShellConfig();
            long written = clock.ElapsedMilliseconds;
            // The file stays read-only for a few seconds after the key, so report the switch as soon as the key is out.
            _shortcuts.StatusText = $"{slot.Name} · " + await HotReloadLoaderAsync(userIni, () =>
            {
                TraceStartupStage($"Slot switch: values written {written} ms, reload key sent {clock.ElapsedMilliseconds} ms");
                _shortcuts.StatusText = $"{slot.Name} · " + T("HotReload.Applying");
                _overlay.InvalidateSlots();
                RefreshShortcutOverlay(bindingsChanged: true);
            });
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay slot switch", ex);
            _shortcuts.StatusText = T("HotReload.SwitchFailed", slot.Name) + ex.Message;
        }
        finally
        {
            _restoringPersistentRuntime = false;
            _shortcuts.SendInProgress = false;
            _overlay.InvalidateSlots();
            if (!_onlineWindowClosed)
            {
                RefreshShortcutOverlay(bindingsChanged: true);
                RefreshPersistentSlotControls();
            }
        }
    }
}
