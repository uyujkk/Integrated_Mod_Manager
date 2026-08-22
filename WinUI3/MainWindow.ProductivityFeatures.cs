using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private bool _checkAppUpdatesOnStartup = true;
    private bool _isApplyingStartupUpdateSetting;
    private bool _isApplyingBackupLimitSetting;
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

    private static void LogApplicationIssue(string operation, Exception exception)
    {
        try
        {
            string summary = SanitizeDiagnosticText(exception.Message);
            lock (ApplicationErrorLogSync)
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "app-errors.log"),
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
        AutomationProperties.SetName(RootGrid, L("集成化 Mod 管理器主窗口", "Integrated Mod Manager main window"));
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
        UpdatesPrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
        UpdatesSecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(0.85, GridUnitType.Star);
        if (UpdatesTopGrid.Children.Count >= 2)
        {
            FrameworkElement secondary = (FrameworkElement)UpdatesTopGrid.Children[1];
            Grid.SetColumn(secondary, narrow ? 0 : 1);
            Grid.SetRow(secondary, narrow ? 1 : 0);
            secondary.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        }

        bool stackUpdateActions = width < 980;
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
            ? L("取消收藏角色", "Remove character from favorites")
            : L("收藏角色并置顶", "Favorite and pin character");
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
        string? sha256Url = null;

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
                }
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    sha256Url = downloadUrl;
                }
            }
        }

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
        if (string.IsNullOrWhiteSpace(release.PackageUrl) || !TryParseVersion(release.Tag, out Version releaseVersion))
        {
            await ShowMessageAsync(
                L("最新 Release 没有可识别的应用 ZIP，请从 Release 页面手动下载。", "The latest release has no recognized app ZIP. Download it manually from the release page."),
                L("没有可下载更新", "No Downloadable Update"));
            await OpenExternalUrlAsync(release.PageUrl, L("打开更新页面失败", "Failed to open the update page"));
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
            AppUpdateProgressTextBlock.Text = L("正在准备下载更新...", "Preparing update download...");

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using HttpResponseMessage response = await _httpClient.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            AppUpdateProgressBar.IsIndeterminate = !totalBytes.HasValue || totalBytes <= 0;

            await using Stream source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using FileStream destination = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
            byte[] buffer = new byte[1024 * 128];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                downloaded += read;
                if (totalBytes is > 0)
                {
                    double progress = Math.Clamp(downloaded * 100d / totalBytes.Value, 0, 100);
                    AppUpdateProgressBar.Value = progress;
                    AppUpdateProgressTextBlock.Text = L(
                        $"正在下载更新：{progress:0}%（{FormatStorageSize(downloaded)} / {FormatStorageSize(totalBytes.Value)}）",
                        $"Downloading update: {progress:0}% ({FormatStorageSize(downloaded)} / {FormatStorageSize(totalBytes.Value)})");
                }
                else
                {
                    AppUpdateProgressTextBlock.Text = L($"已下载 {FormatStorageSize(downloaded)}", $"Downloaded {FormatStorageSize(downloaded)}");
                }
            }

            await destination.FlushAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(release.Sha256Url))
            {
                throw new InvalidDataException(L(
                    "此 Release 没有提供更新包校验文件，已停止自动更新。",
                    "This release does not provide a package checksum. Automatic update was stopped."));
            }

            AppUpdateProgressTextBlock.Text = L("正在校验更新包...", "Verifying update package...");
            string checksumText = await _httpClient.GetStringAsync(release.Sha256Url, timeout.Token);
            string packageFileName = Path.GetFileName(new Uri(release.PackageUrl).LocalPath);
            string checksumFileName = Path.GetFileName(new Uri(release.Sha256Url).LocalPath);
            string expectedHash = ParseExpectedSha256(checksumText, packageFileName, checksumFileName);
            await using (FileStream file = File.OpenRead(temporaryPath))
            {
                string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token));
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(L("更新包 SHA-256 校验失败。", "The update package failed SHA-256 verification."));
                }
            }

            File.Move(temporaryPath, finalPath, true);
            _localUpdatePackagePath = finalPath;
            _localUpdatePackageVersion = releaseVersion;
            AppUpdateProgressBar.IsIndeterminate = false;
            AppUpdateProgressBar.Value = 100;
            AppUpdateProgressTextBlock.Text = L("下载完成，正在启动更新程序...", "Download complete. Starting updater...");
            await StartLocalUpdateAsync(finalPath);
        }
        catch (Exception ex)
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            AppUpdateProgressBar.IsIndeterminate = false;
            AppUpdateProgressPanel.Visibility = Visibility.Collapsed;
            await ShowMessageAsync(L("下载或安装更新失败：", "Failed to download or install update: ") + ex.Message, L("更新失败", "Update Failed"));
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

        _checkAppUpdatesOnStartup = StartupUpdateToggleSwitch.IsOn;
        SaveShellConfig();
    }

    private void RefreshProductivitySettingsText()
    {
        StartupUpdateTitleTextBlock.Text = L("启动时检查软件更新", "Check for app updates at startup");
        StartupUpdateDescriptionTextBlock.Text = L("发现新版本时询问是否下载，完成后自动重启并保留本地配置。", "Ask before downloading a new version, then restart automatically while preserving local settings.");
        _isApplyingStartupUpdateSetting = true;
        StartupUpdateToggleSwitch.IsOn = _checkAppUpdatesOnStartup;
        StartupUpdateToggleSwitch.OnContent = L("开", "On");
        StartupUpdateToggleSwitch.OffContent = L("关", "Off");
        _isApplyingStartupUpdateSetting = false;

        DiagnosticsTitleTextBlock.Text = L("诊断与问题报告", "Diagnostics and Issue Report");
        DiagnosticsHintTextBlock.Text = L("生成脱敏报告，不包含本地隐私路径或访问凭据。提交前由你检查并确认。", "Generate a sanitized report without private local paths or credentials. You review it before submitting.");
        ExportDiagnosticsButton.Content = L("导出诊断报告", "Export Diagnostic Report");
        SubmitDiagnosticsButton.Content = L("提交到 GitHub", "Report on GitHub");
        AutomationProperties.SetName(ExportDiagnosticsButton, ExportDiagnosticsButton.Content?.ToString() ?? string.Empty);
        AutomationProperties.SetName(SubmitDiagnosticsButton, SubmitDiagnosticsButton.Content?.ToString() ?? string.Empty);
    }

    private void RefreshBackupManagerText()
    {
        BackupManagerTitleTextBlock.Text = L("安装备份", "Install Backups");
        BackupManagerHintTextBlock.Text = L("每次 Mod 安装、移除或方案切换前自动备份，可从列表手动恢复。旧备份按容量上限自动清理。", "Back up before each mod install, removal, or profile switch. Restore any entry manually; older backups are pruned by the storage limit.");
        BackupLimitLabelTextBlock.Text = L("空间上限 (GB)", "Storage limit (GB)");
        RefreshBackupsButton.Content = L("刷新列表", "Refresh List");
        AutomationProperties.SetName(BackupLimitNumberBox, L("安装备份空间上限 GB", "Install backup storage limit in GB"));
        AutomationProperties.SetName(RefreshBackupsButton, RefreshBackupsButton.Content?.ToString() ?? string.Empty);
        _isApplyingBackupLimitSetting = true;
        BackupLimitNumberBox.Value = _installBackupLimitGb;
        _isApplyingBackupLimitSetting = false;
    }

    private async void OnBackupLimitValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
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

    private async void OnRefreshBackupsClicked(object sender, RoutedEventArgs e)
    {
        await RefreshInstallBackupListAsync();
    }

    private async Task RefreshInstallBackupListAsync()
    {
        BackupListPanel.Children.Clear();
        Directory.CreateDirectory(_modInstallBackupPath);
        List<InstallBackupDisplayItem> backups = await Task.Run(ReadInstallBackups);
        long totalBytes = backups.Sum(item => item.SizeBytes);
        BackupSummaryTextBlock.Text = L(
            $"共 {backups.Count} 份 · {FormatStorageSize(totalBytes)} / {_installBackupLimitGb:0.#} GB",
            $"{backups.Count} backups · {FormatStorageSize(totalBytes)} / {_installBackupLimitGb:0.#} GB");

        if (backups.Count == 0)
        {
            BackupListPanel.Children.Add(new TextBlock
            {
                Text = L("暂时没有安装备份。", "No install backups yet."),
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
                Text = L(
                    $"{backup.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {backup.ModNames} · {FormatStorageSize(backup.SizeBytes)}",
                    $"{backup.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {backup.ModNames} · {FormatStorageSize(backup.SizeBytes)}"),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(details);
            var restore = new Button
            {
                Content = L("恢复", "Restore"),
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Tag = backup.Path
            };
            Grid.SetColumn(restore, 1);
            AutomationProperties.SetName(restore, L($"恢复备份 {backup.Description}", $"Restore backup {backup.Description}"));
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
                    mods += L(" 等", " and more");
                }
                result.Add(new InstallBackupDisplayItem(directory, transaction.Description, transaction.CreatedAtUtc, string.IsNullOrWhiteSpace(mods) ? L("未记录 Mod", "No mod recorded") : mods, size));
            }
            catch
            {
            }
        }

        return result.OrderByDescending(item => item.CreatedAt).ToList();
    }

    private async void OnRestoreInstallBackupClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string backupPath } || !Directory.Exists(backupPath))
        {
            return;
        }

        if (!await ShowConfirmAsync(L("恢复此备份会替换对应 Mod 的当前文件。是否继续？", "Restoring this backup replaces the current files for the affected mods. Continue?"), L("恢复安装备份", "Restore Install Backup")))
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
            await ShowMessageAsync(L("恢复备份失败：", "Failed to restore backup: ") + ex.Message, L("恢复失败", "Restore Failed"));
        }
        finally
        {
            SetBusyState(false);
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
        string path = await CreateDiagnosticReportAsync();
        DiagnosticsStatusTextBlock.Text = L($"已导出：{Path.GetFileName(path)}", $"Exported: {Path.GetFileName(path)}");
        Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true });
    }

    private async void OnSubmitDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        string path = await CreateDiagnosticReportAsync();
        bool confirmed = await ShowConfirmAsync(
            L($"诊断报告已生成：{Path.GetFileName(path)}\n\n将打开 GitHub 新建 Issue 页面。请先检查内容，再自行附加此 ZIP 并提交。程序不会保存 GitHub 凭据。是否继续？",
              $"Diagnostic report created: {Path.GetFileName(path)}\n\nGitHub's new issue page will open. Review the content, optionally attach this ZIP, and submit it yourself. The app does not store GitHub credentials. Continue?"),
            L("提交错误报告", "Report an Issue"));
        if (!confirmed)
        {
            return;
        }

        string body = Uri.EscapeDataString($"App version: {AppVersion}\nOS: {Environment.OSVersion.VersionString}\nArchitecture: {RuntimeInformation.OSArchitecture}\n\nA sanitized diagnostic report was generated locally: {Path.GetFileName(path)}\nPlease attach it after reviewing the archive.");
        string url = $"{GitHubRepositoryUrl}/issues/new?title={Uri.EscapeDataString("Bug report " + AppVersion)}&body={body}";
        await Launcher.LaunchUriAsync(new Uri(url));
        Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(path)!, UseShellExecute = true });
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
        string[] names = ["startup.log", "app-errors.log", "update-agent.log", "update.log"];
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
        string targetDir)
    {
        Dictionary<string, IndexedModFolder> cached = _appDataStore.ReadModIndex(repositoryId, sourceDir)
            .ToDictionary(item => item.ModPath, StringComparer.OrdinalIgnoreCase);
        List<IndexedModFolder> refreshedIndex = [];
        List<FirstLevelFolderItem> loadedItems = [];
        string[] firstDirs = Directory.GetDirectories(sourceDir);
        Array.Sort(firstDirs, StringComparer.CurrentCultureIgnoreCase);
        int secondCount = 0;
        bool indexChanged = cached.Count == 0;

        foreach (string firstDir in firstDirs)
        {
            var firstItem = new FirstLevelFolderItem(firstDir);
            string[] secondDirs = Directory.GetDirectories(firstDir);
            Array.Sort(secondDirs, StringComparer.CurrentCultureIgnoreCase);
            foreach (string secondDir in secondDirs)
            {
                string stamp = Directory.GetLastWriteTimeUtc(secondDir).Ticks.ToString(CultureInfo.InvariantCulture);
                List<string> files;
                if (cached.TryGetValue(secondDir, out IndexedModFolder? cachedFolder)
                    && string.Equals(cachedFolder.FolderStampUtc, stamp, StringComparison.Ordinal)
                    && cachedFolder.Files.Count == cachedFolder.FileCount)
                {
                    files = cachedFolder.Files;
                }
                else
                {
                    files = GetFiles(secondDir);
                    indexChanged = true;
                }

                long bytes = 0;
                if (!cached.TryGetValue(secondDir, out IndexedModFolder? currentCached)
                    || !string.Equals(currentCached.FolderStampUtc, stamp, StringComparison.Ordinal))
                {
                    foreach (string file in files)
                    {
                        try
                        {
                            bytes += new FileInfo(file).Length;
                        }
                        catch
                        {
                        }
                    }
                }
                else
                {
                    bytes = currentCached.TotalBytes;
                }

                refreshedIndex.Add(new IndexedModFolder
                {
                    FirstLevelPath = firstDir,
                    ModPath = secondDir,
                    FolderStampUtc = stamp,
                    FileCount = files.Count,
                    TotalBytes = bytes,
                    Files = files
                });
                firstItem.Children.Add(new SecondLevelFolderItem(secondDir, files, GetFolderCopyState(targetDir, secondDir)));
                secondCount++;
            }
            loadedItems.Add(firstItem);
        }

        if (indexChanged || cached.Count != refreshedIndex.Count)
        {
            _appDataStore.ReplaceModIndex(repositoryId, sourceDir, refreshedIndex);
        }
        return (loadedItems, secondCount);
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
