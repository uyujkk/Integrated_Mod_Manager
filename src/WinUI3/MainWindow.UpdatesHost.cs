using System.ComponentModel;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The window's side of the Updates page (Pages/UpdatesPage.xaml): it writes the tracked Mods, statuses,
// rollback and backups into UpdatesViewModel, applies the settings changed there and answers the
// page's requests. The navigation badge follows the view model's update count.
public sealed partial class MainWindow
{
    private UpdatesViewModel UpdatesView => _services.Updates;

    private void InitializeUpdatesHost()
    {
        UpdatesView.ActionRequested += OnUpdatesActionRequested;
        UpdatesView.PropertyChanged += OnUpdatesViewChanged;
        _services.Appearance.ReduceAnimations = () => ShouldReduceAnimations;
        _services.Downloads.IsRetryBlocked = () => _onlineView._onlineDownloadActions.IsPreparing;
        // The navigation shows the running downloads; the Updates page lists the finished ones itself.
        _services.Downloads.Changed += (_, _) =>
        {
            RefreshNavTasks();
            if (NavTasksFlyout.IsOpen) FillNavTasksPanel();
        };
    }

    // Writes the window's state into the page's view model; the page shows it again.
    private void RefreshUpdatesPane()
    {
        RefreshCombinationProfileText();
        _isApplyingConflictDetection = true;
        UpdatesView.ConflictDetection = _enableConflictDetection;
        _isApplyingConflictDetection = false;
        RefreshBackupManagerText();
        _ = RefreshInstallBackupListAsync();
        RefreshConfigurationProfiles();
        RefreshDownloadTaskCenter();
        UpdatesView.IsCheckingModUpdates = _isCheckingModUpdates;
        UpdatesView.SetReplacing(_modUpdatesInProgress);

        if (_modUpdateStatus is null)
        {
            SetModUpdateStatus(M("Updates.TrackedModsNotCheckedYet"));
        }
        else
        {
            UpdatesView.ModUpdateStatus = T(_modUpdateStatus);
        }

        string lastCheckedText = _lastModUpdateCheckUtc.HasValue
            ? _lastModUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.NotCheckedYet");
        int trackedCount = _trackedModOrigins.Count;
        int updateCount = _trackedModUpdateResults.Count(item => item.HasUpdate);
        UpdatesView.TrackedSummary = T("Updates.TrackedModsLastCheckedUpdatesAvailable", trackedCount, lastCheckedText, updateCount);

        PopulateModUpdateIntervalOptions();
        UpdatesView.SetTrackedMods(BuildTrackedModRows());
        UpdatesView.NotifyRefreshed();
    }

    // The preset controls are shown by the Mod workspace's Presets view; their labels stay here until J7.
    private void RefreshCombinationProfileText()
    {
        _workspaceView.ConfigurationProfilesTitleTextBlock.Text = T("Updates.ModCombinationPresets");
        _workspaceView.ConfigurationProfilesHintTextBlock.Text = T("Updates.SaveEnabledModsAndPersistentState");
        _workspaceView.CaptureCombinationStateCheckBox.Content = T("Updates.IncludeD3dxUserIniParametersWhen");
        _workspaceView.BrowseCombinationUserIniButton.Content = T("Common.Browse");
        _workspaceView.CreateConfigurationProfileButton.Content = T("Updates.NewProfile");
        _workspaceView.UpdateConfigurationProfileButton.Content = T("Updates.UpdateProfile");
        _workspaceView.ApplyConfigurationProfileButton.Content = T("Updates.PreviewAndRestore");
        _workspaceView.DeleteConfigurationProfileButton.Content = new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "", FontSize = 16 };
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(_workspaceView.DeleteConfigurationProfileButton, T("Updates.DeleteTheSelectedProfile"));
    }

    // The check results, or the bare origins before any check.
    private List<TrackedModRow> BuildTrackedModRows()
    {
        if (_trackedModUpdateResults.Count > 0)
        {
            return [.. _trackedModUpdateResults.Select(result => Row(result,
                UpdatesListPolicy.Status(true, result.CheckFailed, result.HasUpdate)))];
        }
        return [.. _trackedModOrigins.Values.Select(origin => Row(new TrackedModUpdateResult
        {
            Path = origin.Path,
            Title = origin.Title,
            ProfileUrl = origin.ProfileUrl,
            PreviewUrl = origin.PreviewUrl,
            ItemId = origin.ItemId,
            LastKnownUpdatedAt = origin.LastKnownUpdatedAt,
            LatestUpdatedAt = origin.LastKnownUpdatedAt
        }, TrackedModStatus.NotChecked))];

        TrackedModRow Row(TrackedModUpdateResult result, TrackedModStatus status) => new(
            result.Title, result.Path, result.ProfileUrl, result.PreviewUrl, result.BackupPath,
            result.LastKnownUpdatedAt, result.LatestUpdatedAt, result.HasUpdate, status,
            result.Status is null ? null : T(result.Status), result);
    }

    private bool _isApplyingConflictDetection;
    private bool _restoringUpdatesView;

    private async void OnUpdatesViewChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            switch (e.PropertyName)
            {
                case nameof(UpdatesViewModel.AvailableUpdateCount):
                    RefreshUpdatesNavBadge();
                    break;
                case nameof(UpdatesViewModel.View):
                    if (!_restoringUpdatesView) SaveShellConfig();
                    break;
                case nameof(UpdatesViewModel.ModUpdateIntervalIndex):
                    if (_isApplyingModUpdateIntervalSelection) return;
                    _modUpdateCheckInterval = (UpdateCheckInterval)UpdatesView.ModUpdateIntervalIndex;
                    SaveShellConfig();
                    break;
                case nameof(UpdatesViewModel.ConflictDetection):
                    if (_isApplyingConflictDetection) return;
                    _enableConflictDetection = UpdatesView.ConflictDetection;
                    SaveShellConfig();
                    break;
                case nameof(UpdatesViewModel.BackupLimitGb):
                    if (_isApplyingBackupLimitSetting) return;
                    _installBackupLimitGb = UpdatesView.BackupLimitGb;
                    SaveShellConfig();
                    await Task.Run(() => PruneInstallTransactionsByStorageLimit(null));
                    await RefreshInstallBackupListAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnUpdatesViewChanged " + e.PropertyName, ex);
        }
    }

    private async void OnUpdatesActionRequested(object? sender, UpdatesActionRequest request)
    {
        var args = new RoutedEventArgs();
        switch (request.Action)
        {
            case UpdatesAction.CheckModUpdates: OnCheckModUpdatesClicked(this, args); break;
            case UpdatesAction.DownloadAndReplace when request.Item is TrackedModUpdateResult result:
                await RunUiActionAsync("Download and replace Mod", () => DownloadAndReplaceTrackedModAsync(result));
                break;
            case UpdatesAction.OpenModPage:
                await RunUiActionAsync("OpenExternalUrlAsync", () => OpenExternalUrlAsync(request.Item as string ?? string.Empty, T("Common.OpenPageFailed")));
                break;
            case UpdatesAction.OpenOldBackup: OpenDirectory(request.Item as string, T("Updates.OldBackup")); break;
            case UpdatesAction.RemoveTracking when request.Item is TrackedModUpdateResult result:
                await RunUiActionAsync("_onlineView.RemoveTrackedModRecordAsync", () => _onlineView.RemoveTrackedModRecordAsync(result));
                break;
            case UpdatesAction.BrowseOnline: OnOnlineNavClicked(this, args); break;
            case UpdatesAction.ClearFinishedDownloads: OnClearDownloadTasksClicked(this, args); break;
            case UpdatesAction.DownloadTaskAction when request.Item is DownloadTaskItem task: await RunDownloadTaskActionAsync(task); break;
            case UpdatesAction.RollbackLastInstall: OnRollbackLastInstallClicked(this, args); break;
            case UpdatesAction.OpenCombinationPresets: OnOpenCombinationPresetsClicked(this, args); break;
            case UpdatesAction.RefreshBackups: OnRefreshBackupsClicked(this, args); break;
            case UpdatesAction.RestoreBackup when request.Item is string path: await RestoreInstallBackupAsync(path); break;
        }
    }
}
