using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.Core.Updates;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private const double DefaultInstallBackupLimitGb = 5;
    private static readonly object ApplicationErrorLogSync = new();
    private readonly AppDataStore _appDataStore;
    private readonly HashSet<string> _favoriteCharacterKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _systemHighContrast;
    private bool _systemAnimationsEnabled = true;
    private bool _checkAppUpdatesOnStartup = false;
    private bool _isApplyingStartupUpdateSetting;
    private bool _isApplyingBackupLimitSetting;
    private bool _useWideTrackedModsLayout;
    private double _installBackupLimitGb = DefaultInstallBackupLimitGb;
    private string? _latestReleasePackageUrl;
    private string? _latestReleaseSha256Url;

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

    private static void TraceStartupStage(string stage)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "startup.log"),
                $"{DateTimeOffset.Now:O} {stage}{Environment.NewLine}");
        }
        catch
        {
            // Startup tracing must never prevent the application from opening.
        }
    }

    private static void LogPersistentDataStoreIssue(string operation, Exception exception)
        => LogApplicationIssue("SQLite " + operation, exception);

    internal static void LogApplicationIssue(string operation, Exception exception)
    {
        try
        {
            string summary = SanitizeDiagnosticText(exception.Message);
            lock (ApplicationErrorLogSync)
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "app-errors.log");
                LogFileRotation.RotateIfLarger(logPath);
                File.AppendAllText(
                    logPath,
                    $"{DateTimeOffset.Now:O} {operation}: {exception.GetType().Name} (0x{exception.HResult:X8}) {summary}{Environment.NewLine}");
            }
        }
        catch
        {
            // Error logging must never interrupt the user operation being diagnosed.
        }
    }

    private void InitializePersistentDataStore()
    {
        _appDataStore.Initialize();
        foreach (string favorite in _appDataStore.ReadFavorites())
        {
            _favoriteCharacterKeys.Add(favorite);
        }
    }

    private bool ShouldReduceAnimations => _reduceMotion
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
        _currentPrimarySection = section;
        ApplyShellState(refreshRepository: section == PrimarySection.Repository);
        SaveShellConfig();
    }

    private void UpdateProductivityResponsiveLayout(double width)
    {
        bool narrow = width < 1180;
        UpdatesPrimaryColumn.Width = new GridLength(1.35, GridUnitType.Star);
        UpdatesSecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(0.85, GridUnitType.Star);

        Grid.SetColumn(UpdatesSupportPanel, narrow ? 0 : 1);
        Grid.SetRow(UpdatesSupportPanel, narrow ? 1 : 0);

        bool useWideTrackedModsLayout = !narrow && width >= 1720;
        if (_useWideTrackedModsLayout != useWideTrackedModsLayout)
        {
            _useWideTrackedModsLayout = useWideTrackedModsLayout;
            ApplyTrackedModsListLayout();
        }

        UpdateAppUpdateActionsLayout();
    }

    private void UpdateAppUpdateActionsLayout()
    {
        bool stackUpdateActions = AppUpdateActionsGrid.ActualWidth < 540;
        AppUpdateActionColumn1.Width = new GridLength(1, GridUnitType.Star);
        AppUpdateActionColumn2.Width = stackUpdateActions ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        AppUpdateActionColumn3.Width = stackUpdateActions ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(OpenGitHubButton, 0);
        Grid.SetRow(OpenGitHubButton, 0);
        Grid.SetColumn(CheckUpdatesButton, stackUpdateActions ? 0 : 1);
        Grid.SetRow(CheckUpdatesButton, stackUpdateActions ? 1 : 0);
        Grid.SetColumn(InstallUpdateButton, stackUpdateActions ? 0 : 2);
        Grid.SetRow(InstallUpdateButton, stackUpdateActions ? 2 : 0);
    }

    private bool IsFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_onlineCharacterCatalogKey))
        {
            return false;
        }

        return _favoriteCharacterKeys.Contains(AppDataStore.BuildFavoriteKey(_onlineCharacterCatalogKey, characterKey));
    }

    private void ToggleFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_onlineCharacterCatalogKey))
        {
            return;
        }

        string favoriteKey = AppDataStore.BuildFavoriteKey(_onlineCharacterCatalogKey, characterKey);
        bool isFavorite = !_favoriteCharacterKeys.Contains(favoriteKey);
        if (isFavorite)
        {
            _favoriteCharacterKeys.Add(favoriteKey);
        }
        else
        {
            _favoriteCharacterKeys.Remove(favoriteKey);
        }

        _appDataStore.SetFavorite(_onlineCharacterCatalogKey, characterKey, isFavorite);
        _onlineCharacterStripSignature = null;
        RefreshOnlineCharacterAvatarStrip();
        PopulateOnlineCharacterOptions();
        ShowAppNotification(
            isFavorite ? "已收藏角色并置顶。" : "已取消角色收藏。",
            isFavorite ? "Character favorited and pinned." : "Character removed from favorites.",
            InfoBarSeverity.Informational);
    }

    private FrameworkElement WrapCharacterButtonWithFavorite(Button characterButton, string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey))
        {
            return characterButton;
        }

        bool isFavorite = IsFavoriteCharacter(characterKey);
        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        host.Children.Add(characterButton);

        var favoriteButton = new Button
        {
            Width = _useHorizontalOnlineCharacterRail ? 26 : 30,
            Height = _useHorizontalOnlineCharacterRail ? 26 : 30,
            Padding = new Thickness(0),
            Margin = _useHorizontalOnlineCharacterRail
                ? new Thickness(0, 4, 4, 0)
                : new Thickness(0, 0, 7, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = _useHorizontalOnlineCharacterRail
                ? VerticalAlignment.Top
                : VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Background = GetAppThemeBrush(isFavorite ? "AppAccentSoftBrush" : "AppSecondaryDefaultBrush"),
            BorderBrush = GetAppThemeBrush(isFavorite ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = GetAppThemeBrush(isFavorite ? "AppAccentBrush" : "AppSecondaryDefaultForegroundBrush"),
            Content = new FontIcon
            {
                Glyph = isFavorite ? "\uE735" : "\uE734",
                FontSize = _useHorizontalOnlineCharacterRail ? 12 : 14
            }
        };
        string accessibleName = isFavorite
            ? T("ProductivityFeatures.RemoveCharacterFromFavorites")
            : T("ProductivityFeatures.FavoriteAndPinCharacter");
        AutomationProperties.SetName(favoriteButton, accessibleName);
        ToolTipService.SetToolTip(favoriteButton, accessibleName);
        favoriteButton.Click += (_, args) =>
        {
            ToggleFavoriteCharacter(characterKey);
        };
        host.Children.Add(favoriteButton);
        return host;
    }

    private async Task<LatestReleaseInfo> FetchLatestReleaseInfoAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, GitHubLatestReleaseApiUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;

        string tag = root.TryGetProperty("tag_name", out JsonElement tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        string pageUrl = root.TryGetProperty("html_url", out JsonElement urlElement) ? urlElement.GetString() ?? GitHubRepositoryUrl : GitHubRepositoryUrl;
        string title = root.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? tag : tag;
        string body = root.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
        string publishedAt = root.TryGetProperty("published_at", out JsonElement publishedElement) ? publishedElement.GetString() ?? string.Empty : string.Empty;
        string? packageUrl = null;
        string? packageFileName = null;
        var checksumAssets = new List<(string Name, string DownloadUrl)>();

        if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement assetName) ? assetName.GetString() ?? string.Empty : string.Empty;
                string downloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement downloadElement) ? downloadElement.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                if (LocalUpdatePackageRegex.IsMatch(name))
                {
                    packageUrl = downloadUrl;
                    packageFileName = name;
                }
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksumAssets.Add((name, downloadUrl));
                }
            }
        }

        string? sha256Url = packageFileName is null
            ? null
            : UpdateReleaseAssetSelector.SelectChecksumUrl(packageFileName, checksumAssets);
        return new LatestReleaseInfo(tag, title, body.Trim(), publishedAt, pageUrl, packageUrl, sha256Url);
    }

    private void ApplyLatestReleaseInfo(LatestReleaseInfo release)
    {
        _lastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _latestReleaseTag = release.Tag;
        _latestReleaseTitle = string.IsNullOrWhiteSpace(release.Title) ? release.Tag : release.Title;
        _latestReleaseBody = release.Body;
        _latestReleasePublishedAt = release.PublishedAt;
        _latestReleaseUrl = release.PageUrl;
        _latestReleasePackageUrl = release.PackageUrl;
        _latestReleaseSha256Url = release.Sha256Url;
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
            _currentPrimarySection = PrimarySection.Settings;
            ApplyShellState(refreshRepository: false);
            AppUpdateProgressPanel.Visibility = Visibility.Visible;
            AppUpdateProgressBar.IsIndeterminate = true;
            AppUpdateProgressBar.Value = 0;
            AppUpdateProgressTextBlock.Text = T("ProductivityFeatures.PreparingUpdateDownload");

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using HttpResponseMessage response = await _httpClient.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            AppUpdateProgressBar.IsIndeterminate = !totalBytes.HasValue || totalBytes <= 0;

            await using (Stream source = await response.Content.ReadAsStreamAsync(timeout.Token))
            {
                await UpdatePackageFile.WriteAsync(source, temporaryPath, downloaded =>
                {
                    if (totalBytes is > 0)
                    {
                        double progress = Math.Clamp(downloaded * 100d / totalBytes.Value, 0, 100);
                        AppUpdateProgressBar.Value = progress;
                        AppUpdateProgressTextBlock.Text = T("ProductivityFeatures.DownloadingUpdate", progress, FormatStorageSize(downloaded), FormatStorageSize(totalBytes.Value));
                    }
                    else
                    {
                        AppUpdateProgressTextBlock.Text = T("ProductivityFeatures.Downloaded", FormatStorageSize(downloaded));
                    }
                }, timeout.Token);
            }

            if (string.IsNullOrWhiteSpace(release.Sha256Url))
            {
                throw new InvalidDataException(T("ProductivityFeatures.ThisReleaseDoesNotProvideA"));
            }

            AppUpdateProgressTextBlock.Text = T("ProductivityFeatures.VerifyingUpdatePackage");
            string checksumText = await _httpClient.GetStringAsync(release.Sha256Url, timeout.Token);
            string packageFileName = Path.GetFileName(new Uri(release.PackageUrl).LocalPath);
            string checksumFileName = Path.GetFileName(new Uri(release.Sha256Url).LocalPath);
            string expectedHash = ParseExpectedSha256(checksumText, packageFileName, checksumFileName);
            string actualHash = await UpdatePackageFile.ComputeSha256Async(temporaryPath, timeout.Token);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(T("ProductivityFeatures.TheUpdatePackageFailedSHA256"));
            }

            File.Move(temporaryPath, finalPath, true);
            _localUpdatePackagePath = finalPath;
            _localUpdatePackageVersion = releaseVersion;
            AppUpdateProgressBar.IsIndeterminate = false;
            AppUpdateProgressBar.Value = 100;
            AppUpdateProgressTextBlock.Text = T("ProductivityFeatures.DownloadCompleteStartingUpdater");
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

            AppUpdateProgressBar.IsIndeterminate = false;
            AppUpdateProgressPanel.Visibility = Visibility.Collapsed;
            await ShowMessageAsync(T("ProductivityFeatures.FailedToDownloadOrInstallUpdate") + ex.Message, T("Common.UpdateFailed"));
        }
    }

    private static string ParseExpectedSha256(string checksumText, string packageFileName, string checksumFileName)
    {
        return UpdateChecksumParser.ParseSha256(checksumText, packageFileName, checksumFileName);
    }

    private void OnStartupUpdateToggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingStartupUpdateSetting)
        {
            return;
        }

        _checkAppUpdatesOnStartup = ApplicationReleasePolicy.CanCheckApplicationUpdates(StartupUpdateToggleSwitch.IsOn);
        SaveShellConfig();
    }

    private void RefreshProductivitySettingsText()
    {
        StartupUpdateTitleTextBlock.Text = T("ProductivityFeatures.CheckForAppUpdatesAtStartup");
        StartupUpdateDescriptionTextBlock.Text = T("ProductivityFeatures.AskBeforeDownloadingANewVersion");
        if (!AppSelfUpdateEnabled)
        {
            _checkAppUpdatesOnStartup = false;
            StartupUpdateDescriptionTextBlock.Text = T("ProductivityFeatures.ThisLocalTestBuildDoesNot");
        }
        _isApplyingStartupUpdateSetting = true;
        StartupUpdateToggleSwitch.IsOn = _checkAppUpdatesOnStartup;
        StartupUpdateToggleSwitch.IsEnabled = AppSelfUpdateEnabled;
        StartupUpdateToggleSwitch.OnContent = T("Common.On2");
        StartupUpdateToggleSwitch.OffContent = T("Common.Off2");
        _isApplyingStartupUpdateSetting = false;

        DiagnosticsTitleTextBlock.Text = T("ProductivityFeatures.DiagnosticsAndIssueReport");
        DiagnosticsHintTextBlock.Text = T("ProductivityFeatures.GenerateASanitizedReportWithoutPrivate");
        ExportDiagnosticsButton.Content = T("ProductivityFeatures.ExportDiagnosticReport");
        SubmitDiagnosticsButton.Content = T("ProductivityFeatures.ReportOnGitHub");
        AutomationProperties.SetName(ExportDiagnosticsButton, ExportDiagnosticsButton.Content?.ToString() ?? string.Empty);
        AutomationProperties.SetName(SubmitDiagnosticsButton, SubmitDiagnosticsButton.Content?.ToString() ?? string.Empty);
    }

    private void RefreshBackupManagerText()
    {
        BackupManagerTitleTextBlock.Text = T("ProductivityFeatures.InstallBackups");
        BackupManagerHintTextBlock.Text = T("ProductivityFeatures.AutomaticallyPreserveTheStateBeforeInstalls");
        BackupLimitLabelTextBlock.Text = T("ProductivityFeatures.StorageLimitGB");
        RefreshBackupsButton.Content = T("ProductivityFeatures.RefreshList");
        AutomationProperties.SetName(BackupLimitNumberBox, T("ProductivityFeatures.InstallBackupStorageLimitInGB"));
        AutomationProperties.SetName(RefreshBackupsButton, RefreshBackupsButton.Content?.ToString() ?? string.Empty);
        AutomationProperties.SetName(BackupHistoryExpander, T("ProductivityFeatures.ExpandInstallBackupHistory"));
        _isApplyingBackupLimitSetting = true;
        BackupLimitNumberBox.Value = _installBackupLimitGb;
        _isApplyingBackupLimitSetting = false;
    }

    private async void OnBackupLimitValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        try
        {
            if (_isApplyingBackupLimitSetting || double.IsNaN(args.NewValue))
            {
                return;
            }

            _installBackupLimitGb = Math.Clamp(args.NewValue, 0.5, 100);
            SaveShellConfig();
            await Task.Run(() => PruneInstallTransactionsByStorageLimit(null));
            await RefreshInstallBackupListAsync();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnBackupLimitValueChanged", ex);
        }
    }

    private async void OnRefreshBackupsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshInstallBackupListAsync();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRefreshBackupsClicked", ex);
        }
    }

    private async Task RefreshInstallBackupListAsync()
    {
        BackupListPanel.Children.Clear();
        Directory.CreateDirectory(_modInstallBackupPath);
        List<InstallBackupDisplayItem> backups = await Task.Run(ReadInstallBackups);
        long totalBytes = backups.Sum(item => item.SizeBytes);
        BackupSummaryTextBlock.Text = T("ProductivityFeatures.BackupsGB", backups.Count, FormatStorageSize(totalBytes), _installBackupLimitGb);

        if (backups.Count == 0)
        {
            BackupListPanel.Children.Add(new TextBlock
            {
                Text = T("ProductivityFeatures.NoInstallBackupsYet"),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"]
            });
            return;
        }

        foreach (InstallBackupDisplayItem backup in backups.Take(30))
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var details = new StackPanel { Spacing = 3 };
            details.Children.Add(new TextBlock { Text = backup.Description, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            details.Children.Add(new TextBlock
            {
                Text = T("ProductivityFeatures.Text", backup.CreatedAt.ToLocalTime(), backup.ModNames, FormatStorageSize(backup.SizeBytes)),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(details);
            var restore = new Button
            {
                Content = T("ProductivityFeatures.Restore"),
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Tag = backup.Path
            };
            Grid.SetColumn(restore, 1);
            AutomationProperties.SetName(restore, T("ProductivityFeatures.RestoreBackup", backup.Description));
            restore.Click += OnRestoreInstallBackupClicked;
            grid.Children.Add(restore);
            var border = new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                Background = GetAppThemeBrush("AppInsetBackgroundBrush"),
                BorderBrush = GetAppThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                Child = grid
            };
            BackupListPanel.Children.Add(border);
            AnimateElementEntrance(border);
        }
    }

    private List<InstallBackupDisplayItem> ReadInstallBackups()
    {
        if (!Directory.Exists(_modInstallBackupPath))
        {
            return [];
        }

        List<InstallBackupDisplayItem> result = [];
        foreach (string directory in Directory.EnumerateDirectories(_modInstallBackupPath))
        {
            try
            {
                string manifestPath = Path.Combine(directory, "transaction.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                ModInstallTransaction? transaction = JsonSerializer.Deserialize<ModInstallTransaction>(File.ReadAllText(manifestPath));
                if (transaction is null)
                {
                    continue;
                }

                long size = GetDirectorySize(directory);
                string mods = string.Join(", ", transaction.Entries.Select(entry => Path.GetFileName(entry.TargetPath)).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
                if (transaction.Entries.Count > 5)
                {
                    mods += T("ProductivityFeatures.AndMore");
                }
                result.Add(new InstallBackupDisplayItem(directory, transaction.Description, transaction.CreatedAtUtc, string.IsNullOrWhiteSpace(mods) ? T("ProductivityFeatures.NoModRecorded") : mods, size));
            }
            catch
            {
            }
        }

        return result.OrderByDescending(item => item.CreatedAt).ToList();
    }

    private async void OnRestoreInstallBackupClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string backupPath } || !Directory.Exists(backupPath))
            {
                return;
            }

            if (!await ShowConfirmAsync(T("ProductivityFeatures.RestoringThisBackupReplacesTheCurrent"), T("ProductivityFeatures.RestoreInstallBackup")))
            {
                return;
            }

            SetBusyState(true);
            try
            {
                await RollbackInstallTransactionAsync(backupPath, clearLastTransaction: true);
                await RefreshListsAsync();
                await RefreshInstallBackupListAsync();
                ShowAppNotification("备份已恢复。", "Backup restored.");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(T("ProductivityFeatures.FailedToRestoreBackup") + ex.Message, T("ProductivityFeatures.RestoreFailed"));
            }
            finally
            {
                SetBusyState(false);
            }
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRestoreInstallBackupClicked", ex);
        }
    }

    private void PruneInstallTransactionsByStorageLimit(string? preservePath)
    {
        try
        {
            if (!Directory.Exists(_modInstallBackupPath))
            {
                return;
            }

            string root = Path.GetFullPath(_modInstallBackupPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            List<(DirectoryInfo Directory, long Size)> directories = new DirectoryInfo(_modInstallBackupPath).EnumerateDirectories()
                .Select(directory => (directory, GetDirectorySize(directory.FullName)))
                .OrderByDescending(item => item.directory.CreationTimeUtc)
                .ToList();
            long limitBytes = (long)(_installBackupLimitGb * 1024 * 1024 * 1024);
            long totalBytes = directories.Sum(item => item.Size);
            foreach ((DirectoryInfo directory, long size) in directories.OrderBy(item => item.Directory.CreationTimeUtc))
            {
                if (totalBytes <= limitBytes)
                {
                    break;
                }
                if (!string.IsNullOrWhiteSpace(preservePath) && string.Equals(directory.FullName, preservePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string resolved = Path.GetFullPath(directory.FullName);
                if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    DeleteDirectoryTreeSafely(resolved);
                    totalBytes -= size;
                }
                catch (Exception ex)
                {
                    LogApplicationIssue("Backup pruning", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Backup pruning inventory", ex);
        }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            return InspectDirectoryTreeSafely(path).Files.Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Directory size calculation", ex);
            return 0;
        }
    }

    private async void OnExportDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = await CreateDiagnosticReportAsync();
            DiagnosticsStatusTextBlock.Text = T("ProductivityFeatures.Exported", Path.GetFileName(path));
            Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnExportDiagnosticsClicked", ex);
        }
    }

    private async void OnSubmitDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = await CreateDiagnosticReportAsync();
            bool confirmed = await ShowConfirmAsync(
                T("ProductivityFeatures.DiagnosticReportCreatedGitHubSNew", Path.GetFileName(path)),
                T("ProductivityFeatures.ReportAnIssue"));
            if (!confirmed)
            {
                return;
            }

            string body = Uri.EscapeDataString($"App version: {AppVersion}\nOS: {Environment.OSVersion.VersionString}\nArchitecture: {RuntimeInformation.OSArchitecture}\n\nA sanitized diagnostic report was generated locally: {Path.GetFileName(path)}\nPlease attach it after reviewing the archive.");
            string url = $"{GitHubRepositoryUrl}/issues/new?title={Uri.EscapeDataString("Bug report " + AppVersion)}&body={body}";
            await Launcher.LaunchUriAsync(new Uri(url));
            Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnSubmitDiagnosticsClicked", ex);
        }
    }

    private async Task<string> CreateDiagnosticReportAsync()
    {
        string diagnosticsRoot = Path.Combine(AppContext.BaseDirectory, "diagnostics");
        Directory.CreateDirectory(diagnosticsRoot);
        string reportPath = Path.Combine(diagnosticsRoot, $"IntegratedModManager-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await using FileStream output = new(reportPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        var summary = new StringBuilder();
        summary.AppendLine($"Integrated Mod Manager diagnostics");
        summary.AppendLine($"Generated (UTC): {DateTimeOffset.UtcNow:O}");
        summary.AppendLine($"App version: {AppVersion}");
        summary.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        summary.AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
        summary.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        summary.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        summary.AppendLine($"Language: {_currentLanguage}");
        summary.AppendLine($"Theme: {(_isDarkTheme ? "Dark" : "Light")}");
        summary.AppendLine($"Repository count: {_repositories.Count}");
        summary.AppendLine($"Source paths configured: {_repositories.Count(item => !string.IsNullOrWhiteSpace(item.SourcePath))}");
        summary.AppendLine($"Target paths configured: {_repositories.Count(item => !string.IsNullOrWhiteSpace(item.TargetPath))}");
        summary.AppendLine($"SQLite: {_appDataStore.GetHealthSummary()}");
        WriteZipText(archive, "report.txt", summary.ToString());

        var sanitizedConfig = new
        {
            AppVersion,
            Language = _currentLanguage.ToString(),
            Theme = _isDarkTheme ? "Dark" : "Light",
            _checkAppUpdatesOnStartup,
            UpdateInterval = _updateCheckInterval.ToString(),
            _installBackupLimitGb,
            ReduceMotion = _reduceMotion,
            Density = _interfaceDensity.ToString(),
            Repositories = _repositories.Select((repository, index) => new
            {
                Number = index + 1,
                HasSourcePath = !string.IsNullOrWhiteSpace(repository.SourcePath),
                HasTargetPath = !string.IsNullOrWhiteSpace(repository.TargetPath),
                HasLauncher = !string.IsNullOrWhiteSpace(repository.LauncherPath),
                OnlineSource = repository.OnlineSourceSite,
                OnlineGame = repository.OnlineGameName,
                OnlineCategoryId = repository.OnlineCategoryId,
                HasWiki = !string.IsNullOrWhiteSpace(repository.WikiUrl),
                CharacterMappingCount = repository.CharacterCategoryMappings?.Count ?? 0
            }).ToArray()
        };
        WriteZipText(archive, "config-summary.json", JsonSerializer.Serialize(sanitizedConfig, new JsonSerializerOptions { WriteIndented = true }));

        foreach (string logPath in EnumerateDiagnosticLogs())
        {
            try
            {
                string text = await File.ReadAllTextAsync(logPath);
                if (text.Length > 250_000)
                {
                    text = text[^250_000..];
                }
                WriteZipText(archive, "logs/" + Path.GetFileName(logPath), SanitizeDiagnosticText(text));
            }
            catch
            {
            }
        }
        return reportPath;
    }

    private IEnumerable<string> EnumerateDiagnosticLogs()
    {
        foreach (string name in new[] { "ui-action-context.json", "ui-action-context.json.previous" })
        {
            string actionPath = Path.Combine(AppContext.BaseDirectory, "diagnostics", name);
            if (File.Exists(actionPath)) yield return actionPath;
        }
        string[] names = ["startup.log", "app-errors.log", "update-agent.log", "update.log", "ui-diagnostics.log"];
        foreach (string name in names)
        {
            string path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path))
            {
                yield return path;
            }
            string rootPath = Path.Combine(GetInstallRootPath(), name);
            if (!string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(rootPath))
            {
                yield return rootPath;
            }
        }
    }

    private static string SanitizeDiagnosticText(string text)
    {
        text = Regex.Replace(text, @"(?i)(?:[a-z]:\\|\\\\)[^\r\n]+", "<private-path>");
        text = Regex.Replace(text, @"(?i)(token|authorization|password|secret|api[_-]?key)\s*[:=]\s*\S+", "$1=<redacted>");
        text = Regex.Replace(text, @"(?i)([?&](?:token|key|auth|signature)=)[^&\s]+", "$1<redacted>");
        return text;
    }

    private static void WriteZipText(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
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

    private static string FormatStorageSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private (List<FirstLevelFolderItem> Items, int SecondCount) LoadFolderItemsUsingIndex(
        string repositoryId,
        string sourceDir,
        string targetDir,
        CancellationToken cancellationToken,
        out int warningCount)
    {
        var scanner = new RepositoryScanService(new RepositoryIndexStoreAdapter(_appDataStore));
        RepositoryScanResult scan;
        using (var scanTiming = _repositoryPerformance.Measure(RepositoryPerformanceOperation.Scan))
        {
            scan = scanner.Scan(repositoryId, sourceDir, cancellationToken);
            scanTiming?.SetCounts(scan.ModCount, scan.CacheHits);
        }
        using var projectionTiming = _repositoryPerformance.Measure(RepositoryPerformanceOperation.Projection, scan.ModCount, scan.CacheHits);
        warningCount = scan.Warnings.Count;
        foreach (RepositoryScanWarning warning in scan.Warnings)
        {
            LogApplicationIssue(warning.Operation, warning.Error);
        }
        List<FirstLevelFolderItem> loadedItems = [];
        foreach (RepositoryScanCategory category in scan.Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var firstItem = new FirstLevelFolderItem(category.Path);
            foreach (RepositoryModIndexEntry mod in category.Mods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                firstItem.Children.Add(new SecondLevelFolderItem(
                    mod.ModPath, mod.Files.ToList(), GetFolderCopyState(targetDir, mod.ModPath)));
            }
            loadedItems.Add(firstItem);
        }
        return (loadedItems, scan.ModCount);
    }

    private sealed record LatestReleaseInfo(
        string Tag,
        string Title,
        string Body,
        string PublishedAt,
        string PageUrl,
        string? PackageUrl,
        string? Sha256Url);

    private sealed record InstallBackupDisplayItem(
        string Path,
        string Description,
        DateTimeOffset CreatedAt,
        string ModNames,
        long SizeBytes);
}
