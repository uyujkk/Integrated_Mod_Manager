using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Stage J5: the window's side of the Shortcuts page and overlay. The page (Pages/ShortcutsPage) shows
// ShortcutPageViewModel and asks for work through it; the overlay (Overlay/ShortcutOverlayController) shows the
// same view model and asks through IShortcutOverlayActions, implemented here with the window's existing code, so
// sending keys, F10, slot switching, captures and preset saves did not change when the overlay moved out (J5b).
public sealed partial class MainWindow : IShortcutOverlayActions
{
    private void AttachShortcutServices()
    {
        _overlay = new ShortcutOverlayController(_shortcuts, _services.Language, this, DispatcherQueue);
        _services.Overlay = _overlay;
        _shortcuts.ActionRequested += OnShortcutPageRequested;
    }

    // After the settings are loaded, so loading them is not taken for the user's changes.
    private void ListenToShortcutState() => _shortcuts.PropertyChanged += OnShortcutStateChanged;

    bool IShortcutOverlayActions.IsClosing => _services.IsClosing;

    bool IShortcutOverlayActions.IsSavingFromOverlay => _savingOverlaySinglePreset;

    bool IShortcutOverlayActions.HotkeyAvailable => _shortcutPanelHotkey is not null;

    async Task IShortcutOverlayActions.EnsureShortcutsReadAsync()
    {
        if (_shortcutsPrepared) return;
        PrepareShortcuts();
        await RefreshShortcutModsAsync();
    }

    void IShortcutOverlayActions.RefreshTargets() => RefreshShortcutTargets();

    Task IShortcutOverlayActions.RefreshDeployedModsAsync() => RefreshShortcutDeployedModsAsync();

    bool IShortcutOverlayActions.IsModDeployed(ShortcutModOption option) => IsShortcutModDeployed(option);

    Task IShortcutOverlayActions.SendShortcutAsync(DetectedModShortcut shortcut) => SendShortcutFromPanelAsync(shortcut);

    void IShortcutOverlayActions.SetInputEnabled(bool enable) => SetShortcutInputFromOverlay(enable);

    Task IShortcutOverlayActions.SaveCombinationPresetAsync() => QuickSaveCombinationFromOverlayAsync();

    Task IShortcutOverlayActions.SaveSingleModPresetAsync() => SaveSingleModPresetFromOverlayAsync();

    Task IShortcutOverlayActions.RefreshFromDiskAsync() => RefreshOverlayFromDiskAsync();

    ModPersistentSlot[] IShortcutOverlayActions.OverlaySlots(out string? modPath) => OverlaySlots(out modPath);

    ModPersistentSlot? IShortcutOverlayActions.CurrentSlot(ModPersistentSlot[] slots) => CurrentPersistentSlot(slots);

    string? IShortcutOverlayActions.ModRelativePath(string? modPath) => TryGetCurrentModRelativePath(GetSelectedRepository(), modPath);

    Task IShortcutOverlayActions.SwitchSlotAsync(string slotId, string modPath) => SwitchSlotFromOverlayAsync(slotId, modPath);

    Task IShortcutOverlayActions.ShowSlotGalleryAsync() => ShowOverlaySlotGalleryAsync();

    Task IShortcutOverlayActions.FrameCurrentSlotPictureAsync() => FrameCurrentSlotPreviewAsync();

    Image? IShortcutOverlayActions.CreatePresetPreviewImage(string? previewFile, double width, double height) => CreatePresetPreviewImage(previewFile, width, height);

    Task IShortcutOverlayActions.RunAsync(string operation, Func<Task> action) => RunUiActionAsync(operation, action);

    void IShortcutOverlayActions.ReportFailure(string operation, Exception error) => ReportUiActionFailure(operation, error);

    void IShortcutOverlayActions.LogIssue(string operation, Exception error) => LogApplicationIssue(operation, error);
}
