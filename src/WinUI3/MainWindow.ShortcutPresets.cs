using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private bool _savingOverlaySinglePreset;

    private async Task SaveSingleModPresetFromOverlayAsync()
    {
        if (_savingOverlaySinglePreset || _applyingCombinationProfile || (_shortcuts.SendInProgress && !_services.Slots.IsRestoring)) return;
        var overlay = _overlay.Window;
        if (overlay?.Content is not FrameworkElement root) return;
        // Freeze the repository and Mod at click time, independently of the main repository selection
        // and automatic character following. The dialog identifies exactly this Mod.
        WorkspaceRepository? repository = GetSelectedRepository();
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
        SetCombinationBusy(true);
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
            try { SaveShellConfig(throwOnError: true); }
            catch { _services.Slots.Items.Remove(slot); _services.PresetPreviews.DeletePresetPreview(slot.PreviewFile); throw; }
            _selectedModPersistentSlotId = slot.Id;
            _shortcuts.StatusText = T("ShortcutPanel.SinglePresetSaved", name, values.Count) + _services.PresetPreviews.PresetPreviewNote(slot.PreviewFile)
                + (menuStateLeft > 0 ? T("SlotCapture.MenuStateLeftOut", menuStateLeft) : string.Empty);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay single Mod preset capture", ex);
            if (!_services.IsClosing) _shortcuts.ReportError(T("ShortcutPanel.PresetNotSaved") + OverlayCaptureError(ex));
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (!_services.IsClosing)
            {
                SetCombinationBusy(false);
                RefreshPersistentSlotControls();
                RefreshShortcutOverlay(bindingsChanged: true);
            }
        }
    }
}
