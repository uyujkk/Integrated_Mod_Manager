using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // Runs on the UI thread after Program redirected a second launch to this process.
    internal void RestoreFromExternalActivation()
    {
        if (_onlineWindowClosed)
        {
            return;
        }

        try
        {
            UiCrashDiagnostics.RecordAction("Second launch redirected");
            if (_trayWindowHandle == 0)
            {
                _trayWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            }

            bool hiddenOrMinimized = !IsWindowVisible(_trayWindowHandle)
                || AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
            if (hiddenOrMinimized)
            {
                // Same path as double-clicking the tray icon.
                RestoreFromTray();
                return;
            }

            // Already on screen (possibly maximized): only bring it forward, never resize it.
            Activate();
            BringWindowToTop(_trayWindowHandle);
            SetForegroundWindow(_trayWindowHandle);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Restore window after second launch", ex);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
}
