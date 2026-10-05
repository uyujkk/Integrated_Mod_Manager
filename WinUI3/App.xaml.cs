using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        UnhandledException += (_, args) =>
        {
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
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}

