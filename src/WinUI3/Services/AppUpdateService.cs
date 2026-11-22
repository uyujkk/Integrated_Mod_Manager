using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Updates;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The app's own updates (stage J finish, step 8c, moved from the window unchanged): the check intervals, checking the
/// latest GitHub release, a package beside the install, downloading and verifying a release, starting the local update
/// agent, and what Settings shows about it. Builds that cannot update themselves (ApplicationReleasePolicy) only point
/// to the release page. The tracked Mods' checks are ModTrackingService's.
/// </summary>
internal sealed class AppUpdateService(AppServices services)
{
    /// <summary>The project's GitHub page.</summary>
    public const string GitHubRepositoryUrl = "https://github.com/uyujkk/Integrated_Mod_Manager";
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/uyujkk/Integrated_Mod_Manager/releases/latest";

    private readonly AppServices _services = services;
    private HttpClient _http = null!;
    private AppDataStore _dataStore = null!;
    private Action _refreshSettingsPane = () => { };
    private Action _showSettings = () => { };
    private Action _closeWindow = () => { };
    private Action<string, Exception> _reportFailure = (_, _) => { };
    private UiMessage? _updateStatus;
    private string? _localUpdatePackagePath;
    private Version? _localUpdatePackageVersion;
    private bool _isCheckingUpdates;
    private bool _hasPromptedForLocalUpdate;

    /// <summary>
    /// Called once by the window: the HTTP client, the local index (it caches the latest release), redrawing Settings,
    /// showing Settings (during a download), closing the window (for the update agent) and reporting a failed action.
    /// </summary>
    public void Attach(HttpClient http, AppDataStore dataStore, Action refreshSettingsPane, Action showSettings, Action closeWindow,
        Action<string, Exception> reportFailure)
    {
        _http = http;
        _dataStore = dataStore;
        _refreshSettingsPane = refreshSettingsPane;
        _showSettings = showSettings;
        _closeWindow = closeWindow;
        _reportFailure = reportFailure;
    }

    /// <summary>A package was found and the user has not been asked about it yet.</summary>
    public bool HasUnpromptedLocalPackage => _localUpdatePackageVersion is not null && !_hasPromptedForLocalUpdate;

    /// <summary>Settings' status line: the last result, or the current version.</summary>
    public void RefreshStatusText()
    {
        if (_updateStatus is null)
        {
            SetUpdateStatus(M("Updates.CurrentVersion", ApplicationReleasePolicy.VersionLabel));
        }
        else
        {
            _services.Settings.UpdateStatus = T(_updateStatus);
        }
    }

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private string T(UiMessage message) => _services.Language.T(message);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    private Task<bool> ShowConfirmAsync(string content, string title, string primaryText) => _services.Dialogs.ConfirmAsync(title, content, primaryText);

    private Task OpenExternalUrlAsync(string? url, string failureTitle) => _services.Dialogs.OpenExternalUrlAsync(url, failureTitle);

    private TimeSpan? GetUpdateCheckIntervalTimeSpan()
    {
        return _services.ShellSettings.UpdateCheckInterval switch
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

        return !_services.ShellSettings.LastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _services.ShellSettings.LastUpdateCheckUtc.Value >= interval.Value;
    }

    /// <summary>At startup: the app check when it is due and enabled, then the tracked Mods' check when due.</summary>
    public async Task CheckForUpdatesIfDueAsync()
    {
        bool startupCheckDue = ApplicationReleasePolicy.ApplicationSelfUpdateEnabled && _services.ShellSettings.CheckAppUpdatesOnStartup
            && (ShouldAutoCheckForUpdates()
                || _services.ShellSettings.UpdateCheckInterval == UpdateCheckInterval.Manual
                    && (!_services.ShellSettings.LastUpdateCheckUtc.HasValue || DateTimeOffset.UtcNow - _services.ShellSettings.LastUpdateCheckUtc.Value >= TimeSpan.FromMinutes(30)));
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
        return _services.ShellSettings.ModUpdateCheckInterval switch
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
        _services.Settings.UpdateStatus = T(status);
    }

    public void RefreshUpdateDetailsView()
    {
        if (!ApplicationReleasePolicy.ApplicationSelfUpdateEnabled)
        {
            _services.Settings.ReleaseTitle = T("Updates.StandaloneBeta", ApplicationReleasePolicy.VersionLabel);
            _services.Settings.ReleaseMeta = T("Updates.ManualDownloadExtractSeparatelyFromThe");
            _services.Settings.ReleaseNotes = T("Updates.ModUpdateTestBuildFixesInterval");
            return;
        }
        string latestTag = string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleaseTag) ? T("Updates.NotCheckedYet") : _services.ShellSettings.LatestReleaseTag!;
        string latestTitle = string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleaseTitle)
            ? T("Updates.LatestReleaseDetailsWillAppearHere")
            : _services.Language.NormalizeUiText(_services.ShellSettings.LatestReleaseTitle);

        string publishedText = ConfigurationService.TryParseDateTimeOffset(_services.ShellSettings.LatestReleasePublishedAt) is DateTimeOffset publishedAt
            ? publishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.UnknownTime");

        string lastCheckedText = _services.ShellSettings.LastUpdateCheckUtc.HasValue
            ? _services.ShellSettings.LastUpdateCheckUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : T("Updates.NotCheckedYet");

        _services.Settings.ReleaseTitle = T("Updates.LatestRelease", latestTag);
        _services.Settings.ReleaseMeta = T("Updates.TitlePublishedLastChecked", latestTitle, publishedText, lastCheckedText);
        _services.Settings.ReleaseNotes = string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleaseBody)
            ? T("Updates.GitHubReleaseNotesWillAppearHere")
            : _services.Language.NormalizeUiText(_services.ShellSettings.LatestReleaseBody);
        // Before the first check there is nothing but placeholders, so the box stays one line.
        _services.Settings.HasReleaseDetails = !string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleaseTag);
    }

    /// <summary>Settings' "check for updates".</summary>
    public async void CheckNow()
    {
        try
        {
            await CheckForUpdatesAsync(showDialogs: true);
        }
        catch (Exception ex)
        {
            _reportFailure("OnCheckUpdatesClicked", ex);
        }
    }

    /// <summary>Settings' update button: a local package, a check, the downloaded release or the release page.</summary>
    public async void Install()
    {
        try
        {
            if (!ApplicationReleasePolicy.ApplicationSelfUpdateEnabled)
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

            if (string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleaseUrl))
            {
                await CheckForUpdatesAsync(showDialogs: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleasePackageUrl) && IsReleaseNewer(_services.ShellSettings.LatestReleaseTag ?? string.Empty))
            {
                await DownloadAndInstallApplicationUpdateAsync(new LatestReleaseInfo(
                    _services.ShellSettings.LatestReleaseTag ?? string.Empty,
                    _services.ShellSettings.LatestReleaseTitle ?? string.Empty,
                    _services.ShellSettings.LatestReleaseBody ?? string.Empty,
                    _services.ShellSettings.LatestReleasePublishedAt ?? string.Empty,
                    _services.ShellSettings.LatestReleaseUrl,
                    _services.ShellSettings.LatestReleasePackageUrl,
                    _services.ShellSettings.LatestReleaseSha256Url));
                return;
            }

            await OpenExternalUrlAsync(_services.ShellSettings.LatestReleaseUrl, T("Common.FailedToOpenTheUpdatePage"));
        }
        catch (Exception ex)
        {
            _reportFailure("OnInstallUpdateClicked", ex);
        }
    }

    /// <summary>Finds the newest update package beside the install that is newer than this version.</summary>
    public void DetectLocalUpdatePackage()
    {
        _localUpdatePackagePath = null;
        _localUpdatePackageVersion = null;

        if (!ApplicationReleasePolicy.ApplicationSelfUpdateEnabled)
        {
            return;
        }

        string installRoot = GetInstallRootPath();
        if (!Directory.Exists(installRoot) || !TryParseVersion(ApplicationReleasePolicy.VersionLabel, out Version currentVersion))
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

    /// <summary>The folder above WinUI3, where packages and the update agent are.</summary>
    public static string GetInstallRootPath()
    {
        var runtimeDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return runtimeDirectory.Name.Equals("WinUI3", StringComparison.OrdinalIgnoreCase) && runtimeDirectory.Parent is not null
            ? runtimeDirectory.Parent.FullName
            : runtimeDirectory.FullName;
    }

    public async Task PromptAndStartLocalUpdateAsync()
    {
        if (!ApplicationReleasePolicy.ApplicationSelfUpdateEnabled) { return; }
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
        if (!ApplicationReleasePolicy.ApplicationSelfUpdateEnabled) { return; }
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

            _services.Configuration.Save();
            _services.Configuration.SaveShell();

            string updaterCopy = Path.Combine(Path.GetTempPath(), $"IntegratedModManagerUpdateAgent-{Guid.NewGuid():N}.exe");
            File.Copy(updaterSource, updaterCopy, overwrite: true);

            string arguments = string.Join(" ",
                "--package", QuoteCommandLineArgument(packagePath),
                "--install-root", QuoteCommandLineArgument(installRoot),
                "--process-id", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                "--launcher", QuoteCommandLineArgument(launcherPath),
                "--expected-version", _localUpdatePackageVersion?.ToString() ?? string.Empty,
                "--language", !_services.Language.IsChinese ? "en" : "zh");

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterCopy,
                Arguments = arguments,
                WorkingDirectory = installRoot,
                UseShellExecute = false
            });

            _closeWindow();
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
        if (!ApplicationReleasePolicy.ApplicationSelfUpdateEnabled) { return; }
        if (_isCheckingUpdates)
        {
            return;
        }

        try
        {
            _isCheckingUpdates = true;
            _services.Settings.IsCheckingUpdates = true;
            _refreshSettingsPane();
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
                SetUpdateStatus(M("Updates.AlreadyLatest", ApplicationReleasePolicy.VersionLabel));
                if (showDialogs)
                {
                    _services.Status.Notify(T("Updates.AppUpToDate"), InfoBarSeverity.Success);
                }
            }
            else
            {
                SetUpdateStatus(M("Updates.NewVersionDetected", release.Tag, ApplicationReleasePolicy.VersionLabel));
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
            _services.Settings.IsCheckingUpdates = false;
            _refreshSettingsPane();
        }
    }

    private Task<LatestReleaseInfo> FetchLatestReleaseInfoAsync(CancellationToken cancellationToken = default)
        => ApplicationUpdateClient.FetchLatestReleaseAsync(_http, GitHubLatestReleaseApiUrl, GitHubRepositoryUrl, cancellationToken);
    private void ApplyLatestReleaseInfo(LatestReleaseInfo release)
    {
        _services.ShellSettings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _services.ShellSettings.LatestReleaseTag = release.Tag;
        _services.ShellSettings.LatestReleaseTitle = string.IsNullOrWhiteSpace(release.Title) ? release.Tag : release.Title;
        _services.ShellSettings.LatestReleaseBody = release.Body;
        _services.ShellSettings.LatestReleasePublishedAt = release.PublishedAt;
        _services.ShellSettings.LatestReleaseUrl = release.PageUrl;
        _services.ShellSettings.LatestReleasePackageUrl = release.PackageUrl;
        _services.ShellSettings.LatestReleaseSha256Url = release.Sha256Url;
        _dataStore.WriteCache("app-release", "latest", release);
        _services.Configuration.SaveShell();
        RefreshUpdateDetailsView();
    }
    private bool IsReleaseNewer(string tag)
    {
        return TryParseVersion(tag, out Version latest)
            && TryParseVersion(ApplicationReleasePolicy.VersionLabel, out Version current)
            && latest > current;
    }
    private async Task DownloadAndInstallApplicationUpdateAsync(LatestReleaseInfo release)
    {
        if (!ApplicationReleasePolicy.ApplicationSelfUpdateEnabled) { return; }
        if (string.IsNullOrWhiteSpace(release.PackageUrl) || !TryParseVersion(release.Tag, out Version releaseVersion))
        {
            await ShowMessageAsync(
                T("ProductivityFeatures.TheLatestReleaseHasNoRecognized"),
                T("ProductivityFeatures.NoDownloadableUpdate"));
            await OpenExternalUrlAsync(release.PageUrl, T("Common.FailedToOpenTheUpdatePage"));
            return;
        }

        string installRoot = GetInstallRootPath();
        string finalPath = Path.Combine(installRoot, $"Integrated_Mod_Manager-v{releaseVersion}.zip");
        string temporaryPath = finalPath + ".download";
        try
        {
            _showSettings();
            _services.Settings.BeginDownload(T("ProductivityFeatures.PreparingUpdateDownload"));

            if (string.IsNullOrWhiteSpace(release.Sha256Url))
            {
                throw new InvalidDataException(T("ProductivityFeatures.ThisReleaseDoesNotProvideA"));
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await ApplicationUpdateClient.DownloadAsync(_http, release.PackageUrl, temporaryPath, (downloaded, totalBytes) =>
            {
                if (totalBytes is { } total)
                {
                    double progress = Math.Clamp(downloaded * 100d / total, 0, 100);
                    _services.Settings.ReportDownload(progress, T("ProductivityFeatures.DownloadingUpdate", progress, StorageSizeText.Format(downloaded), StorageSizeText.Format(total)));
                }
                else
                {
                    _services.Settings.ReportDownload(null, downloaded > 0 ? T("ProductivityFeatures.Downloaded", StorageSizeText.Format(downloaded)) : null);
                }
            }, timeout.Token);

            _services.Settings.ReportDownloadText(T("ProductivityFeatures.VerifyingUpdatePackage"));
            if (!await ApplicationUpdateClient.VerifyAsync(_http, release.PackageUrl, release.Sha256Url, temporaryPath, timeout.Token))
            {
                throw new InvalidDataException(T("ProductivityFeatures.TheUpdatePackageFailedSHA256"));
            }

            File.Move(temporaryPath, finalPath, true);
            _localUpdatePackagePath = finalPath;
            _localUpdatePackageVersion = releaseVersion;
            _services.Settings.ReportDownload(100, T("ProductivityFeatures.DownloadCompleteStartingUpdater"));
            await StartLocalUpdateAsync(finalPath);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Preserve the original update failure if a scanner still holds the temporary file.
            }

            _services.Settings.EndDownload();
            await ShowMessageAsync(T("ProductivityFeatures.FailedToDownloadOrInstallUpdate") + ex.Message, T("Common.UpdateFailed"));
        }
    }

    // What the update button can do now.
    public void RefreshAppUpdateInstallState()
        => _services.Settings.SetInstallState(
            _localUpdatePackageVersion?.ToString(),
            hasReleasePage: !string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleaseUrl),
            canDownloadRelease: !string.IsNullOrWhiteSpace(_services.ShellSettings.LatestReleasePackageUrl) && IsReleaseNewer(_services.ShellSettings.LatestReleaseTag ?? string.Empty));
}
