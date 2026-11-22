using System.Runtime.InteropServices;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Updates;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // The logs are AppLog's (stage J finish, step 8a).
    private static void TraceStartupStage(string stage) => AppLog.TraceStartupStage(stage);

    private static void LogPersistentDataStoreIssue(string operation, Exception exception) => AppLog.LogPersistentDataStoreIssue(operation, exception);

    internal static void LogApplicationIssue(string operation, Exception exception) => AppLog.LogApplicationIssue(operation, exception);

    internal static void LogApplicationIssueWithStack(string operation, Exception exception) => AppLog.LogApplicationIssueWithStack(operation, exception);

    private readonly AppDataStore _appDataStore;
    private readonly HashSet<string> _favoriteCharacterKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _systemHighContrast;
    private bool _systemAnimationsEnabled = true;
    private bool _isApplyingStartupUpdateSetting;
    private bool _isApplyingBackupLimitSetting;

    private const uint SpiGetHighContrast = 0x0042;
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint HcfHighContrastOn = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HighContrastInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr DefaultScheme;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        IntPtr value,
        uint flags);

    private void InitializePersistentDataStore()
    {
        _appDataStore.Initialize();
        foreach (string favorite in _appDataStore.ReadFavorites())
        {
            _favoriteCharacterKeys.Add(favorite);
        }
    }

    private bool ShouldReduceAnimations => _shell.ReduceMotion
        || _systemHighContrast
        || !_systemAnimationsEnabled;

    private void InitializeAccessibilityFeatures()
    {
        RootGrid.UseSystemFocusVisuals = true;
        AutomationProperties.SetName(RootGrid, T("ProductivityFeatures.IntegratedModManagerMainWindow"));
        RefreshSystemAccessibilityPreferences();
        RootGrid.ActualThemeChanged += OnRootActualThemeChanged;
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        RefreshSystemAccessibilityPreferences();
    }

    private void RefreshSystemAccessibilityPreferences()
    {
        _systemHighContrast = ReadHighContrastState();
        _services.Appearance.IsHighContrast = _systemHighContrast;
        _systemAnimationsEnabled = ReadClientAreaAnimationState();
    }

    private static bool ReadHighContrastState()
    {
        HighContrastInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<HighContrastInfo>()
        };
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<HighContrastInfo>());
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            return SystemParametersInfo(SpiGetHighContrast, info.Size, buffer, 0)
                && (Marshal.PtrToStructure<HighContrastInfo>(buffer).Flags & HcfHighContrastOn) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ReadClientAreaAnimationState()
    {
        IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, 1);
            return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, buffer, 0)
                || Marshal.ReadInt32(buffer) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void NavigateToPrimarySection(PrimarySection section)
    {
        _shell.CurrentSection = section;
        ApplyShellState(refreshRepository: section == PrimarySection.Repository);
        SaveShellConfig();
    }

    private bool IsFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_services.Online.CharacterCatalogKey))
        {
            return false;
        }

        return _favoriteCharacterKeys.Contains(AppDataStore.BuildFavoriteKey(_services.Online.CharacterCatalogKey, characterKey));
    }

    private void ToggleFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_services.Online.CharacterCatalogKey))
        {
            return;
        }

        string favoriteKey = AppDataStore.BuildFavoriteKey(_services.Online.CharacterCatalogKey, characterKey);
        bool isFavorite = !_favoriteCharacterKeys.Contains(favoriteKey);
        if (isFavorite)
        {
            _favoriteCharacterKeys.Add(favoriteKey);
        }
        else
        {
            _favoriteCharacterKeys.Remove(favoriteKey);
        }

        _appDataStore.SetFavorite(_services.Online.CharacterCatalogKey, characterKey, isFavorite);
        if (_onlineView is null) EnsureOnlineCharacterOptions(_services.Repositories.Selected);
        else _onlineView.RefreshFavoriteCharacters();
        ShowAppNotification(
                isFavorite ? T("ProductivityFeatures.CharacterFavorited") : T("ProductivityFeatures.CharacterUnfavorited"),
                InfoBarSeverity.Informational);
    }

    private Task<LatestReleaseInfo> FetchLatestReleaseInfoAsync(CancellationToken cancellationToken = default)
        => ApplicationUpdateClient.FetchLatestReleaseAsync(_httpClient, GitHubLatestReleaseApiUrl, GitHubRepositoryUrl, cancellationToken);

    private void ApplyLatestReleaseInfo(LatestReleaseInfo release)
    {
        _shell.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _shell.LatestReleaseTag = release.Tag;
        _shell.LatestReleaseTitle = string.IsNullOrWhiteSpace(release.Title) ? release.Tag : release.Title;
        _shell.LatestReleaseBody = release.Body;
        _shell.LatestReleasePublishedAt = release.PublishedAt;
        _shell.LatestReleaseUrl = release.PageUrl;
        _shell.LatestReleasePackageUrl = release.PackageUrl;
        _shell.LatestReleaseSha256Url = release.Sha256Url;
        _appDataStore.WriteCache("app-release", "latest", release);
        SaveShellConfig();
        RefreshUpdateDetailsView();
    }

    private bool IsReleaseNewer(string tag)
    {
        return TryParseVersion(tag, out Version latest)
            && TryParseVersion(AppVersion, out Version current)
            && latest > current;
    }

    private async Task DownloadAndInstallApplicationUpdateAsync(LatestReleaseInfo release)
    {
        if (!AppSelfUpdateEnabled) { return; }
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
            _shell.CurrentSection = PrimarySection.Settings;
            ApplyShellState(refreshRepository: false);
            SettingsView.BeginDownload(T("ProductivityFeatures.PreparingUpdateDownload"));

            if (string.IsNullOrWhiteSpace(release.Sha256Url))
            {
                throw new InvalidDataException(T("ProductivityFeatures.ThisReleaseDoesNotProvideA"));
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await ApplicationUpdateClient.DownloadAsync(_httpClient, release.PackageUrl, temporaryPath, (downloaded, totalBytes) =>
            {
                if (totalBytes is { } total)
                {
                    double progress = Math.Clamp(downloaded * 100d / total, 0, 100);
                    SettingsView.ReportDownload(progress, T("ProductivityFeatures.DownloadingUpdate", progress, StorageSizeText.Format(downloaded), StorageSizeText.Format(total)));
                }
                else
                {
                    SettingsView.ReportDownload(null, downloaded > 0 ? T("ProductivityFeatures.Downloaded", StorageSizeText.Format(downloaded)) : null);
                }
            }, timeout.Token);

            SettingsView.ReportDownloadText(T("ProductivityFeatures.VerifyingUpdatePackage"));
            if (!await ApplicationUpdateClient.VerifyAsync(_httpClient, release.PackageUrl, release.Sha256Url, temporaryPath, timeout.Token))
            {
                throw new InvalidDataException(T("ProductivityFeatures.TheUpdatePackageFailedSHA256"));
            }

            File.Move(temporaryPath, finalPath, true);
            _localUpdatePackagePath = finalPath;
            _localUpdatePackageVersion = releaseVersion;
            SettingsView.ReportDownload(100, T("ProductivityFeatures.DownloadCompleteStartingUpdater"));
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

            SettingsView.EndDownload();
            await ShowMessageAsync(T("ProductivityFeatures.FailedToDownloadOrInstallUpdate") + ex.Message, T("Common.UpdateFailed"));
        }
    }

    private void RefreshBackupManagerText()
    {
        _isApplyingBackupLimitSetting = true;
        UpdatesView.BackupLimitGb = _shell.InstallBackupLimitGb;
        _isApplyingBackupLimitSetting = false;
    }

    private async void OnRefreshBackupsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _services.Install.RefreshBackupListAsync();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRefreshBackupsClicked", ex);
        }
    }

    private void AnimateElementEntrance(UIElement element)
    {
        if (ShouldReduceAnimations)
        {
            return;
        }

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = 0;
        visual.Offset = new System.Numerics.Vector3(0, 8, 0);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(150);
        fade.InsertKeyFrame(1, 1);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Duration = TimeSpan.FromMilliseconds(180);
        slide.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Offset", slide);
    }

}
