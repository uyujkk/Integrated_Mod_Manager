using System.Diagnostics;
using System.IO.Compression;
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
    // As read from the config; a build without self-update writes these back unchanged.
    private bool? _loadedCheckAppUpdatesOnStartup;
    private string? _loadedUpdateCheckInterval;
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
        _currentPrimarySection = section;
        ApplyShellState(refreshRepository: section == PrimarySection.Repository);
        SaveShellConfig();
    }

    private bool IsFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_onlineView._onlineCharacterCatalogKey))
        {
            return false;
        }

        return _favoriteCharacterKeys.Contains(AppDataStore.BuildFavoriteKey(_onlineView._onlineCharacterCatalogKey, characterKey));
    }

    private void ToggleFavoriteCharacter(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) || string.IsNullOrWhiteSpace(_onlineView._onlineCharacterCatalogKey))
        {
            return;
        }

        string favoriteKey = AppDataStore.BuildFavoriteKey(_onlineView._onlineCharacterCatalogKey, characterKey);
        bool isFavorite = !_favoriteCharacterKeys.Contains(favoriteKey);
        if (isFavorite)
        {
            _favoriteCharacterKeys.Add(favoriteKey);
        }
        else
        {
            _favoriteCharacterKeys.Remove(favoriteKey);
        }

        _appDataStore.SetFavorite(_onlineView._onlineCharacterCatalogKey, characterKey, isFavorite);
        _onlineView._onlineCharacterStripSignature = null;
        _onlineView.RefreshOnlineCharacterAvatarStrip();
        _onlineView.PopulateOnlineCharacterOptions();
        ShowAppNotification(
                isFavorite ? T("ProductivityFeatures.CharacterFavorited") : T("ProductivityFeatures.CharacterUnfavorited"),
                InfoBarSeverity.Informational);
    }

    private Task<LatestReleaseInfo> FetchLatestReleaseInfoAsync(CancellationToken cancellationToken = default)
        => ApplicationUpdateClient.FetchLatestReleaseAsync(_httpClient, GitHubLatestReleaseApiUrl, GitHubRepositoryUrl, cancellationToken);

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
                    SettingsView.ReportDownload(progress, T("ProductivityFeatures.DownloadingUpdate", progress, FormatStorageSize(downloaded), FormatStorageSize(total)));
                }
                else
                {
                    SettingsView.ReportDownload(null, downloaded > 0 ? T("ProductivityFeatures.Downloaded", FormatStorageSize(downloaded)) : null);
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
        UpdatesView.BackupLimitGb = _installBackupLimitGb;
        _isApplyingBackupLimitSetting = false;
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
        Directory.CreateDirectory(_services.Deployment.InstallBackupPath);
        List<InstallBackupDisplayItem> backups = await Task.Run(ReadInstallBackups);
        long totalBytes = backups.Sum(item => item.SizeBytes);
        UpdatesView.SetBackups(
            T("ProductivityFeatures.BackupsGB", backups.Count, FormatStorageSize(totalBytes), _installBackupLimitGb),
            [.. backups.Take(30).Select(backup => new InstallBackupRow(backup.Path, backup.Description,
                T("ProductivityFeatures.Text", backup.CreatedAt.ToLocalTime(), backup.ModNames, FormatStorageSize(backup.SizeBytes))))]);
    }

    private List<InstallBackupDisplayItem> ReadInstallBackups()
    {
        if (!Directory.Exists(_services.Deployment.InstallBackupPath))
        {
            return [];
        }

        List<InstallBackupDisplayItem> result = [];
        foreach (string directory in Directory.EnumerateDirectories(_services.Deployment.InstallBackupPath))
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
            catch (Exception ex)
            {
                LogApplicationIssue("Read install backup", ex);
            }
        }

        return result.OrderByDescending(item => item.CreatedAt).ToList();
    }

    private async Task RestoreInstallBackupAsync(string backupPath)
    {
        try
        {
            if (!Directory.Exists(backupPath))
            {
                return;
            }

            if (!await ShowDangerConfirmAsync(T("ProductivityFeatures.RestoringThisBackupReplacesTheCurrent"), T("ProductivityFeatures.RestoreInstallBackup"), T("Dialogs.Restore")))
            {
                return;
            }

            _workspaceView.SetBusyState(true);
            try
            {
                await RollbackInstallTransactionAsync(backupPath, clearLastTransaction: true);
                await _workspaceView.RefreshListsAsync();
                await RefreshInstallBackupListAsync();
                ShowAppNotification(T("ProductivityFeatures.BackupRestored"));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(T("ProductivityFeatures.FailedToRestoreBackup") + ex.Message, T("ProductivityFeatures.RestoreFailed"));
            }
            finally
            {
                _workspaceView.SetBusyState(false);
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
            if (!Directory.Exists(_services.Deployment.InstallBackupPath))
            {
                return;
            }

            string root = Path.GetFullPath(_services.Deployment.InstallBackupPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            List<(DirectoryInfo Directory, long Size)> directories = new DirectoryInfo(_services.Deployment.InstallBackupPath).EnumerateDirectories()
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
            SettingsView.DiagnosticsStatus = T("ProductivityFeatures.Exported", Path.GetFileName(path));
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
                T("ProductivityFeatures.ReportAnIssue"), T("Dialogs.OpenIssuePage"));
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
        summary.AppendLine($"Language: {_services.Language.Current}");
        summary.AppendLine($"Theme: {(_services.Appearance.IsDark ? "Dark" : "Light")}");
        summary.AppendLine($"Repository count: {_services.Repositories.Items.Count}");
        summary.AppendLine($"Source paths configured: {_services.Repositories.Items.Count(item => !string.IsNullOrWhiteSpace(item.SourcePath))}");
        summary.AppendLine($"Target paths configured: {_services.Repositories.Items.Count(item => !string.IsNullOrWhiteSpace(item.TargetPath))}");
        summary.AppendLine($"SQLite: {_appDataStore.GetHealthSummary()}");
        WriteZipText(archive, "report.txt", summary.ToString());

        var sanitizedConfig = new
        {
            AppVersion,
            Language = _services.Language.Current,
            Theme = _services.Appearance.IsDark ? "Dark" : "Light",
            _checkAppUpdatesOnStartup,
            UpdateInterval = _updateCheckInterval.ToString(),
            _installBackupLimitGb,
            ReduceMotion = _reduceMotion,
            Density = _interfaceDensity.ToString(),
            Repositories = _services.Repositories.Items.Select((repository, index) => new
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
            catch (Exception ex)
            {
                LogApplicationIssue("Add log to diagnostic report", ex);
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
                    mod.ModPath, mod.Files.ToList(), _workspaceView.GetFolderCopyState(targetDir, mod.ModPath)));
            }
            loadedItems.Add(firstItem);
        }
        return (loadedItems, scan.ModCount);
    }

    private sealed record InstallBackupDisplayItem(
        string Path,
        string Description,
        DateTimeOffset CreatedAt,
        string ModNames,
        long SizeBytes);
}
