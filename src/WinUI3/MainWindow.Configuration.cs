using System.Text.Json;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Repository;
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
        _services.Repositories.Items.Clear();

        // A first install starts without a game and asks for one. Earlier installs without this file keep what they always got.
        WorkspaceRepository fallbackRepository = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = T("Configuration.DefaultRepositoryName"),
            SourcePath = Paths.Source,
            TargetPath = Paths.Target,
            LauncherPath = Paths.Launcher,
            OnlineSourceSite = DefaultOnlineSourceSite,
            OnlineGameName = _firstInstall ? string.Empty : DefaultOnlineGameName,
            OnlineCategoryId = _firstInstall ? string.Empty : DefaultOnlineCategoryId,
            WikiUrl = _firstInstall ? string.Empty : EndfieldOfficialWikiUrl
        };

        if (!File.Exists(_services.ShellConfig.ShellConfigPath))
        {
            fallbackRepository = TryCreatePersistentDemoRepository() ?? fallbackRepository;
            _services.Repositories.Items.Add(fallbackRepository);
            _services.Repositories.SelectedId = fallbackRepository.Id;
            _gettingStartedTips = GettingStartedTips.Load(_firstInstall, savedPending: false);
            _whatsNewSeenVersion = WhatsNewPolicy.SeenAfterLoad(null, WhatsNewFeatureSet, _firstInstall);
            SaveShellConfig();
            ApplySelectedRepositoryToInputs();
            return;
        }

        try
        {
            string shellJson = ReadConfigurationTextWithRecovery(_services.ShellConfig.ShellConfigPath, ConfigurationFileStore.IsJsonObject);
            BetaShellConfig? config = JsonSerializer.Deserialize<BetaShellConfig>(shellJson);
            _services.ShellConfig.ExtensionData = config?.ExtensionData;
            if (config is not null) RepositoryGameMigration.Apply(config, DefaultOnlineGameName, DefaultOnlineCategoryId);
            if (config is not null) RemoveOrphanPresetPreviews(config);
            if (config?.Repositories is { Count: > 0 })
            {
                _services.Repositories.Items.AddRange(config.Repositories.Where(repository => !string.IsNullOrWhiteSpace(repository.Id)));
            }

            if (_services.Repositories.Items.Count == 0)
            {
                _services.Repositories.Items.Add(fallbackRepository);
            }

            _services.Repositories.SelectedId = _services.Repositories.Items.Any(repository => repository.Id == config?.SelectedRepositoryId)
                ? config?.SelectedRepositoryId
                : _services.Repositories.Items[0].Id;

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
            _loadedUpdateCheckInterval = config?.UpdateCheckInterval;
            _updateCheckInterval = AppSelfUpdateEnabled ? ParseUpdateCheckInterval(_loadedUpdateCheckInterval) : UpdateCheckInterval.Manual;
            _navigationCollapsed = config?.NavigationCollapsed ?? false;
            _modList.View = config?.RepositoryWorkspaceView switch
            {
                "covers" => RepositoryWorkspaceView.Covers,
                "presets" => RepositoryWorkspaceView.Presets,
                _ => RepositoryWorkspaceView.List
            };
            _lastUpdateCheckUtc = TryParseDateTimeOffset(config?.LastUpdateCheckUtc);
            // Restoring the view is not a change to save.
            _restoringUpdatesView = true;
            _services.Updates.View = UpdatesViewModel.ParseView(config?.UpdatesView);
            _restoringUpdatesView = false;
            _modUpdateCheckInterval = ParseUpdateCheckInterval(config?.ModUpdateCheckInterval);
            _shortcuts.LoadSettings(config?.ShortcutInputMode, config?.ShortcutOverlayOpacity, config?.ShortcutHideUnsupported,
                config?.ShortcutPanelHotkeyEnabled ?? true, config?.ShortcutPanelHotkeyModifiers ?? ShortcutPageViewModel.DefaultHotkeyModifiers,
                config?.ShortcutPanelHotkeyKey ?? ShortcutPageViewModel.DefaultHotkeyKey);
            _shortcuts.OverlayElementOpacity = OverlayAppearancePolicy.ClampElementOpacity(config?.ShortcutOverlayElementOpacity);
            _shortcuts.OverlaySlotSwitchEnabled = config?.OverlaySlotSwitchEnabled ?? false;
            _shortcuts.CapturePresetPreviews = config?.CapturePresetPreviews ?? false;
            _shortcuts.OverlayCompactSlots = config?.OverlayCompactSlots ?? false;
            _shortcuts.OverlayCompactView = config?.OverlayCompactView ?? false;
            _shortcuts.KeepMenuStateVariables = config?.KeepMenuStateVariables ?? false;
            _loaderAutoSaveOriginals = new Dictionary<string, int>(config?.LoaderAutoSaveOriginals ?? [], StringComparer.OrdinalIgnoreCase);
            _presetPreviewFrameLocked = config?.PresetPreviewFrameLocked ?? false;
            _presetPreviewDefaultRegion = config?.PresetPreviewDefaultRegion is { IsValid: true } region ? region : null;
            _shortcuts.OverlayTextColor = ShortcutOverlayController.FormatTextColor(ShortcutOverlayController.ParseTextColor(config?.ShortcutOverlayTextColor));
            _shortcuts.OverlayAutoContrast = config?.OverlayAutoContrast ?? false;
            _lastModUpdateCheckUtc = TryParseDateTimeOffset(config?.LastModUpdateCheckUtc);
            _latestReleaseTag = config?.LatestReleaseTag;
            _latestReleaseTitle = config?.LatestReleaseTitle;
            _latestReleaseBody = config?.LatestReleaseBody;
            _latestReleasePublishedAt = config?.LatestReleasePublishedAt;
            _latestReleaseUrl = config?.LatestReleaseUrl;
            _latestReleasePackageUrl = config?.LatestReleasePackageUrl;
            _latestReleaseSha256Url = config?.LatestReleaseSha256Url;
            _loadedCheckAppUpdatesOnStartup = config?.CheckAppUpdatesOnStartup;
            _checkAppUpdatesOnStartup = ApplicationReleasePolicy.CanCheckApplicationUpdates(_loadedCheckAppUpdatesOnStartup ?? false);
            _installBackupLimitGb = Math.Clamp(config?.InstallBackupLimitGb ?? DefaultInstallBackupLimitGb, 0.5, 100);
            _reduceMotion = config?.ReduceMotion ?? false;
            _storedThemePreference = config?.ThemePreference;
            _minimizeToTray = config?.MinimizeToTray ?? false;
            _showTitleBarSearch = config?.ShowTitleBarSearch ?? true;
            _interfaceDensity = string.Equals(config?.InterfaceDensity, "compact", StringComparison.OrdinalIgnoreCase)
                ? InterfaceDensity.Compact
                : InterfaceDensity.Comfortable;
            _onlinePage.CardLayoutMode = string.Equals(config?.OnlineCardLayout, "grid", StringComparison.OrdinalIgnoreCase)
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
            _defaultSlotSeeds.Clear();
            _defaultSlotsBackfilled.Clear();
            _defaultSlotsBackfilled.UnionWith(config?.DefaultSlotsBackfilled ?? []);
            foreach ((string repositoryId, List<string> mods) in config?.DefaultSlotSeeds ?? [])
            {
                _defaultSlotSeeds[repositoryId] = new HashSet<string>(mods ?? [], StringComparer.OrdinalIgnoreCase);
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
            _savedWindowMaximized = config?.WindowMaximized ?? false;
            _whatsNewSeenVersion = WhatsNewPolicy.SeenAfterLoad(config?.WhatsNewSeenVersion, WhatsNewFeatureSet, _firstInstall);
            lock (_copyDeployments)
            {
                _copyDeployments.Clear();
                _copyDeployments.AddRange(config?.CopyDeployments ?? []);
            }
            _gettingStartedTips = GettingStartedTips.Load(_firstInstall, config?.GettingStartedTipsPending ?? false);
        }
        catch
        {
            _gettingStartedTips = GettingStartedTips.Load(_firstInstall, savedPending: false);
            _services.Repositories.Items.Add(fallbackRepository);
            _services.Repositories.SelectedId = fallbackRepository.Id;
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
            _storedThemePreference = null;
            _minimizeToTray = false;
            _interfaceDensity = InterfaceDensity.Comfortable;
            _onlinePage.CardLayoutMode = OnlineCardLayoutMode.List;
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
        foreach (WorkspaceRepository repository in _services.Repositories.Items)
        {
            repository.CharacterCategoryMappings ??= [];
            repository.CharacterAvatarBindings ??= [];
            repository.CharacterSelectionOverrides ??= [];
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
                SelectedRepositoryId = _services.Repositories.SelectedId,
                UpdateCheckInterval = ApplicationReleasePolicy.UpdateSettingToSave(_updateCheckInterval.ToString(), _loadedUpdateCheckInterval),
                LastUpdateCheckUtc = _lastUpdateCheckUtc?.ToString("O"),
                ModUpdateCheckInterval = _modUpdateCheckInterval.ToString(),
                ShortcutPanelHotkeyModifiers = _shortcuts.HotkeyModifiers,
                ShortcutPanelHotkeyKey = _shortcuts.HotkeyKey,
                ShortcutPanelHotkeyEnabled = _shortcuts.HotkeyEnabled,
                ShortcutInputMode = _shortcuts.InputMode.ToString(),
                ShortcutOverlayOpacity = _shortcuts.OverlayOpacity,
                ShortcutOverlayElementOpacity = _shortcuts.OverlayElementOpacity,
                ShortcutHideUnsupported = _shortcuts.HideUnsupported,
                OverlaySlotSwitchEnabled = _shortcuts.OverlaySlotSwitchEnabled,
                CapturePresetPreviews = _shortcuts.CapturePresetPreviews,
                OverlayCompactSlots = _shortcuts.OverlayCompactSlots,
                OverlayCompactView = _shortcuts.OverlayCompactView,
                OverlayAutoContrast = _shortcuts.OverlayAutoContrast,
                KeepMenuStateVariables = _shortcuts.KeepMenuStateVariables,
                LoaderAutoSaveOriginals = _loaderAutoSaveOriginals.Count == 0 ? null : new Dictionary<string, int>(_loaderAutoSaveOriginals),
                PresetPreviewFrameLocked = _presetPreviewFrameLocked,
                PresetPreviewDefaultRegion = _presetPreviewDefaultRegion,
                ShortcutOverlayTextColor = _shortcuts.OverlayTextColor,
                LastModUpdateCheckUtc = _lastModUpdateCheckUtc?.ToString("O"),
                LatestReleaseTag = _latestReleaseTag,
                LatestReleaseTitle = _latestReleaseTitle,
                LatestReleaseBody = _latestReleaseBody,
                LatestReleasePublishedAt = _latestReleasePublishedAt,
                LatestReleaseUrl = _latestReleaseUrl,
                LatestReleasePackageUrl = _latestReleasePackageUrl,
                LatestReleaseSha256Url = _latestReleaseSha256Url,
                CheckAppUpdatesOnStartup = ApplicationReleasePolicy.UpdateSettingToSave<bool?>(_checkAppUpdatesOnStartup, _loadedCheckAppUpdatesOnStartup),
                InstallBackupLimitGb = _installBackupLimitGb,
                ReduceMotion = _reduceMotion,
                ThemePreference = ThemePreferences.Format(_themePreference),
                MinimizeToTray = _minimizeToTray,
                ShowTitleBarSearch = _showTitleBarSearch,
                NavigationCollapsed = _navigationCollapsed,
                InterfaceDensity = _interfaceDensity == InterfaceDensity.Compact ? "compact" : "comfortable",
                OnlineCardLayout = _onlinePage.CardLayoutMode == OnlineCardLayoutMode.Grid ? "grid" : "list",
                RepositoryWorkspaceView = _modList.View.ToString().ToLowerInvariant(),
                UpdatesView = UpdatesViewModel.FormatView(_services.Updates.View),
                SelectedConfigurationProfileId = _selectedConfigurationProfileId,
                EnableConflictDetection = _enableConflictDetection,
                LastInstallTransactionPath = _lastInstallTransactionPath,
                ConfigurationProfiles = [.. _configurationProfiles],
                ModPersistentSlots = [.. _modPersistentSlots],
                CopyDeployments = CopyRecordsSnapshot(),
                DefaultSlotSeeds = _defaultSlotSeeds.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToList()),
                DefaultSlotsBackfilled = [.. _defaultSlotsBackfilled.Order(StringComparer.Ordinal)],
                WindowX = _savedWindowX,
                WindowY = _savedWindowY,
                WindowWidth = _savedWindowWidth,
                WindowHeight = _savedWindowHeight,
                WindowMaximized = _savedWindowMaximized,
                WhatsNewSeenVersion = _whatsNewSeenVersion,
                Repositories = [.. _services.Repositories.Items],
                RepositoryGamesExplicit = true,
                GettingStartedTipsPending = _gettingStartedTips.IsPending,
                ExtensionData = _services.ShellConfig.ExtensionData
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            WriteConfigurationTextWithBackup(_services.ShellConfig.ShellConfigPath, json);
        }
        catch (Exception ex)
        {
            if (throwOnError) throw;
            LogApplicationIssue("Save repository configuration", ex);
            StatusTextBlock.Text = T("Configuration.FailedToSaveRepositoryConfig");
        }
    }

    // Normal bounds come from WindowPlacementPolicy: saved bounds that fill the screen (for example
    // saved while maximized by earlier versions) or no bounds become a share of the work area.
    private void RestoreWindowPlacement()
    {
        WindowRect? saved = _savedWindowWidth is int width && _savedWindowHeight is int height
            ? new WindowRect(_savedWindowX ?? AppWindow.Position.X, _savedWindowY ?? AppWindow.Position.Y, width, height)
            : null;
        RectInt32 workArea = DisplayArea.GetFromPoint(
            new PointInt32(saved?.X ?? AppWindow.Position.X, saved?.Y ?? AppWindow.Position.Y), DisplayAreaFallback.Primary).WorkArea;
        WindowRect normal = WindowPlacementPolicy.Normal(saved, new WindowRect(workArea.X, workArea.Y, workArea.Width, workArea.Height));
        AppWindow.MoveAndResize(new RectInt32(normal.X, normal.Y, normal.Width, normal.Height));
        if (_savedWindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
        _lastPresenterState = (AppWindow.Presenter as OverlappedPresenter)?.State;
        AppWindow.Changed -= OnWindowPlacementChanged;
        AppWindow.Changed += OnWindowPlacementChanged;
    }

    // Keep the last normal bounds: a maximized or minimized window only records its state.
    private void SaveWindowPlacement()
    {
        OverlappedPresenterState? state = (AppWindow.Presenter as OverlappedPresenter)?.State;
        if (state == OverlappedPresenterState.Minimized)
        {
            return;
        }

        _savedWindowMaximized = state == OverlappedPresenterState.Maximized;
        if (_savedWindowMaximized)
        {
            return;
        }

        _savedWindowX = AppWindow.Position.X;
        _savedWindowY = AppWindow.Position.Y;
        _savedWindowWidth = AppWindow.Size.Width;
        _savedWindowHeight = AppWindow.Size.Height;
    }

    // Leaving maximized for a normal window that still fills the screen shrinks it to the share of
    // the work area, so restoring visibly changes the window.
    private void OnWindowPlacementChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        try
        {
            if (!args.DidPresenterChange && !args.DidSizeChange) return;
            OverlappedPresenterState? state = (sender.Presenter as OverlappedPresenter)?.State;
            OverlappedPresenterState? previous = _lastPresenterState;
            _lastPresenterState = state;
            if (previous != OverlappedPresenterState.Maximized || state != OverlappedPresenterState.Restored) return;

            RectInt32 workArea = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Primary).WorkArea;
            var work = new WindowRect(workArea.X, workArea.Y, workArea.Width, workArea.Height);
            var window = new WindowRect(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
            if (WindowPlacementPolicy.FillsWorkArea(window, work))
            {
                WindowRect normal = WindowPlacementPolicy.Proportional(work);
                sender.MoveAndResize(new RectInt32(normal.X, normal.Y, normal.Width, normal.Height));
            }
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Window placement", ex);
        }
    }

    private void LoadConfig()
    {
        _modBindings.Clear();
        _modLinks.Clear();
        _trackedModOrigins.Clear();

        if (!File.Exists(_services.ShellConfig.ConfigPath))
        {
            LoadDefaultShortcutTemplate();
            return;
        }

        try
        {
            LegacyConfig config = LegacyConfigFile.Parse(ReadConfigurationTextWithRecovery(_services.ShellConfig.ConfigPath, LegacyConfigFile.IsValid));
            if (config.SourceDirectory is not null) Paths.Source = config.SourceDirectory;
            if (config.TargetDirectory is not null) Paths.Target = config.TargetDirectory;
            if (config.LauncherPath is not null) Paths.Launcher = config.LauncherPath;
            _legacyDarkTheme = config.DarkTheme;
            if (config.Language is not null)
            {
                _services.Language.Load(UiLanguagePolicy.FromConfig(config.Language, UiText.Languages) ?? _services.Language.Current);
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
            WriteLegacyConfig();
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

    // config.ini: paths, language, and the Mod bindings, links and origins. Throws when it cannot be written.
    private void WriteLegacyConfig()
    {
        var config = new LegacyConfig
        {
            SourceDirectory = Paths.Source,
            TargetDirectory = Paths.Target,
            LauncherPath = Paths.Launcher,
            DarkTheme = _services.Appearance.IsDark,
            Language = UiLanguagePolicy.ToConfig(_services.Language.Current)
        };
        foreach ((string modPath, List<ShortcutBinding> bindings) in _modBindings) config.Bindings[modPath] = bindings;
        foreach ((string modPath, string link) in _modLinks) config.ModLinks[modPath] = link;
        foreach ((string modPath, TrackedModOrigin origin) in _trackedModOrigins) config.ModOrigins[modPath] = origin;

        WriteConfigurationTextWithBackup(_services.ShellConfig.ConfigPath, LegacyConfigFile.Format(config));
    }

    private string ReadConfigurationTextWithRecovery(string filePath, Func<string, bool> validator)
    {
        string text = _services.ShellConfig.Read(filePath, validator, out bool restoredFromBackup);
        if (restoredFromBackup)
        {
            StatusTextBlock.Text = T("Configuration.WasDamagedAndTheLatestBackup", Path.GetFileName(filePath));
        }

        return text;
    }

    private void WriteConfigurationTextWithBackup(string filePath, string content)
    {
        _services.ShellConfig.Write(filePath, content);
    }
}
