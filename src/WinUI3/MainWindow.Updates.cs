using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static UpdateCheckInterval ParseUpdateCheckInterval(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out UpdateCheckInterval interval)
            ? interval
            : UpdateCheckInterval.Manual;
    }

    private TimeSpan? GetUpdateCheckIntervalTimeSpan()
    {
        return _updateCheckInterval switch
        {
            UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
            UpdateCheckInterval.EveryThreeDays => TimeSpan.FromDays(3),
            UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
            _ => null
        };
    }

    private bool ShouldAutoCheckForUpdates()
    {
        TimeSpan? interval = GetUpdateCheckIntervalTimeSpan();
        if (interval is null)
        {
            return false;
        }

        return !_lastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastUpdateCheckUtc.Value >= interval.Value;
    }

    private async Task CheckForUpdatesIfDueAsync()
    {
        bool startupCheckDue = AppSelfUpdateEnabled && _checkAppUpdatesOnStartup
            && (ShouldAutoCheckForUpdates()
                || _updateCheckInterval == UpdateCheckInterval.Manual
                    && (!_lastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastUpdateCheckUtc.Value >= TimeSpan.FromMinutes(30)));
        if (startupCheckDue)
        {
            await CheckForUpdatesAsync(showDialogs: false, promptIfAvailable: true);
        }

        if (ShouldAutoCheckForModUpdates())
        {
            await _services.Tracking.CheckTrackedModUpdatesAsync(showDialogs: false);
        }
    }

    private TimeSpan? GetModUpdateCheckIntervalTimeSpan()
    {
        return _modUpdateCheckInterval switch
        {
            UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
            UpdateCheckInterval.EveryThreeDays => TimeSpan.FromDays(3),
            UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
            _ => null
        };
    }

    private bool ShouldAutoCheckForModUpdates()
    {
        TimeSpan? interval = GetModUpdateCheckIntervalTimeSpan();
        if (interval is null)
        {
            return false;
        }

        return !_services.Tracking.LastCheckUtc.HasValue || DateTimeOffset.UtcNow - _services.Tracking.LastCheckUtc.Value >= interval.Value;
    }

    private void SetUpdateStatus(UiMessage status)
    {
        _updateStatus = status;
        SettingsView.UpdateStatus = T(status);
    }

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
            SettingsView.UpdateIntervalIndex = (int)_updateCheckInterval;
        }
        finally
        {
            _isApplyingUpdateCheckIntervalSelection = false;
        }
    }

    private void RefreshUpdateDetailsView()
    {
        if (!AppSelfUpdateEnabled)
        {
            SettingsView.ReleaseTitle = T("Updates.StandaloneBeta", AppVersion);
            SettingsView.ReleaseMeta = T("Updates.ManualDownloadExtractSeparatelyFromThe");
            SettingsView.ReleaseNotes = T("Updates.ModUpdateTestBuildFixesInterval");
            return;
        }
        string latestTag = string.IsNullOrWhiteSpace(_latestReleaseTag) ? T("Updates.NotCheckedYet") : _latestReleaseTag!;
        string latestTitle = string.IsNullOrWhiteSpace(_latestReleaseTitle)
            ? T("Updates.LatestReleaseDetailsWillAppearHere")
            : NormalizeUiText(_latestReleaseTitle);

        string publishedText = TryParseDateTimeOffset(_latestReleasePublishedAt) is DateTimeOffset publishedAt
            ? publishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.UnknownTime");

        string lastCheckedText = _lastUpdateCheckUtc.HasValue
            ? _lastUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.NotCheckedYet");

        SettingsView.ReleaseTitle = T("Updates.LatestRelease", latestTag);
        SettingsView.ReleaseMeta = T("Updates.TitlePublishedLastChecked", latestTitle, publishedText, lastCheckedText);
        SettingsView.ReleaseNotes = string.IsNullOrWhiteSpace(_latestReleaseBody)
            ? T("Updates.GitHubReleaseNotesWillAppearHere")
            : NormalizeUiText(_latestReleaseBody);
        // Before the first check there is nothing but placeholders, so the box stays one line.
        SettingsView.HasReleaseDetails = !string.IsNullOrWhiteSpace(_latestReleaseTag);
    }

    private void PopulateModUpdateIntervalOptions()
    {
        _isApplyingModUpdateIntervalSelection = true;
        try
        {
            UpdatesView.ModUpdateIntervalIndex = (int)_modUpdateCheckInterval;
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

    private async void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckForUpdatesAsync(showDialogs: true);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnCheckUpdatesClicked", ex);
        }
    }

    private async void OnInstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!AppSelfUpdateEnabled)
            {
                await OpenExternalUrlAsync(ApplicationReleasePolicy.ReleasePageUrl, T("Updates.FailedToOpenThePublicRelease"));
                return;
            }
            DetectLocalUpdatePackage();
            if (_localUpdatePackageVersion is not null && !string.IsNullOrWhiteSpace(_localUpdatePackagePath))
            {
                await PromptAndStartLocalUpdateAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(_latestReleaseUrl))
            {
                await CheckForUpdatesAsync(showDialogs: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_latestReleasePackageUrl) && IsReleaseNewer(_latestReleaseTag ?? string.Empty))
            {
                await DownloadAndInstallApplicationUpdateAsync(new LatestReleaseInfo(
                    _latestReleaseTag ?? string.Empty,
                    _latestReleaseTitle ?? string.Empty,
                    _latestReleaseBody ?? string.Empty,
                    _latestReleasePublishedAt ?? string.Empty,
                    _latestReleaseUrl,
                    _latestReleasePackageUrl,
                    _latestReleaseSha256Url));
                return;
            }

            await OpenExternalUrlAsync(_latestReleaseUrl, T("Common.FailedToOpenTheUpdatePage"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnInstallUpdateClicked", ex);
        }
    }

    private void DetectLocalUpdatePackage()
    {
        _localUpdatePackagePath = null;
        _localUpdatePackageVersion = null;

        if (!AppSelfUpdateEnabled)
        {
            return;
        }

        string installRoot = GetInstallRootPath();
        if (!Directory.Exists(installRoot) || !TryParseVersion(AppVersion, out Version currentVersion))
        {
            return;
        }

        foreach (string packagePath in Directory.EnumerateFiles(installRoot, "*.zip", SearchOption.TopDirectoryOnly))
        {
            Match match = ApplicationUpdateClient.PackageNamePattern.Match(Path.GetFileName(packagePath));
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out Version? packageVersion))
            {
                continue;
            }

            if (packageVersion <= currentVersion || (_localUpdatePackageVersion is not null && packageVersion <= _localUpdatePackageVersion))
            {
                continue;
            }

            _localUpdatePackagePath = packagePath;
            _localUpdatePackageVersion = packageVersion;
        }

        if (_localUpdatePackageVersion is not null)
        {
            SetUpdateStatus(M("Updates.LocalPackageDetected", _localUpdatePackageVersion));
        }

        RefreshAppUpdateInstallState();
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        return Version.TryParse(value.Trim().TrimStart('v', 'V'), out version!);
    }

    private string GetInstallRootPath()
    {
        var runtimeDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return runtimeDirectory.Name.Equals("WinUI3", StringComparison.OrdinalIgnoreCase) && runtimeDirectory.Parent is not null
            ? runtimeDirectory.Parent.FullName
            : runtimeDirectory.FullName;
    }

    private async Task PromptAndStartLocalUpdateAsync()
    {
        if (!AppSelfUpdateEnabled) { return; }
        if (_localUpdatePackageVersion is null || string.IsNullOrWhiteSpace(_localUpdatePackagePath))
        {
            return;
        }

        _hasPromptedForLocalUpdate = true;
        bool confirmed = await ShowConfirmAsync(
            T("Updates.LocalUpdateVWasDetectedThe", _localUpdatePackageVersion),
            T("Updates.InstallLocalUpdate"), T("Dialogs.Install"));

        if (!confirmed)
        {
            return;
        }

        await StartLocalUpdateAsync(_localUpdatePackagePath);
    }

    private async Task StartLocalUpdateAsync(string packagePath)
    {
        if (!AppSelfUpdateEnabled) { return; }
        if (IntegratedModManager.EndfieldLaunch.LaunchCommand.IsLaunchRunning())
        {
            // The window-less launch process runs this executable until the game exits.
            await ShowMessageAsync(
                T("Updates.AnEndfieldLaunchSessionIsStill"),
                T("Updates.UpdateUnavailable"));
            return;
        }

        try
        {
            string installRoot = GetInstallRootPath();
            string updaterSource = Path.Combine(installRoot, "LocalUpdateAgent.exe");
            string launcherPath = Path.Combine(installRoot, "IntegratedModManager.exe");
            if (!File.Exists(launcherPath))
            {
                launcherPath = Path.Combine(installRoot, "ModFolderCopier.exe");
            }

            if (!File.Exists(updaterSource))
            {
                await ShowMessageAsync(
                    T("Updates.LocalUpdateAgentExeIsMissingDownloadThe"),
                    T("Updates.UnableToUpdate"));
                return;
            }

            SaveConfig();
            SaveShellConfig();

            string updaterCopy = Path.Combine(Path.GetTempPath(), $"IntegratedModManagerUpdateAgent-{Guid.NewGuid():N}.exe");
            File.Copy(updaterSource, updaterCopy, overwrite: true);

            string arguments = string.Join(" ",
                "--package", QuoteCommandLineArgument(packagePath),
                "--install-root", QuoteCommandLineArgument(installRoot),
                "--process-id", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                "--launcher", QuoteCommandLineArgument(launcherPath),
                "--expected-version", _localUpdatePackageVersion?.ToString() ?? string.Empty,
                "--language", !IsChineseUi ? "en" : "zh");

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterCopy,
                Arguments = arguments,
                WorkingDirectory = installRoot,
                UseShellExecute = false
            });

            Close();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(
                T("Updates.FailedToStartTheLocalUpdate") + ex.Message,
                T("Common.UpdateFailed"));
        }
    }

    private static string QuoteCommandLineArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private async Task CheckForUpdatesAsync(bool showDialogs, bool promptIfAvailable = false)
    {
        if (!AppSelfUpdateEnabled) { return; }
        if (_isCheckingUpdates)
        {
            return;
        }

        try
        {
            _isCheckingUpdates = true;
            SettingsView.IsCheckingUpdates = true;
            RefreshSettingsPane();
            SetUpdateStatus(M("Updates.CheckingGitHub"));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            LatestReleaseInfo release = await FetchLatestReleaseInfoAsync(timeout.Token);
            ApplyLatestReleaseInfo(release);

            if (!TryParseVersion(release.Tag, out _))
            {
                SetUpdateStatus(M("Updates.NoValidVersion"));
            }
            else if (!IsReleaseNewer(release.Tag))
            {
                SetUpdateStatus(M("Updates.AlreadyLatest", AppVersion));
                if (showDialogs)
                {
                    ShowAppNotification(T("Updates.AppUpToDate"), InfoBarSeverity.Success);
                }
            }
            else
            {
                SetUpdateStatus(M("Updates.NewVersionDetected", release.Tag, AppVersion));
                if (showDialogs || promptIfAvailable)
                {
                    bool install = await ShowConfirmAsync(
                        T("Updates.VersionIsAvailableDownloadInstallAnd", release.Tag),
                        T("Updates.AppUpdateAvailable"), T("Dialogs.Update"));
                    if (install)
                    {
                        await DownloadAndInstallApplicationUpdateAsync(release);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetUpdateStatus(M("Updates.AppCheckFailed"));
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.FailedToCheckUpdatesFromGitHub") + ex.Message,
                    T("Updates.CheckFailed"));
            }
        }
        finally
        {
            _isCheckingUpdates = false;
            SettingsView.IsCheckingUpdates = false;
            RefreshSettingsPane();
        }
    }
}
