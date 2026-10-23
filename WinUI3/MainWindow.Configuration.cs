using System.Text.Json;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.Core.Shortcuts;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static DateTimeOffset? TryParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : null;
    }

    private void LoadShellConfig()
    {
        _repositories.Clear();

        WorkspaceRepository fallbackRepository = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "默认仓库",
            SourcePath = SourceTextBox.Text ?? string.Empty,
            TargetPath = TargetTextBox.Text ?? string.Empty,
            LauncherPath = LauncherTextBox.Text ?? string.Empty,
            OnlineSourceSite = DefaultOnlineSourceSite,
            OnlineGameName = DefaultOnlineGameName,
            OnlineCategoryId = DefaultOnlineCategoryId,
            WikiUrl = EndfieldOfficialWikiUrl
        };

        if (!File.Exists(_shellConfigPath))
        {
            fallbackRepository = TryCreatePersistentDemoRepository() ?? fallbackRepository;
            _repositories.Add(fallbackRepository);
            _selectedRepositoryId = fallbackRepository.Id;
            SaveShellConfig();
            ApplySelectedRepositoryToInputs();
            return;
        }

        try
        {
            string shellJson = ReadConfigurationTextWithRecovery(_shellConfigPath, ConfigurationFileStore.IsJsonObject);
            BetaShellConfig? config = JsonSerializer.Deserialize<BetaShellConfig>(shellJson);
            if (config?.Repositories is { Count: > 0 })
            {
                _repositories.AddRange(config.Repositories.Where(repository => !string.IsNullOrWhiteSpace(repository.Id)));
            }

            if (_repositories.Count == 0)
            {
                _repositories.Add(fallbackRepository);
            }

            _selectedRepositoryId = _repositories.Any(repository => repository.Id == config?.SelectedRepositoryId)
                ? config?.SelectedRepositoryId
                : _repositories[0].Id;

            _currentPrimarySection = config?.CurrentPrimarySection switch
            {
                "repository" => PrimarySection.Repository,
                "online" => PrimarySection.Online,
                "updates" => PrimarySection.Updates,
                "settings" => PrimarySection.Settings,
                "shortcuts" => PrimarySection.Shortcuts,
                _ => PrimarySection.Dashboard
            };
            _onlinePreferences = config?.OnlinePreferences ?? new();
            _onlinePreferences.ConcurrentDownloads = Math.Clamp(_onlinePreferences.ConcurrentDownloads, 1, 4);
            _onlinePreferences.Authors ??= [];
            _onlinePreferences.SavedMods ??= [];
            _onlinePreferences.DownloadFolders ??= new(StringComparer.OrdinalIgnoreCase);
            _onlinePreferences.BrowseSessions ??= [];
            _onlinePreferences.RecentAuthors ??= [];
            _localLibraries = config?.LocalLibraries ?? new(StringComparer.OrdinalIgnoreCase);
            foreach (var library in _localLibraries.Values) library.Normalize();
            _onlineDownloadScheduler.Limit = _onlinePreferences.ConcurrentDownloads;
            _updateCheckInterval = AppSelfUpdateEnabled ? ParseUpdateCheckInterval(config?.UpdateCheckInterval) : UpdateCheckInterval.Manual;
            _repositoryWorkspaceView = config?.RepositoryWorkspaceView switch
            {
                "covers" => RepositoryWorkspaceView.Covers,
                "presets" => RepositoryWorkspaceView.Presets,
                _ => RepositoryWorkspaceView.List
            };
            _lastUpdateCheckUtc = TryParseDateTimeOffset(config?.LastUpdateCheckUtc);
            _modUpdateCheckInterval = ParseUpdateCheckInterval(config?.ModUpdateCheckInterval);
            _shortcutPanelHotkeyModifiers = config?.ShortcutPanelHotkeyModifiers is 3 or 5 or 6 ? config.ShortcutPanelHotkeyModifiers : 3;
            _shortcutPanelHotkeyKey = config?.ShortcutPanelHotkeyKey is >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7B ? config.ShortcutPanelHotkeyKey : 0x4B;
            _shortcutPanelHotkeyEnabled = config?.ShortcutPanelHotkeyEnabled ?? true;
            _shortcutInputMode = Enum.TryParse(config?.ShortcutInputMode, out ShortcutInputMode inputMode) ? inputMode : ShortcutInputMode.ScanCode;
            if (!Enum.IsDefined(_shortcutInputMode)) _shortcutInputMode = ShortcutInputMode.ScanCode;
            _shortcutOverlayOpacity = Math.Clamp(config?.ShortcutOverlayOpacity ?? 100, ShortcutOverlayWindow.MinimumOpacity, 100);
            _shortcutHideUnsupported = config?.ShortcutHideUnsupported ?? false;
            _overlayTextBrush.Color = ParseOverlayTextColor(config?.ShortcutOverlayTextColor);
            _lastModUpdateCheckUtc = TryParseDateTimeOffset(config?.LastModUpdateCheckUtc);
            _latestReleaseTag = config?.LatestReleaseTag;
            _latestReleaseTitle = config?.LatestReleaseTitle;
            _latestReleaseBody = config?.LatestReleaseBody;
            _latestReleasePublishedAt = config?.LatestReleasePublishedAt;
            _latestReleaseUrl = config?.LatestReleaseUrl;
            _latestReleasePackageUrl = config?.LatestReleasePackageUrl;
            _latestReleaseSha256Url = config?.LatestReleaseSha256Url;
            _checkAppUpdatesOnStartup = ApplicationReleasePolicy.CanCheckApplicationUpdates(config?.CheckAppUpdatesOnStartup ?? false);
            _endfieldBridgeOfferRevision = config?.EndfieldBridgeOfferRevision ?? 0;
            _installBackupLimitGb = Math.Clamp(config?.InstallBackupLimitGb ?? DefaultInstallBackupLimitGb, 0.5, 100);
            _reduceMotion = config?.ReduceMotion ?? false;
            _minimizeToTray = config?.MinimizeToTray ?? false;
            _interfaceDensity = string.Equals(config?.InterfaceDensity, "compact", StringComparison.OrdinalIgnoreCase)
                ? InterfaceDensity.Compact
                : InterfaceDensity.Comfortable;
            _onlineCardLayoutMode = string.Equals(config?.OnlineCardLayout, "grid", StringComparison.OrdinalIgnoreCase)
                ? OnlineCardLayoutMode.Grid
                : OnlineCardLayoutMode.List;
            if (config?.ConfigurationProfiles is { Count: > 0 })
            {
                _configurationProfiles.AddRange(config.ConfigurationProfiles
                    .Where(profile => !string.IsNullOrWhiteSpace(profile.Id)
                        && !string.IsNullOrWhiteSpace(profile.RepositoryId)));
            }
            if (config?.ModPersistentSlots is { Count: > 0 })
            {
                _modPersistentSlots.AddRange(config.ModPersistentSlots.Where(slot =>
                    !string.IsNullOrWhiteSpace(slot.Id)
                    && !string.IsNullOrWhiteSpace(slot.RepositoryId)
                    && !string.IsNullOrWhiteSpace(slot.ModRelativePath)
                    && slot.Values is { Count: > 0 }));
            }

            _selectedConfigurationProfileId = _configurationProfiles.Any(profile => profile.Id == config?.SelectedConfigurationProfileId)
                ? config?.SelectedConfigurationProfileId
                : null;
            _enableConflictDetection = config?.EnableConflictDetection ?? true;
            _lastInstallTransactionPath = !string.IsNullOrWhiteSpace(config?.LastInstallTransactionPath)
                && Directory.Exists(config.LastInstallTransactionPath)
                    ? config.LastInstallTransactionPath
                    : null;
            _savedWindowX = config?.WindowX;
            _savedWindowY = config?.WindowY;
            _savedWindowWidth = config?.WindowWidth;
            _savedWindowHeight = config?.WindowHeight;
        }
        catch
        {
            _repositories.Add(fallbackRepository);
            _selectedRepositoryId = fallbackRepository.Id;
            _currentPrimarySection = PrimarySection.Dashboard;
            _updateCheckInterval = UpdateCheckInterval.Manual;
            _lastUpdateCheckUtc = null;
            _modUpdateCheckInterval = UpdateCheckInterval.Manual;
            _lastModUpdateCheckUtc = null;
            _latestReleaseTag = null;
            _latestReleaseTitle = null;
            _latestReleaseBody = null;
            _latestReleasePublishedAt = null;
            _latestReleaseUrl = null;
            _latestReleasePackageUrl = null;
            _latestReleaseSha256Url = null;
            _checkAppUpdatesOnStartup = false;
            _installBackupLimitGb = DefaultInstallBackupLimitGb;
            _reduceMotion = false;
            _minimizeToTray = false;
            _interfaceDensity = InterfaceDensity.Comfortable;
            _onlineCardLayoutMode = OnlineCardLayoutMode.List;
            _savedWindowX = null;
            _savedWindowY = null;
            _savedWindowWidth = null;
            _savedWindowHeight = null;
        }

        if (UpgradeBuiltInRepositoryMetadata())
        {
            SaveShellConfig();
        }

        ApplySelectedRepositoryToInputs();
    }

    private bool UpgradeBuiltInRepositoryMetadata()
    {
        bool changed = false;
        foreach (WorkspaceRepository repository in _repositories)
        {
            repository.CharacterCategoryMappings ??= [];
            RepositoryPresetInfo? preset = BuiltInRepositoryPresets.FirstOrDefault(candidate =>
                IsRepositoryForPreset(repository, candidate));
            if (preset is null)
            {
                continue;
            }

            if (string.Equals(preset.Id, "genshin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(repository.OnlineCategoryId?.Trim(), "17510", StringComparison.OrdinalIgnoreCase))
            {
                repository.OnlineCategoryId = preset.CategoryId;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(repository.OnlineGameName))
            {
                repository.OnlineGameName = preset.DisplayNameEn;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(repository.WikiUrl))
            {
                repository.WikiUrl = preset.WikiUrl;
                changed = true;
            }
        }

        return changed;
    }

    private void SaveShellConfig(bool throwOnError = false)
    {
        try
        {
            CaptureOnlineBrowseSession();
            var config = new BetaShellConfig
            {
                CurrentPrimarySection = _currentPrimarySection.ToString().ToLowerInvariant(),
                OnlinePreferences = _onlinePreferences,
                LocalLibraries = _localLibraries,
                SelectedRepositoryId = _selectedRepositoryId,
                UpdateCheckInterval = _updateCheckInterval.ToString(),
                LastUpdateCheckUtc = _lastUpdateCheckUtc?.ToString("O"),
                ModUpdateCheckInterval = _modUpdateCheckInterval.ToString(),
                ShortcutPanelHotkeyModifiers = _shortcutPanelHotkeyModifiers,
                ShortcutPanelHotkeyKey = _shortcutPanelHotkeyKey,
                ShortcutPanelHotkeyEnabled = _shortcutPanelHotkeyEnabled,
                ShortcutInputMode = _shortcutInputMode.ToString(),
                ShortcutOverlayOpacity = _shortcutOverlayOpacity,
                ShortcutHideUnsupported = _shortcutHideUnsupported,
                ShortcutOverlayTextColor = OverlayTextColorValue,
                LastModUpdateCheckUtc = _lastModUpdateCheckUtc?.ToString("O"),
                LatestReleaseTag = _latestReleaseTag,
                LatestReleaseTitle = _latestReleaseTitle,
                LatestReleaseBody = _latestReleaseBody,
                LatestReleasePublishedAt = _latestReleasePublishedAt,
                LatestReleaseUrl = _latestReleaseUrl,
                LatestReleasePackageUrl = _latestReleasePackageUrl,
                LatestReleaseSha256Url = _latestReleaseSha256Url,
                CheckAppUpdatesOnStartup = _checkAppUpdatesOnStartup,
                EndfieldBridgeOfferRevision = _endfieldBridgeOfferRevision,
                InstallBackupLimitGb = _installBackupLimitGb,
                ReduceMotion = _reduceMotion,
                MinimizeToTray = _minimizeToTray,
                InterfaceDensity = _interfaceDensity == InterfaceDensity.Compact ? "compact" : "comfortable",
                OnlineCardLayout = _onlineCardLayoutMode == OnlineCardLayoutMode.Grid ? "grid" : "list",
                RepositoryWorkspaceView = _repositoryWorkspaceView.ToString().ToLowerInvariant(),
                SelectedConfigurationProfileId = _selectedConfigurationProfileId,
                EnableConflictDetection = _enableConflictDetection,
                LastInstallTransactionPath = _lastInstallTransactionPath,
                ConfigurationProfiles = [.. _configurationProfiles],
                ModPersistentSlots = [.. _modPersistentSlots],
                WindowX = _savedWindowX,
                WindowY = _savedWindowY,
                WindowWidth = _savedWindowWidth,
                WindowHeight = _savedWindowHeight,
                Repositories = [.. _repositories]
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            WriteConfigurationTextWithBackup(_shellConfigPath, json);
        }
        catch (Exception ex)
        {
            if (throwOnError) throw;
            LogApplicationIssue("Save repository configuration", ex);
            StatusTextBlock.Text = T("Configuration.FailedToSaveRepositoryConfig");
        }
    }

    private void RestoreWindowPlacement()
    {
        if (_savedWindowWidth is not int width || _savedWindowHeight is not int height)
        {
            return;
        }

        width = Math.Max(1100, width);
        height = Math.Max(700, height);
        int x = _savedWindowX ?? AppWindow.Position.X;
        int y = _savedWindowY ?? AppWindow.Position.Y;

        DisplayArea display = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Primary);
        RectInt32 workArea = display.WorkArea;
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        x = Math.Clamp(x, workArea.X, workArea.X + Math.Max(0, workArea.Width - width));
        y = Math.Clamp(y, workArea.Y, workArea.Y + Math.Max(0, workArea.Height - height));
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void SaveWindowPlacement()
    {
        _savedWindowX = AppWindow.Position.X;
        _savedWindowY = AppWindow.Position.Y;
        _savedWindowWidth = AppWindow.Size.Width;
        _savedWindowHeight = AppWindow.Size.Height;
    }

    private void LoadConfig()
    {
        _modBindings.Clear();
        _modLinks.Clear();
        _trackedModOrigins.Clear();

        if (!File.Exists(_configPath))
        {
            LoadDefaultShortcutTemplate();
            return;
        }

        try
        {
            LegacyConfig config = LegacyConfigFile.Parse(ReadConfigurationTextWithRecovery(_configPath, LegacyConfigFile.IsValid));
            if (config.SourceDirectory is not null) SourceTextBox.Text = config.SourceDirectory;
            if (config.TargetDirectory is not null) TargetTextBox.Text = config.TargetDirectory;
            if (config.LauncherPath is not null) LauncherTextBox.Text = config.LauncherPath;
            if (config.DarkTheme is bool darkTheme) _isDarkTheme = darkTheme;
            if (config.Language is not null)
            {
                _currentLanguage = string.Equals(config.Language, "en", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.EnUs
                    : AppLanguage.ZhCn;
            }

            foreach ((string modPath, List<ShortcutBinding> bindings) in config.Bindings) _modBindings[modPath] = bindings;
            foreach ((string modPath, string link) in config.ModLinks) _modLinks[modPath] = link;
            foreach ((string modPath, TrackedModOrigin origin) in config.ModOrigins) _trackedModOrigins[modPath] = origin;
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Load configuration", ex);
            StatusTextBlock.Text = T("Configuration.FailedToLoadConfigOlderConfig");
        }

        if (DeduplicateTrackedModOrigins())
        {
            SaveConfig();
        }

        LoadDefaultShortcutTemplate();
        LoadLinkForCurrentMod(null);
    }

    private bool SaveConfig()
    {
        try
        {
            SaveSelectedRepositoryFromInputs();

            var config = new LegacyConfig
            {
                SourceDirectory = SourceTextBox.Text ?? string.Empty,
                TargetDirectory = TargetTextBox.Text ?? string.Empty,
                LauncherPath = LauncherTextBox.Text ?? string.Empty,
                DarkTheme = _isDarkTheme,
                Language = _currentLanguage == AppLanguage.EnUs ? "en" : "zh"
            };
            foreach ((string modPath, List<ShortcutBinding> bindings) in _modBindings) config.Bindings[modPath] = bindings;
            foreach ((string modPath, string link) in _modLinks) config.ModLinks[modPath] = link;
            foreach ((string modPath, TrackedModOrigin origin) in _trackedModOrigins) config.ModOrigins[modPath] = origin;

            WriteConfigurationTextWithBackup(_configPath, LegacyConfigFile.Format(config));
            SaveShellConfig();
            return true;
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Save configuration", ex);
            StatusTextBlock.Text = T("Configuration.FailedToSaveConfigButThe");
            return false;
        }
    }

    private string ReadConfigurationTextWithRecovery(string filePath, Func<string, bool> validator)
    {
        ConfigurationReadResult result = _configurationFiles.Read(filePath, validator);
        if (result.RestoredFromBackup)
        {
            StatusTextBlock.Text = T("Configuration.WasDamagedAndTheLatestBackup", Path.GetFileName(filePath));
        }

        return result.Text;
    }

    private void WriteConfigurationTextWithBackup(string filePath, string content)
    {
        _configurationFiles.Write(filePath, content);
    }
}
