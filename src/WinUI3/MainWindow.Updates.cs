using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The app's own updates are AppServices.AppUpdates since stage J finish, step 8c; the window keeps the interval
// pickers and the tracked Mods' check button.
public sealed partial class MainWindow
{
    private void SetModUpdateStatus(UiMessage status)
    {
        _modUpdateStatus = status;
        UpdatesView.ModUpdateStatus = T(status);
    }

    private void PopulateUpdateCheckIntervalOptions()
    {
        _isApplyingUpdateCheckIntervalSelection = true;
        try
        {
            SettingsView.UpdateIntervalIndex = (int)_shell.UpdateCheckInterval;
        }
        finally
        {
            _isApplyingUpdateCheckIntervalSelection = false;
        }
    }

    private void PopulateModUpdateIntervalOptions()
    {
        _isApplyingModUpdateIntervalSelection = true;
        try
        {
            UpdatesView.ModUpdateIntervalIndex = (int)_shell.ModUpdateCheckInterval;
        }
        finally
        {
            _isApplyingModUpdateIntervalSelection = false;
        }
    }

    private async void OnCheckModUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _services.Tracking.CheckTrackedModUpdatesAsync(showDialogs: true);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnCheckModUpdatesClicked", ex);
        }
    }
}
