namespace ModFolderCopier.WinUI;

// The tray icon is AppServices.Tray since stage J finish, step 8c; the window shows its setting on the Settings page.
public sealed partial class MainWindow
{
    private bool _isApplyingTraySetting;

    private void RefreshTraySetting()
    {
        _isApplyingTraySetting = true;
        SettingsView.MinimizeToTray = _shell.MinimizeToTray;
        _isApplyingTraySetting = false;
    }

    private void InitializeTraySupport()
    {
        _services.Tray.Attach(WinRT.Interop.WindowNative.GetWindowHandle(this), AppWindow, DispatcherQueue, Activate, Close,
            GetKnownRepositorySummary, LogApplicationIssue);
        _services.Tray.Initialize();
    }
}
