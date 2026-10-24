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
        if (_savingOverlaySinglePreset || _applyingCombinationProfile || _restoringPersistentRuntime || _shortcutSendInProgress) return;
        var overlay = _shortcutOverlay;
        if (overlay?.Content is not FrameworkElement root) return;
        // Freeze the repository and Mod at click time, independently of the main repository selection
        // and automatic character following. The dialog identifies exactly this Mod.
        WorkspaceRepository? repository = GetSelectedRepository();
        string? modPath = (_overlayModPicker?.SelectedItem as ShortcutModOption)?.Path;
        string? relative = TryGetCurrentModRelativePath(repository, modPath);
        if (repository is null || modPath is null || relative is null)
        {
            _shortcutStatus!.Text = T("ShortcutPanel.SelectAMod");
            RefreshShortcutOverlay();
            return;
        }

        _savingOverlaySinglePreset = true;
        SetCombinationBusy(true);
        try
        {
            string? userIni = repository.PersistentUserIniPath is { Length: > 0 } configured
                ? configured : TryGetDefaultPersistentIniPath(repository);
            if (userIni is null || !File.Exists(userIni))
                throw new InvalidDataException(T("HotReload.SetTheUserIniFirst"));
            if (string.IsNullOrWhiteSpace(repository.TargetPath)
                || !Directory.Exists(ModDeploymentLayout.DeployedPath(repository.TargetPath, modPath)))
                throw new InvalidDataException(T("PersistentSlots.DeployThisModUnderItsMatching"));

            var nameBox = new TextBox { Header = T("ShortcutPanel.SinglePresetName"), MaxLength = 120 };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock { Text = relative, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = T("ShortcutPanel.SinglePresetHint"), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(nameBox);
            content.Children.Add(error);
            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark,
                Title = T("ShortcutPanel.SaveSingleModPreset"), Content = content,
                PrimaryButtonText = T("PersistentSlots.SaveSlot"), CloseButtonText = T("Common.Cancel"),
                IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary
            };
            nameBox.TextChanged += (_, _) =>
            {
                string candidate = nameBox.Text.Trim();
                bool duplicate = _modPersistentSlots.Any(slot => slot.RepositoryId == repository.Id
                    && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(slot.Name, candidate, StringComparison.OrdinalIgnoreCase));
                error.Text = duplicate ? T("ShortcutPanel.SinglePresetDuplicate") : string.Empty;
                dialog.IsPrimaryButtonEnabled = candidate.Length > 0 && !duplicate;
            };
            dialog.Opened += (_, _) => nameBox.Focus(FocusState.Programmatic);
            overlay.SetKeyboardInputEnabled(true);
            ContentDialogResult result;
            try { result = await dialog.ShowAsync(); }
            finally
            {
                if (ReferenceEquals(_shortcutOverlay, overlay)) overlay.SetKeyboardInputEnabled(false);
                if (_shortcutTargetPicker?.SelectedItem is ShortcutTarget target) ShortcutInputService.FocusTarget(target);
            }
            if (result != ContentDialogResult.Primary || _onlineWindowClosed) return;

            string name = nameBox.Text.Trim();
            _shortcutStatus!.Text = T("ShortcutPanel.SavingSinglePreset");
            RefreshShortcutOverlay();
            // Use the strict snapshot reader for one Mod: missing values or a concurrent loader
            // write fail rather than quietly producing a partial preset or capturing defaults.
            var states = await Task.Run(() => ModPersistentPresetEngine.CaptureCombination(
                [(modPath, ModDeploymentLayout.DeployedName(modPath))], userIni));
            var values = states.Single().Values;
            if (values.Count == 0)
                throw new InvalidDataException(T("PersistentSlots.NoNumericPersistentValuesMatchedThis"));
            if (_onlineWindowClosed) return;
            var slot = new ModPersistentSlot
            {
                RepositoryId = repository.Id, ModRelativePath = relative, Name = name,
                Values = [.. values], ActivatedAtUtc = DateTimeOffset.UtcNow
            };
            _modPersistentSlots.Add(slot);
            try { SaveShellConfig(throwOnError: true); }
            catch { _modPersistentSlots.Remove(slot); throw; }
            _selectedModPersistentSlotId = slot.Id;
            _shortcutStatus.Text = T("ShortcutPanel.SinglePresetSaved", name, values.Count);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Overlay single Mod preset capture", ex);
            if (!_onlineWindowClosed) _shortcutStatus!.Text = T("ShortcutPanel.PresetNotSaved") + ex.Message;
        }
        finally
        {
            _savingOverlaySinglePreset = false;
            if (!_onlineWindowClosed)
            {
                SetCombinationBusy(false);
                RefreshPersistentSlotControls();
                RefreshShortcutOverlay();
            }
        }
    }
}
