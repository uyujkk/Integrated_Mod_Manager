using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public partial class App : Application
{
    private Window? _window;

    // Read from the AppInstance.Activated callback, which runs off the UI thread.
    internal static App? Instance { get; private set; }

    public App()
    {
        Instance = this;
        UiCrashDiagnostics.RecordAction("Application starting");
        UnhandledException += (_, args) =>
        {
            UiCrashDiagnostics.RecordAction("Unhandled UI exception");
            // Keep local diagnostics without suppressing an unrecoverable UI failure.
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "startup.log");
                if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 1024 * 1024)
                    System.IO.File.Move(path, path + ".previous", overwrite: true);
                System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O} Unhandled UI exception: {args.Message}\n{args.Exception}\n");
            }
            catch { /* Diagnostics must not replace the original failure. */ }
        };
        // Fire-and-forget tasks ("_ = SomethingAsync()") lose their exceptions silently;
        // record them so failures that never reach a handler still leave evidence.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            MainWindow.LogApplicationIssueWithStack("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        InitializeComponent();
        if (UiCrashDiagnostics.Enabled)
        {
            DebugSettings.LayoutCycleTracingLevel = LayoutCycleTracingLevel.High;
            UiCrashDiagnostics.Write("Diagnostics enabled", "LayoutCycleTracingLevel=High");
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                UiCrashDiagnostics.Write("Unhandled managed exception", args.ExceptionObject.ToString() ?? string.Empty);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    // A second launch from the same install directory was redirected here.
    internal void OnExternalActivation()
    {
        if (_window is MainWindow mainWindow)
        {
            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.RestoreFromExternalActivation);
        }
    }
}

