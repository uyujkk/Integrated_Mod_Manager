using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{

    private TimeSpan? GetUpdateCheckIntervalTimeSpan()
    {
        return _shell.UpdateCheckInterval switch
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

        return !_shell.LastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _shell.LastUpdateCheckUtc.Value >= interval.Value;
    }

    private async Task CheckForUpdatesIfDueAsync()
    {
        bool startupCheckDue = AppSelfUpdateEnabled && _shell.CheckAppUpdatesOnStartup
            && (ShouldAutoCheckForUpdates()
                || _shell.UpdateCheckInterval == UpdateCheckInterval.Manual
                    && (!_shell.LastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _shell.LastUpdateCheckUtc.Value >= TimeSpan.FromMinutes(30)));
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
        return _shell.ModUpdateCheckInterval switch
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
            SettingsView.UpdateIntervalIndex = (int)_shell.UpdateCheckInterval;
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
        string latestTag = string.IsNullOrWhiteSpace(_shell.LatestReleaseTag) ? T("Updates.NotCheckedYet") : _shell.LatestReleaseTag!;
        string latestTitle = string.IsNullOrWhiteSpace(_shell.LatestReleaseTitle)
            ? T("Updates.LatestReleaseDetailsWillAppearHere")
            : NormalizeUiText(_shell.LatestReleaseTitle);

        string publishedText = ConfigurationService.TryParseDateTimeOffset(_shell.LatestReleasePublishedAt) is DateTimeOffset publishedAt
            ? publishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.UnknownTime");

        string lastCheckedText = _shell.LastUpdateCheckUtc.HasValue
            ? _shell.LastUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.NotCheckedYet");

        SettingsView.ReleaseTitle = T("Updates.LatestRelease", latestTag);
        SettingsView.ReleaseMeta = T("Updates.TitlePublishedLastChecked", latestTitle, publishedText, lastCheckedText);
        SettingsView.ReleaseNotes = string.IsNullOrWhiteSpace(_shell.LatestReleaseBody)
            ? T("Updates.GitHubReleaseNotesWillAppearHere")
            : NormalizeUiText(_shell.LatestReleaseBody);
        // Before the first check there is nothing but placeholders, so the box stays one line.
        SettingsView.HasReleaseDetails = !string.IsNullOrWhiteSpace(_shell.LatestReleaseTag);
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

            if (string.IsNullOrWhiteSpace(_shell.LatestReleaseUrl))
            {
                await CheckForUpdatesAsync(showDialogs: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_shell.LatestReleasePackageUrl) && IsReleaseNewer(_shell.LatestReleaseTag ?? string.Empty))
            {
                await DownloadAndInstallApplicationUpdateAsync(new LatestReleaseInfo(
                    _shell.LatestReleaseTag ?? string.Empty,
                    _shell.LatestReleaseTitle ?? string.Empty,
                    _shell.LatestReleaseBody ?? string.Empty,
                    _shell.LatestReleasePublishedAt ?? string.Empty,
                    _shell.LatestReleaseUrl,
                    _shell.LatestReleasePackageUrl,
                    _shell.LatestReleaseSha256Url));
                return;
            }

            await OpenExternalUrlAsync(_shell.LatestReleaseUrl, T("Common.FailedToOpenTheUpdatePage"));
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
