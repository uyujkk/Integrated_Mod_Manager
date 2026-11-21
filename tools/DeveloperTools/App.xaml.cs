using IntegratedModManager.DeveloperTools.Services;
using Microsoft.UI.Xaml;

namespace IntegratedModManager.DeveloperTools;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // --root <folder> --page <name> --language <code> --theme light|dark --mod <folder> (for scripts and screenshots; not saved).
        var options = ToolOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
        _window = new MainWindow(new ToolContext(options));
        _window.Activate();
    }
}
