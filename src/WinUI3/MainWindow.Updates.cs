using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
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
            await CheckTrackedModUpdatesAsync(showDialogs: false);
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

        return !_lastModUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _lastModUpdateCheckUtc.Value >= interval.Value;
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

    private async Task CheckTrackedModUpdatesAsync(bool showDialogs)
    {
        if (_isCheckingModUpdates)
        {
            return;
        }

        if (_trackedModOrigins.Count == 0)
        {
            SetModUpdateStatus(M("Updates.NoTrackedOnlineMods"));
            RefreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.DownloadAndExtractAtLeastOne"),
                    T("Updates.NoTrackedMods"));
            }
            return;
        }

        bool deduplicatedTrackedMods = DeduplicateTrackedModOrigins();
        if (deduplicatedTrackedMods)
        {
            SaveConfig();
            SaveShellConfig();
        }

        try
        {
            _isCheckingModUpdates = true;
            UpdatesView.CheckError = null;
            SetModUpdateStatus(M("Updates.CheckingTrackedMods", _trackedModOrigins.Count));
            RefreshUpdatesPane();

            _trackedModUpdateResults.Clear();
            foreach (TrackedModOrigin origin in _trackedModOrigins.Values.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToArray())
            {
                TrackedModUpdateResult result = await FetchTrackedModUpdateResultAsync(origin);
                _trackedModUpdateResults.Add(result);

                if (!string.IsNullOrWhiteSpace(result.PreviewUrl))
                {
                    origin.PreviewUrl = result.PreviewUrl;
                }

                if (!string.IsNullOrWhiteSpace(result.Title))
                {
                    origin.Title = result.Title;
                }

                if (!string.IsNullOrWhiteSpace(result.ProfileUrl))
                {
                    origin.ProfileUrl = result.ProfileUrl;
                    _modLinks[origin.Path] = result.ProfileUrl;
                }
            }

            _lastModUpdateCheckUtc = DateTimeOffset.UtcNow;
            UpdatesView.HasChecked = true;
            SaveConfig();
            SaveShellConfig();

            int updateCount = _trackedModUpdateResults.Count(item => item.HasUpdate);
            int failedCount = _trackedModUpdateResults.Count(item => item.CheckFailed);
            SetModUpdateStatus(
                failedCount > 0 ? M("Updates.CheckCompleteWithFailures", updateCount, failedCount)
                : updateCount > 0 ? M("Updates.CheckCompleteWithUpdates", updateCount)
                : M("Updates.CheckCompleteAllCurrent"));

            // The check is over; do not keep showing "Checking..." behind the result dialog.
            _isCheckingModUpdates = false;
            RefreshUpdatesPane();

            if (updateCount > 0)
            {
                await NotifyModUpdatesAvailableAsync();
            }
            else if (showDialogs)
            {
                await ShowMessageAsync(
                    updateCount > 0
                        ? T("Updates.CheckedModsAndFoundUpdates", _trackedModUpdateResults.Count, updateCount)
                        : failedCount > 0 ? T("Updates.ModChecksFailedPleaseRetry", failedCount)
                        : T("Updates.CheckedModsAndFoundNoNew", _trackedModUpdateResults.Count),
                    T("Updates.UpdateCheckComplete"));
            }
        }
        catch (Exception ex)
        {
            SetModUpdateStatus(M("Updates.TrackedCheckFailed"));
            UpdatesView.CheckError = T("Updates.FailedToCheckTrackedModUpdates") + ex.Message;
            RefreshUpdatesPane();
            if (showDialogs)
            {
                await ShowMessageAsync(
                    T("Updates.FailedToCheckTrackedModUpdates") + ex.Message,
                    T("Updates.UpdateCheckFailed"));
            }
        }
        finally
        {
            _isCheckingModUpdates = false;
            RefreshUpdatesPane();
        }
    }

    private async Task<TrackedModUpdateResult> FetchTrackedModUpdateResultAsync(TrackedModOrigin origin)
    {
        OnlineModCard? latest = null;
        try
        {
            latest = await FetchGameBananaModCardAsyncV2(origin.ItemId);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Check tracked Mod update", ex);
        }

        if (latest is null)
            return new TrackedModUpdateResult
            {
                Path = origin.Path, ItemId = origin.ItemId, Title = origin.Title,
                ProfileUrl = origin.ProfileUrl, PreviewUrl = origin.PreviewUrl,
                LastKnownUpdatedAt = origin.LastKnownUpdatedAt, CheckFailed = true,
                Status = M("Updates.CheckFailedLatestUnavailable")
            };

        DateTimeOffset? latestUpdatedAt = latest?.UpdatedAt;
        DateTimeOffset? recordedUpdatedAt = origin.LastKnownUpdatedAt;
        bool hasUpdate = latestUpdatedAt.HasValue
            && recordedUpdatedAt.HasValue
            && latestUpdatedAt.Value > recordedUpdatedAt.Value.AddMinutes(1);

        if (!recordedUpdatedAt.HasValue && latestUpdatedAt.HasValue)
        {
            origin.LastKnownUpdatedAt = latestUpdatedAt;
        }

        return new TrackedModUpdateResult
        {
            Path = origin.Path,
            ItemId = origin.ItemId,
            Title = string.IsNullOrWhiteSpace(latest?.Title) ? origin.Title : latest.Title,
            ProfileUrl = string.IsNullOrWhiteSpace(latest?.ProfileUrl) ? origin.ProfileUrl : latest.ProfileUrl,
            PreviewUrl = string.IsNullOrWhiteSpace(latest?.PreviewUrl) ? origin.PreviewUrl : latest.PreviewUrl,
            LastKnownUpdatedAt = recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            LatestUpdatedAt = latestUpdatedAt ?? recordedUpdatedAt ?? origin.LastKnownUpdatedAt,
            HasUpdate = hasUpdate,
            Status = M(hasUpdate ? "Updates.NewerVersionFound" : "Updates.NoUpdateDetected")
        };
    }

    private async void OnCheckModUpdatesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckTrackedModUpdatesAsync(showDialogs: true);
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
            T("Updates.InstallLocalUpdate"));

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
                        T("Updates.AppUpdateAvailable"));
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
