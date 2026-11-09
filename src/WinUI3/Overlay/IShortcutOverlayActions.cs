using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What the Shortcuts overlay asks the app to do (stage J5b). The overlay (ShortcutOverlayController) only shows
/// and lays out; everything that reaches the game, the loader files or the presets stays behind this interface,
/// implemented by the window, so moving the overlay changed none of it.
/// </summary>
internal interface IShortcutOverlayActions
{
    /// <summary>The app is closing; the overlay does nothing more.</summary>
    bool IsClosing { get; }

    /// <summary>A preset is being saved from the overlay; it must not be hidden or reopened meanwhile.</summary>
    bool IsSavingFromOverlay { get; }

    /// <summary>The global hotkey is registered, so click-through can be undone.</summary>
    bool HotkeyAvailable { get; }

    /// <summary>Reads the Mods and windows once, before the overlay first opens without the page.</summary>
    Task EnsureShortcutsReadAsync();

    /// <summary>Looks for the game window again.</summary>
    void RefreshTargets();

    /// <summary>Reads again which Mods are enabled in the game folder.</summary>
    Task RefreshDeployedModsAsync();

    bool IsModDeployed(ShortcutModOption option);

    Task SendShortcutAsync(DetectedModShortcut shortcut);

    /// <summary>Arms or pauses input, looking for the game window first.</summary>
    void SetInputEnabled(bool enable);

    Task SaveCombinationPresetAsync();

    Task SaveSingleModPresetAsync();

    /// <summary>Reads the Mod's INI and slots again (after F10).</summary>
    Task RefreshFromDiskAsync();

    /// <summary>The state slots of the overlay's Mod (none unless hot switching is on), and that Mod's folder.</summary>
    ModPersistentSlot[] OverlaySlots(out string? modPath);

    /// <summary>The slot whose values the game has now, if known.</summary>
    ModPersistentSlot? CurrentSlot(ModPersistentSlot[] slots);

    /// <summary>The Mod's folder relative to the repository, part of what makes the slot row change.</summary>
    string? ModRelativePath(string? modPath);

    /// <summary>Writes the slot's values and sends F10.</summary>
    Task SwitchSlotAsync(string slotId, string modPath);

    Task ShowSlotGalleryAsync();

    Task FrameCurrentSlotPictureAsync();

    Image? CreatePresetPreviewImage(string? previewFile, double width, double height);

    /// <summary>Runs an action the way the window runs clicks: errors are reported, never thrown.</summary>
    Task RunAsync(string operation, Func<Task> action);

    void ReportFailure(string operation, Exception error);

    /// <summary>Writes a problem to the log without telling the user (what the hotkey path always did).</summary>
    void LogIssue(string operation, Exception error);
}
