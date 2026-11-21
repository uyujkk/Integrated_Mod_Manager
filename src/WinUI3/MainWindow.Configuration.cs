using IntegratedModManager.Core.Layout;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // beta-shell.json and config.ini are ConfigurationService's (stage J finish, step 8a); the window keeps its place on
    // the screen, which only it can read and set.
    private ShellSettings _shell => _services.ShellSettings;

    private void LoadShellConfig() => _services.Configuration.LoadShell();

    private void SaveShellConfig(bool throwOnError = false) => _services.Configuration.SaveShell(throwOnError);

    private bool SaveConfig() => _services.Configuration.Save();

    private void WriteLegacyConfig() => _services.Configuration.WriteLegacyConfig();

    // config.ini, then the workspace page's defaults when it exists.
    private void LoadConfig()
    {
        _services.Configuration.LoadLegacy();
        _workspaceView?.LoadDefaultShortcutTemplate();
        _workspaceView?.LoadLinkForCurrentMod(null);
    }


    // Normal bounds come from WindowPlacementPolicy: saved bounds that fill the screen (for example
    // saved while maximized by earlier versions) or no bounds become a share of the work area.
    private void RestoreWindowPlacement()
    {
        WindowRect? saved = _shell.WindowWidth is int width && _shell.WindowHeight is int height
            ? new WindowRect(_shell.WindowX ?? AppWindow.Position.X, _shell.WindowY ?? AppWindow.Position.Y, width, height)
            : null;
        RectInt32 workArea = DisplayArea.GetFromPoint(
            new PointInt32(saved?.X ?? AppWindow.Position.X, saved?.Y ?? AppWindow.Position.Y), DisplayAreaFallback.Primary).WorkArea;
        WindowRect normal = WindowPlacementPolicy.Normal(saved, new WindowRect(workArea.X, workArea.Y, workArea.Width, workArea.Height));
        AppWindow.MoveAndResize(new RectInt32(normal.X, normal.Y, normal.Width, normal.Height));
        if (_shell.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
        _lastPresenterState = (AppWindow.Presenter as OverlappedPresenter)?.State;
        AppWindow.Changed -= OnWindowPlacementChanged;
        AppWindow.Changed += OnWindowPlacementChanged;
    }

    // Keep the last normal bounds: a maximized or minimized window only records its state.
    private void SaveWindowPlacement()
    {
        OverlappedPresenterState? state = (AppWindow.Presenter as OverlappedPresenter)?.State;
        if (state == OverlappedPresenterState.Minimized)
        {
            return;
        }

        _shell.WindowMaximized = state == OverlappedPresenterState.Maximized;
        if (_shell.WindowMaximized)
        {
            return;
        }

        _shell.WindowX = AppWindow.Position.X;
        _shell.WindowY = AppWindow.Position.Y;
        _shell.WindowWidth = AppWindow.Size.Width;
        _shell.WindowHeight = AppWindow.Size.Height;
    }

    // Leaving maximized for a normal window that still fills the screen shrinks it to the share of
    // the work area, so restoring visibly changes the window.
    private void OnWindowPlacementChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        try
        {
            if (!args.DidPresenterChange && !args.DidSizeChange) return;
            OverlappedPresenterState? state = (sender.Presenter as OverlappedPresenter)?.State;
            OverlappedPresenterState? previous = _lastPresenterState;
            _lastPresenterState = state;
            if (previous != OverlappedPresenterState.Maximized || state != OverlappedPresenterState.Restored) return;

            RectInt32 workArea = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Primary).WorkArea;
            var work = new WindowRect(workArea.X, workArea.Y, workArea.Width, workArea.Height);
            var window = new WindowRect(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
            if (WindowPlacementPolicy.FillsWorkArea(window, work))
            {
                WindowRect normal = WindowPlacementPolicy.Proportional(work);
                sender.MoveAndResize(new RectInt32(normal.X, normal.Y, normal.Width, normal.Height));
            }
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Window placement", ex);
        }
    }

}
