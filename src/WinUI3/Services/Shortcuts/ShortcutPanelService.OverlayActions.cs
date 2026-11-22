using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// The overlay's requests (IShortcutOverlayActions, stage J5b), answered with the code that answered them in the window.
internal sealed partial class ShortcutPanelService : IShortcutOverlayActions
{
    bool IShortcutOverlayActions.IsClosing => _services.IsClosing;

    bool IShortcutOverlayActions.IsSavingFromOverlay => _savingOverlaySinglePreset;

    bool IShortcutOverlayActions.HotkeyAvailable => _shortcutPanelHotkey is not null;

    async Task IShortcutOverlayActions.EnsureShortcutsReadAsync()
    {
        if (_shortcutsPrepared) return;
        Prepare();
        await RefreshShortcutModsAsync();
    }

    void IShortcutOverlayActions.RefreshTargets() => RefreshShortcutTargets();

    Task IShortcutOverlayActions.RefreshDeployedModsAsync() => RefreshDeployedModsAsync();

    bool IShortcutOverlayActions.IsModDeployed(ShortcutModOption option) => IsShortcutModDeployed(option);

    Task IShortcutOverlayActions.SendShortcutAsync(DetectedModShortcut shortcut) => SendShortcutFromPanelAsync(shortcut);

    void IShortcutOverlayActions.SetInputEnabled(bool enable) => SetShortcutInputFromOverlay(enable);

    Task IShortcutOverlayActions.SaveCombinationPresetAsync() => QuickSaveCombinationFromOverlayAsync();

    Task IShortcutOverlayActions.SaveSingleModPresetAsync() => SaveSingleModPresetFromOverlayAsync();

    Task IShortcutOverlayActions.RefreshFromDiskAsync() => RefreshOverlayFromDiskAsync();

    ModPersistentSlot[] IShortcutOverlayActions.OverlaySlots(out string? modPath) => _services.Slots.OverlaySlots(out modPath);

    ModPersistentSlot? IShortcutOverlayActions.CurrentSlot(ModPersistentSlot[] slots) => SlotService.CurrentPersistentSlot(slots);

    string? IShortcutOverlayActions.ModRelativePath(string? modPath) => SlotService.TryGetCurrentModRelativePath(_services.Repositories.Selected, modPath);

    Task IShortcutOverlayActions.SwitchSlotAsync(string slotId, string modPath) => _services.Slots.SwitchSlotFromOverlayAsync(slotId, modPath);

    Task IShortcutOverlayActions.ShowSlotGalleryAsync() => ShowOverlaySlotGalleryAsync();

    Task IShortcutOverlayActions.FrameCurrentSlotPictureAsync() => FrameCurrentSlotPreviewAsync();

    Image? IShortcutOverlayActions.CreatePresetPreviewImage(string? previewFile, double width, double height) => _services.PresetPreviews.CreatePresetPreviewImage(previewFile, width, height);

    Task IShortcutOverlayActions.RunAsync(string operation, Func<Task> action) => _runUiAction(operation, action);

    void IShortcutOverlayActions.ReportFailure(string operation, Exception error) => _reportFailure(operation, error);

    void IShortcutOverlayActions.LogIssue(string operation, Exception error) => _log(operation, error);
}
