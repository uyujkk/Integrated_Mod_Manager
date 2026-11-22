using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The Shortcuts page and overlay are AppServices.ShortcutPanel since stage J finish, step 8b; the window opens the
// page and attaches the service with what only the window has.
public sealed partial class MainWindow
{
    private ShortcutsPage? _shortcutsPage;

    private void OnShortcutsNavClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _shell.CurrentSection = PrimarySection.Shortcuts;
            ApplyShellState(refreshRepository: false);
        }
        catch (Exception ex) { ReportUiActionFailure(nameof(OnShortcutsNavClicked), ex); }
    }

    private void AttachShortcutServices()
    {
        _services.ShortcutBindings.Attach(LogApplicationIssue, SaveConfig);
        _services.ShortcutPanel.Attach(WinRT.Interop.WindowNative.GetWindowHandle(this), DispatcherQueue, Activate, Close,
            () => OnDashboardNavClicked(this, new RoutedEventArgs()), () => _services.Workspace.CurrentModPath, RefreshPersistentSlotControls,
            () => _shortcutsPage, LogApplicationIssue, ReportUiActionFailure, RunUiActionAsync);
        // Stage J finish, step 6: slots and pictures are services; they reach the overlay through the shortcut service.
        _services.Slots.Attach(LogApplicationIssue, TraceStartupStage, () => SaveShellConfig(), RefreshPersistentSlotControls,
            text => _services.Workspace.Status = text, _services.ShortcutPanel.Input, () => _services.ShortcutPanel.OverlayController,
            () => _services.ShortcutPanel.SavingFromOverlay);
        _services.PresetPreviews.Attach(LogApplicationIssue, () => SaveShellConfig(), text => _services.Workspace.Status = text,
            Activate, RunUiActionAsync, () => _services.ShortcutPanel.OverlayController);
    }
}
