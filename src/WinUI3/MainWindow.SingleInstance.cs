namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // Runs on the UI thread after Program redirected a second launch to this process.
    internal void RestoreFromExternalActivation()
    {
        if (_services.IsClosing)
        {
            return;
        }

        _services.Tray.RestoreFromExternalActivation();
    }
}
