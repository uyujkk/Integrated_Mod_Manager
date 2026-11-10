using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private async Task<bool> WaitForOverlayCaptureAsync()
    {
        // A hot switch owns the file until its read-only hold finishes. Queue a save behind it.
        while (_restoringPersistentRuntime && !_services.IsClosing) await Task.Delay(50);
        return !_services.IsClosing && !_applyingCombinationProfile;
    }

    private async Task<bool> PrepareOverlayCaptureAsync(string userIni)
    {
        bool canSend = CanHotReloadLoader;
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
                _ => _shortcutInput.SendAsync(chosen, new ShortcutKeyPlan([LoaderReloadKey], []), 120, _shortcuts.InputMode, _services.Language.Current));
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
            _shortcuts.StatusText = T("ShortcutPanel.PresetNotSaved") + T("ShortcutCapture.TimedOut");
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
        if (_applyingCombinationProfile || _restoringPersistentRuntime || _shortcuts.SendInProgress) return;
        await RefreshShortcutDeployedModsAsync();
        await LoadShortcutSelectionAsync(force: true);
        if (_shortcuts.SelectedMod is null) _shortcuts.StatusText = T("ShortcutCapture.CurrentRemoved");
        RefreshPersistentSlotControls();
        RefreshShortcutOverlay(bindingsChanged: true);
    }
}
