using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private async Task<bool> WaitForOverlayCaptureAsync()
    {
        // A hot switch owns the file until its read-only hold finishes. Queue a save behind it.
        while (_restoringPersistentRuntime && !_onlineWindowClosed) await Task.Delay(50);
        return !_onlineWindowClosed && !_applyingCombinationProfile;
    }

    private async Task<bool> PrepareOverlayCaptureAsync(string userIni)
    {
        bool canSend = CanHotReloadLoader;
        if (_shortcutTargetPicker?.SelectedItem is ShortcutTarget target)
        {
            var own = ShortcutProcessSecurity.Read((uint)Environment.ProcessId);
            var destination = ShortcutProcessSecurity.Read(target.ProcessId);
            canSend &= ShortcutPrivilegePolicy.Evaluate(own.Integrity, own.UiAccess, destination.Integrity, destination.Error)
                == ShortcutPrivilegeVerdict.Allowed;
        }
        if (!canSend) return await AskToReadExistingFileAsync(T("ShortcutCapture.CannotSend"), T("ShortcutCapture.ReadExistingWarning"));
        _shortcutStatus!.Text = T("ShortcutCapture.Waiting");
        RefreshShortcutOverlay();
        _shortcutSendInProgress = true;
        try
        {
            var chosen = (ShortcutTarget)_shortcutTargetPicker!.SelectedItem;
            await LoaderStateCapture.FlushAsync(userIni,
                _ => _shortcutInput.SendAsync(chosen, new ShortcutKeyPlan([LoaderReloadKey], []), 120, _shortcutInputMode, IsEnglishUi));
            return !_onlineWindowClosed;
        }
        catch (TimeoutException)
        {
            // The loader writes nothing when no value changed since its last save (common for a new Mod),
            // which looks the same as a missed F10. Let the user decide instead of refusing outright.
        }
        finally { _shortcutSendInProgress = false; }
        bool readExisting = !_onlineWindowClosed
            && await AskToReadExistingFileAsync(T("ShortcutCapture.NoNewWrite"), T("ShortcutCapture.NoNewWriteWarning"));
        if (!readExisting && !_onlineWindowClosed)
        {
            _shortcutStatus.Text = T("ShortcutPanel.PresetNotSaved") + T("ShortcutCapture.TimedOut");
            RefreshShortcutOverlay();
        }
        return readExisting;
    }

    // Asks in the overlay whether to save from the parameter file as it is on disk.
    private async Task<bool> AskToReadExistingFileAsync(string title, string message)
    {
        var overlay = _shortcutOverlay;
        if (overlay?.Content is not FrameworkElement root) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark,
            Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = T("ShortcutCapture.ReadExisting"), CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        overlay.SetKeyboardInputEnabled(true);
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && !_onlineWindowClosed; }
        finally
        {
            if (ReferenceEquals(_shortcutOverlay, overlay)) overlay.SetKeyboardInputEnabled(false);
            if (_shortcutTargetPicker?.SelectedItem is ShortcutTarget target) ShortcutInputService.FocusTarget(target);
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
        if (_applyingCombinationProfile || _restoringPersistentRuntime || _shortcutSendInProgress) return;
        await RefreshShortcutDeployedModsAsync();
        await LoadShortcutSelectionAsync(force: true);
        if (_shortcutModPicker?.SelectedItem is null) _shortcutStatus!.Text = T("ShortcutCapture.CurrentRemoved");
        RefreshPersistentSlotControls();
        RefreshShortcutOverlay(bindingsChanged: true);
    }
}
