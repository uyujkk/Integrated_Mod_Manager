using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// Saving presets from the overlay: the enabled Mod combination, or one Mod's state as a slot. The loader is asked to
// write its state first (F10 to the chosen window); when it cannot or writes nothing, the user decides whether to save
// from d3dx_user.ini as it is.
internal sealed partial class ShortcutPanelService
{
    private bool _savingOverlaySinglePreset;

    /// <summary>Flush loader memory before capturing; offer an explicit disk-only fallback when input is unavailable.</summary>
    private async Task QuickSaveCombinationFromOverlayAsync()
    {
        if (_savingOverlaySinglePreset || _services.Combinations.IsApplying || (_shortcuts.SendInProgress && !_services.Slots.IsRestoring)) return;
        if (_services.Repositories.Selected is not { } repository)
        {
            _shortcuts.StatusText = T("ShortcutPanel.SelectARepositoryInTheMain");
            RefreshShortcutOverlay();
            return;
        }
        _savingOverlaySinglePreset = true;
        if (!await WaitForOverlayCaptureAsync()) { _savingOverlaySinglePreset = false; return; }
        string? userIni = repository.PersistentUserIniPath is { Length: > 0 } configured ? configured : SlotService.TryGetDefaultPersistentIniPath(repository);
        bool includeState = userIni is not null && File.Exists(userIni);
        string name = T("ShortcutPanel.InGame", DateTime.Now);
        _shortcuts.StatusText = T("ShortcutPanel.SavingCombinationPreset");
        RefreshShortcutOverlay();
        _services.Combinations.SetBusy(true);
        try
        {
            CharacterFrame? preview = await _services.PresetPreviews.CapturePresetPreviewAsync();
            // Without a d3dx_user.ini there is no loader state to flush: save the Mod combination alone, as before.
            if (includeState && !await PrepareOverlayCaptureAsync(userIni!)) return;
            (int Mods, int Parameters)? saved = await _services.Combinations.CaptureAsync(repository, name, null, includeState, userIni ?? string.Empty,
                () => Task.FromResult(false));
            string previewNote = string.Empty;
            if (saved is not null && _services.Combinations.Profiles.FirstOrDefault(item => item.Id == _services.Combinations.SelectedId) is { } profile)
            {
                profile.PreviewFile = await _services.PresetPreviews.StorePresetPreviewAsync(repository.Id, profile.Id, preview);
                if (profile.PreviewFile is not null) _services.Configuration.SaveShell();
                previewNote = _services.PresetPreviews.PresetPreviewNote(profile.PreviewFile);
            }
            _shortcuts.StatusText = saved is (int mods, int parameters)
                ? T("ShortcutPanel.SavedPresetModsParameters", name, mods, parameters) + previewNote
                : T("ShortcutPanel.NoEnabledModsThePresetWas");
        }
        catch (Exception ex)
        {
            _log("Overlay combination capture", ex);
            if (!_services.IsClosing) _shortcuts.ReportError(T("ShortcutPanel.PresetNotSaved") + OverlayCaptureError(ex));
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (!_services.IsClosing)
            {
                _services.Combinations.SetBusy(false);
                RefreshShortcutOverlay(bindingsChanged: true);
            }
        }
    }

    private async Task SaveSingleModPresetFromOverlayAsync()
    {
        if (_savingOverlaySinglePreset || _services.Combinations.IsApplying || (_shortcuts.SendInProgress && !_services.Slots.IsRestoring)) return;
        var overlay = _overlay.Window;
        if (overlay?.Content is not FrameworkElement root) return;
        // Freeze the repository and Mod at click time, independently of the main repository selection
        // and automatic character following. The dialog identifies exactly this Mod.
        WorkspaceRepository? repository = _services.Repositories.Selected;
        string? modPath = _overlay.ModOnOverlay?.Path;
        string? relative = SlotService.TryGetCurrentModRelativePath(repository, modPath);
        if (repository is null || modPath is null || relative is null)
        {
            _shortcuts.StatusText = T("ShortcutPanel.SelectAMod");
            RefreshShortcutOverlay();
            return;
        }

        _savingOverlaySinglePreset = true;
        if (!await WaitForOverlayCaptureAsync()) { _savingOverlaySinglePreset = false; return; }
        _services.Combinations.SetBusy(true);
        try
        {
            string? userIni = repository.PersistentUserIniPath is { Length: > 0 } configured
                ? configured : SlotService.TryGetDefaultPersistentIniPath(repository);
            if (userIni is null || !File.Exists(userIni))
                throw new InvalidDataException(T("HotReload.SetTheUserIniFirst"));
            if (string.IsNullOrWhiteSpace(repository.TargetPath)
                || !Directory.Exists(ModDeploymentLayout.DeployedPath(repository.TargetPath, modPath)))
                throw new InvalidDataException(T("PersistentSlots.DeployThisModUnderItsMatching"));

            (ContentDialog dialog, TextBox nameBox) = SingleModPresetDialog.Create(_services, root.XamlRoot, relative,
                candidate => _services.Slots.Items.Any(slot => slot.RepositoryId == repository.Id
                    && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(slot.Name, candidate, StringComparison.OrdinalIgnoreCase)));
            overlay.SetKeyboardInputEnabled(true);
            ContentDialogResult result;
            try { result = await _services.Dialogs.ShowAsync(dialog); }
            finally
            {
                if (ReferenceEquals(_overlay.Window, overlay)) overlay.SetKeyboardInputEnabled(false);
                if (_shortcuts.SelectedTarget is ShortcutTarget target) ShortcutInputService.FocusTarget(target);
            }
            if (result != ContentDialogResult.Primary || _services.IsClosing) return;

            CharacterFrame? preview = await _services.PresetPreviews.CapturePresetPreviewAsync();
            if (!await PrepareOverlayCaptureAsync(userIni)) return;
            string name = nameBox.Text.Trim();
            _shortcuts.StatusText = T("ShortcutPanel.SavingSinglePreset");
            RefreshShortcutOverlay();
            // Use the strict snapshot reader for one Mod: missing values or a concurrent loader
            // write fail rather than quietly producing a partial preset or capturing defaults.
            var states = await Task.Run(() => ModPersistentPresetEngine.CaptureCombination(
                [(modPath, ModDeploymentLayout.DeployedName(modPath))], userIni));
            var captured = states.Single().Values;
            if (captured.Count == 0)
                throw new InvalidDataException(T("PersistentSlots.NoNumericPersistentValuesMatchedThis"));
            var (values, menuStateLeft) = _services.Slots.FilterSlotValues(captured);
            if (_services.IsClosing) return;
            var slot = new ModPersistentSlot
            {
                RepositoryId = repository.Id, ModRelativePath = relative, Name = name,
                Values = [.. values], ActivatedAtUtc = DateTimeOffset.UtcNow
            };
            await _services.Slots.EnsureDefaultSlotAsync(repository, modPath, relative);
            slot.PreviewFile = await _services.PresetPreviews.StorePresetPreviewAsync(repository.Id, slot.Id, _services.PresetPreviews.CropToDefaultRegion(preview));
            _services.Slots.Items.Add(slot);
            try { _services.Configuration.SaveShell(throwOnError: true); }
            catch { _services.Slots.Items.Remove(slot); _services.PresetPreviews.DeletePresetPreview(slot.PreviewFile); throw; }
            _services.Slots.SelectedId = slot.Id;
            _shortcuts.StatusText = T("ShortcutPanel.SinglePresetSaved", name, values.Count) + _services.PresetPreviews.PresetPreviewNote(slot.PreviewFile)
                + (menuStateLeft > 0 ? T("SlotCapture.MenuStateLeftOut", menuStateLeft) : string.Empty);
        }
        catch (Exception ex)
        {
            _log("Overlay single Mod preset capture", ex);
            if (!_services.IsClosing) _shortcuts.ReportError(T("ShortcutPanel.PresetNotSaved") + OverlayCaptureError(ex));
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (!_services.IsClosing)
            {
                _services.Combinations.SetBusy(false);
                _refreshSlotControls();
                RefreshShortcutOverlay(bindingsChanged: true);
            }
        }
    }

    private async Task<bool> WaitForOverlayCaptureAsync()
    {
        // A hot switch owns the file until its read-only hold finishes. Queue a save behind it.
        while (_services.Slots.IsRestoring && !_services.IsClosing) await Task.Delay(50);
        return !_services.IsClosing && !_services.Combinations.IsApplying;
    }

    private async Task<bool> PrepareOverlayCaptureAsync(string userIni)
    {
        bool canSend = _services.Slots.CanHotReloadLoader;
        if (_shortcuts.SelectedTarget is ShortcutTarget target)
        {
            var own = ShortcutProcessSecurity.Read((uint)Environment.ProcessId);
            var destination = ShortcutProcessSecurity.Read(target.ProcessId);
            canSend &= ShortcutPrivilegePolicy.Evaluate(own.Integrity, own.UiAccess, destination.Integrity, destination.Error)
                == ShortcutPrivilegeVerdict.Allowed;
        }
        if (!canSend) return await AskToReadExistingFileAsync(T("ShortcutCapture.CannotSend"), T("ShortcutCapture.ReadExistingWarning"));
        _shortcuts.StatusText = T("ShortcutCapture.Waiting");
        RefreshShortcutOverlay();
        _shortcuts.SendInProgress = true;
        try
        {
            var chosen = _shortcuts.SelectedTarget!;
            await LoaderStateCapture.FlushAsync(userIni,
                _ => _shortcutInput.SendAsync(chosen, new ShortcutKeyPlan([SlotService.LoaderReloadKey], []), 120, _shortcuts.InputMode, _services.Language.Current));
            return !_services.IsClosing;
        }
        catch (TimeoutException)
        {
            // The loader writes nothing when no value changed since its last save (common for a new Mod),
            // which looks the same as a missed F10. Let the user decide instead of refusing outright.
        }
        finally { _shortcuts.SendInProgress = false; }
        bool readExisting = !_services.IsClosing
            && await AskToReadExistingFileAsync(T("ShortcutCapture.NoNewWrite"), T("ShortcutCapture.NoNewWriteWarning"));
        if (!readExisting && !_services.IsClosing)
        {
            _shortcuts.ReportError(T("ShortcutPanel.PresetNotSaved") + T("ShortcutCapture.TimedOut"));
            RefreshShortcutOverlay();
        }
        return readExisting;
    }

    // Asks in the overlay whether to save from the parameter file as it is on disk.
    private async Task<bool> AskToReadExistingFileAsync(string title, string message)
    {
        var overlay = _overlay.Window;
        if (overlay?.Content is not FrameworkElement root) return false;
        overlay.SetKeyboardInputEnabled(true);
        try { return await ReadExistingFileDialog.ConfirmAsync(_services, root.XamlRoot, title, message) && !_services.IsClosing; }
        finally
        {
            if (ReferenceEquals(_overlay.Window, overlay)) overlay.SetKeyboardInputEnabled(false);
            if (_shortcuts.SelectedTarget is ShortcutTarget target) ShortcutInputService.FocusTarget(target);
        }
    }

    private string OverlayCaptureError(Exception error) => error switch
    {
        TimeoutException => T("ShortcutCapture.TimedOut"),
        _ when error.Data["MissingLoaderValue"] is string variable => T("ShortcutCapture.MissingValue", variable),
        _ => error.Message
    };

    private async Task RefreshOverlayFromDiskAsync()
    {
        if (_services.Combinations.IsApplying || _services.Slots.IsRestoring || _shortcuts.SendInProgress) return;
        await RefreshDeployedModsAsync();
        await LoadShortcutSelectionAsync(force: true);
        if (_shortcuts.SelectedMod is null) _shortcuts.StatusText = T("ShortcutCapture.CurrentRemoved");
        _refreshSlotControls();
        RefreshShortcutOverlay(bindingsChanged: true);
    }
}
