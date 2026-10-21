using System.Runtime.InteropServices;
using IntegratedModManager.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ModFolderCopier.WinUI;

// Replaces the XAML-generated entry point (DISABLE_XAML_GENERATED_MAIN in the project)
// so a second launch from the same install directory activates the running window
// instead of starting another process that would write the same config, caches,
// SQLite index and download journal concurrently.
public static class Program
{
    private const uint CoWaitDefault = 0;
    private const uint RedirectTimeoutMilliseconds = 10_000;

    [STAThread]
    private static int Main(string[] args)
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToRunningInstance())
        {
            return 0;
        }

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    private static bool TryRedirectToRunningInstance()
    {
        AppInstance keyInstance;
        AppActivationArguments activation;
        try
        {
            activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            keyInstance = AppInstance.FindOrRegisterForKey(
                SingleInstancePolicy.CreateInstanceKey(AppContext.BaseDirectory));
        }
        catch (Exception ex)
        {
            // Never block startup because instance registration is unavailable.
            WriteStartupNote("Single-instance registration unavailable", ex);
            return false;
        }

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += (_, _) => App.Instance?.OnExternalActivation();
            return false;
        }

        try
        {
            // This process was started by the user, so it may hand foreground rights to
            // the running instance; otherwise Windows only flashes its taskbar button.
            AllowSetForegroundWindow(keyInstance.ProcessId);
            // The handle is intentionally not closed: after a timeout the worker may still
            // signal it, and this process exits immediately afterwards anyway.
            nint redirected = CreateEvent(0, bManualReset: true, bInitialState: false, null);
            if (redirected == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            _ = Task.Run(() =>
            {
                try
                {
                    keyInstance.RedirectActivationToAsync(activation).AsTask().Wait();
                }
                catch (Exception ex)
                {
                    WriteStartupNote("Redirect to running instance failed", ex);
                }
                finally
                {
                    SetEvent(redirected);
                }
            });
            // The STA thread must keep pumping COM while it waits for the redirect.
            _ = CoWaitForMultipleObjects(CoWaitDefault, RedirectTimeoutMilliseconds, 1, [redirected], out _);
        }
        catch (Exception ex)
        {
            WriteStartupNote("Redirect to running instance failed", ex);
        }

        // Even when the redirect could not be confirmed, another instance owns this
        // install directory: starting a second writer is the outcome to avoid.
        return true;
    }

    private static void WriteStartupNote(string operation, Exception exception)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "startup.log");
            File.AppendAllText(path,
                $"{DateTimeOffset.Now:O} {operation}: {exception.GetType().Name} (0x{exception.HResult:X8}) {exception.Message}{Environment.NewLine}");
        }
        catch
        {
            // Startup diagnostics must not change startup behaviour.
        }
    }

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateEvent(nint lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, nint[] pHandles, out uint dwIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
