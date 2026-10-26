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
        if (!canSend)
        {
            var overlay = _shortcutOverlay;
            if (overlay?.Content is not FrameworkElement root) return false;
            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark,
                Title = T("ShortcutCapture.CannotSend"), Content = T("ShortcutCapture.ReadExistingWarning"),
                PrimaryButtonText = T("ShortcutCapture.ReadExisting"), CloseButtonText = T("Common.Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            overlay.SetKeyboardInputEnabled(true);
            try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
            finally
            {
                if (ReferenceEquals(_shortcutOverlay, overlay)) overlay.SetKeyboardInputEnabled(false);
            }
        }
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
        finally { _shortcutSendInProgress = false; }
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
