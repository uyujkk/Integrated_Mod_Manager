using System.Text.Json;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.Core.Updates;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Loading and saving the two settings files (stage J finish, step 8a, moved from the window unchanged): beta-shell.json
/// (repositories, the shell's settings, every service's saved state) and config.ini (paths, language, and the Mods'
/// shortcut bindings, links and tracked origins), each with its backup and recovery.
/// </summary>
internal sealed class ConfigurationService(AppServices services)
{
    /// <summary>The game a new repository gets on an install from before the game choice.</summary>
    public const string DefaultGameName = "Arknights: Endfield";

    private readonly AppServices _services = services;
    private Action<string, Exception> _log = (_, _) => { };
    private Action _beforeSave = () => { };
    private Action _applySelectedRepositoryToInputs = () => { };
    private Action _saveSelectedRepositoryFromInputs = () => { };
    private Dictionary<string, List<ShortcutBinding>> _bindings = [];
    private Dictionary<string, string> _links = [];
    private string _whatsNewFeatureSet = string.Empty;

    /// <summary>
    /// Called once by the window: the error log, what to capture before saving (the Online page's browse session), the
    /// selected repository to and from the path boxes, the Mods' shortcut bindings and links, and the what's-new set.
    /// </summary>
    public void Attach(Action<string, Exception> log, Action beforeSave, Action applySelectedRepositoryToInputs, Action saveSelectedRepositoryFromInputs,
        Dictionary<string, List<ShortcutBinding>> bindings, Dictionary<string, string> links, string whatsNewFeatureSet)
    {
        _log = log;
        _beforeSave = beforeSave;
        _applySelectedRepositoryToInputs = applySelectedRepositoryToInputs;
        _saveSelectedRepositoryFromInputs = saveSelectedRepositoryFromInputs;
        _bindings = bindings;
        _links = links;
        _whatsNewFeatureSet = whatsNewFeatureSet;
    }

    /// <summary>Neither settings file existed when the app started: a first install.</summary>
    public bool FirstInstall { get; set; }

    private ShellSettings Settings => _services.ShellSettings;

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    public static DateTimeOffset? TryParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : null;
    }

    public void LoadShell()
    {
        _services.Repositories.Items.Clear();

        // A first install starts without a game and asks for one. Earlier installs without this file keep what they always got.
        WorkspaceRepository fallbackRepository = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = T("Configuration.DefaultRepositoryName"),
            SourcePath = _services.Paths.Source,
            TargetPath = _services.Paths.Target,
            LauncherPath = _services.Paths.Launcher,
            OnlineSourceSite = OnlineSourceRules.DefaultSourceSite,
            OnlineGameName = FirstInstall ? string.Empty : DefaultGameName,
            OnlineCategoryId = FirstInstall ? string.Empty : OnlineSourceRules.DefaultCategoryId,
            WikiUrl = FirstInstall ? string.Empty : OnlineSourceRules.EndfieldOfficialWikiUrl
        };

        if (!File.Exists(_services.ShellConfig.ShellConfigPath))
        {
            fallbackRepository = SlotService.TryCreatePersistentDemoRepository() ?? fallbackRepository;
            _services.Repositories.Items.Add(fallbackRepository);
            _services.Repositories.SelectedId = fallbackRepository.Id;
            Settings.GettingStarted = GettingStartedChecklist.Load(FirstInstall, savedPending: false);
            Settings.WhatsNewSeenVersion = WhatsNewPolicy.SeenAfterLoad(null, _whatsNewFeatureSet, FirstInstall);
            SaveShell();
            _applySelectedRepositoryToInputs();
            return;
        }

        try
        {
            string shellJson = ReadConfigurationTextWithRecovery(_services.ShellConfig.ShellConfigPath, ConfigurationFileStore.IsJsonObject);
            BetaShellConfig? config = JsonSerializer.Deserialize<BetaShellConfig>(shellJson);
            _services.ShellConfig.ExtensionData = config?.ExtensionData;
            if (config is not null) RepositoryGameMigration.Apply(config, DefaultGameName, OnlineSourceRules.DefaultCategoryId);
            if (config is not null) _services.PresetPreviews.RemoveOrphanPresetPreviews(config);
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

            Settings.CurrentSection = config?.CurrentPrimarySection switch
            {
                "repository" => PrimarySection.Repository,
                "online" => PrimarySection.Online,
                "updates" => PrimarySection.Updates,
                "settings" => PrimarySection.Settings,
                "shortcuts" => PrimarySection.Shortcuts,
                _ => PrimarySection.Dashboard
            };
            _services.Online.Preferences = config?.OnlinePreferences ?? new();
            _services.Online.Preferences.ConcurrentDownloads = Math.Clamp(_services.Online.Preferences.ConcurrentDownloads, 1, 4);
            _services.Online.Preferences.Authors ??= [];
            _services.Online.Preferences.SavedMods ??= [];
            _services.Online.Preferences.DownloadFolders ??= new(StringComparer.OrdinalIgnoreCase);
            _services.Online.Preferences.BrowseSessions ??= [];
            _services.Online.Preferences.RecentAuthors ??= [];
            _services.Workspace.LocalLibraries = config?.LocalLibraries ?? new(StringComparer.OrdinalIgnoreCase);
            foreach (var library in _services.Workspace.LocalLibraries.Values) library.Normalize();
            _services.Downloads.Scheduler.Limit = _services.Online.Preferences.ConcurrentDownloads;
            Settings.LoadedUpdateCheckInterval = config?.UpdateCheckInterval;
            Settings.UpdateCheckInterval = ApplicationReleasePolicy.ApplicationSelfUpdateEnabled ? ParseUpdateCheckInterval(Settings.LoadedUpdateCheckInterval) : UpdateCheckInterval.Manual;
            Settings.NavigationCollapsed = config?.NavigationCollapsed ?? false;
            _services.Workspace.ModList.View = config?.RepositoryWorkspaceView switch
            {
                "covers" => RepositoryWorkspaceView.Covers,
                "presets" => RepositoryWorkspaceView.Presets,
                _ => RepositoryWorkspaceView.List
            };
            _services.Workspace.ModList.CharacterPaneWidth = config?.WorkspaceCharacterWidth;
            _services.Workspace.ModList.DetailPaneWidth = config?.WorkspaceDetailWidth;
            Settings.LastUpdateCheckUtc = TryParseDateTimeOffset(config?.LastUpdateCheckUtc);
            // Restoring the view is not a change to save.
            Settings.RestoringUpdatesView = true;
            _services.Updates.View = UpdatesViewModel.ParseView(config?.UpdatesView);
            Settings.RestoringUpdatesView = false;
            Settings.ModUpdateCheckInterval = ParseUpdateCheckInterval(config?.ModUpdateCheckInterval);
            _services.Shortcuts.LoadSettings(config?.ShortcutInputMode, config?.ShortcutOverlayOpacity, config?.ShortcutHideUnsupported,
                config?.ShortcutPanelHotkeyEnabled ?? true, config?.ShortcutPanelHotkeyModifiers ?? ShortcutPageViewModel.DefaultHotkeyModifiers,
                config?.ShortcutPanelHotkeyKey ?? ShortcutPageViewModel.DefaultHotkeyKey);
            _services.Shortcuts.OverlayElementOpacity = OverlayAppearancePolicy.ClampElementOpacity(config?.ShortcutOverlayElementOpacity);
            _services.Shortcuts.OverlaySlotSwitchEnabled = config?.OverlaySlotSwitchEnabled ?? false;
            _services.Shortcuts.CapturePresetPreviews = config?.CapturePresetPreviews ?? false;
            _services.Shortcuts.OverlayCompactSlots = config?.OverlayCompactSlots ?? false;
            _services.Shortcuts.OverlayCompactView = config?.OverlayCompactView ?? false;
            _services.Shortcuts.OverlayPlacements = config?.OverlayPlacements ?? [];
            _services.Shortcuts.OverlayCompactFontSize = OverlayLayoutPolicy.CompactFontSize(config?.OverlayCompactFontSize);
            _services.Shortcuts.KeepMenuStateVariables = config?.KeepMenuStateVariables ?? false;
            _services.Slots.LoaderAutoSaveOriginals = new Dictionary<string, int>(config?.LoaderAutoSaveOriginals ?? [], StringComparer.OrdinalIgnoreCase);
            _services.PresetPreviews.FrameLocked = config?.PresetPreviewFrameLocked ?? false;
            _services.PresetPreviews.DefaultRegion = config?.PresetPreviewDefaultRegion is { IsValid: true } region ? region : null;
            _services.Shortcuts.OverlayTextColor = ShortcutOverlayController.FormatTextColor(ShortcutOverlayController.ParseTextColor(config?.ShortcutOverlayTextColor));
            _services.Shortcuts.OverlayAutoContrast = config?.OverlayAutoContrast ?? false;
            _services.Tracking.LastCheckUtc = TryParseDateTimeOffset(config?.LastModUpdateCheckUtc);
            Settings.LatestReleaseTag = config?.LatestReleaseTag;
            Settings.LatestReleaseTitle = config?.LatestReleaseTitle;
            Settings.LatestReleaseBody = config?.LatestReleaseBody;
            Settings.LatestReleasePublishedAt = config?.LatestReleasePublishedAt;
            Settings.LatestReleaseUrl = config?.LatestReleaseUrl;
            Settings.LatestReleasePackageUrl = config?.LatestReleasePackageUrl;
            Settings.LatestReleaseSha256Url = config?.LatestReleaseSha256Url;
            Settings.LoadedCheckAppUpdatesOnStartup = config?.CheckAppUpdatesOnStartup;
            Settings.CheckAppUpdatesOnStartup = ApplicationReleasePolicy.CanCheckApplicationUpdates(Settings.LoadedCheckAppUpdatesOnStartup ?? false);
            Settings.InstallBackupLimitGb = Math.Clamp(config?.InstallBackupLimitGb ?? ShellSettings.DefaultInstallBackupLimitGb, 0.5, 100);
            Settings.ReduceMotion = config?.ReduceMotion ?? false;
            Settings.StoredThemePreference = config?.ThemePreference;
            Settings.MinimizeToTray = config?.MinimizeToTray ?? false;
            Settings.ShowTitleBarSearch = config?.ShowTitleBarSearch ?? true;
            Settings.DetailedSearch = !string.Equals(config?.SearchStyle, "brief", StringComparison.OrdinalIgnoreCase);
            Settings.ClosedPageHints = new HashSet<string>(config?.ClosedPageHints ?? [], StringComparer.OrdinalIgnoreCase);
            Settings.SearchUsed = config?.SearchUsed ?? false;
            Settings.ShortcutsVisited = config?.ShortcutsVisited ?? false;
            Settings.InterfaceDensity = string.Equals(config?.InterfaceDensity, "compact", StringComparison.OrdinalIgnoreCase)
                ? InterfaceDensity.Compact
                : InterfaceDensity.Comfortable;
            _services.Online.Page.CardLayoutMode = string.Equals(config?.OnlineCardLayout, "grid", StringComparison.OrdinalIgnoreCase)
                ? OnlineCardLayoutMode.Grid
                : OnlineCardLayoutMode.List;
            if (config?.ConfigurationProfiles is { Count: > 0 })
            {
                _services.Combinations.Profiles.AddRange(config.ConfigurationProfiles
                    .Where(profile => !string.IsNullOrWhiteSpace(profile.Id)
                        && !string.IsNullOrWhiteSpace(profile.RepositoryId)));
            }
            if (config?.ModPersistentSlots is { Count: > 0 })
            {
                _services.Slots.Items.AddRange(config.ModPersistentSlots.Where(slot =>
                    !string.IsNullOrWhiteSpace(slot.Id)
                    && !string.IsNullOrWhiteSpace(slot.RepositoryId)
                    && !string.IsNullOrWhiteSpace(slot.ModRelativePath)
                    && slot.Values is { Count: > 0 }));
            }
            _services.Slots.DefaultSeeds.Clear();
            _services.Slots.DefaultsBackfilled.Clear();
            _services.Slots.DefaultsBackfilled.UnionWith(config?.DefaultSlotsBackfilled ?? []);
            foreach ((string repositoryId, List<string> mods) in config?.DefaultSlotSeeds ?? [])
            {
                _services.Slots.DefaultSeeds[repositoryId] = new HashSet<string>(mods ?? [], StringComparer.OrdinalIgnoreCase);
            }

            _services.Combinations.SelectedId = _services.Combinations.Profiles.Any(profile => profile.Id == config?.SelectedConfigurationProfileId)
                ? config?.SelectedConfigurationProfileId
                : null;
            Settings.EnableConflictDetection = config?.EnableConflictDetection ?? true;
            _services.Install.LastTransactionPath = !string.IsNullOrWhiteSpace(config?.LastInstallTransactionPath)
                && Directory.Exists(config.LastInstallTransactionPath)
                    ? config.LastInstallTransactionPath
                    : null;
            Settings.WindowX = config?.WindowX;
            Settings.WindowY = config?.WindowY;
            Settings.WindowWidth = config?.WindowWidth;
            Settings.WindowHeight = config?.WindowHeight;
            Settings.WindowMaximized = config?.WindowMaximized ?? false;
            Settings.WhatsNewSeenVersion = WhatsNewPolicy.SeenAfterLoad(config?.WhatsNewSeenVersion, _whatsNewFeatureSet, FirstInstall);
            _services.Install.LoadCopyRecords(config?.CopyDeployments ?? []);
            Settings.GettingStarted = GettingStartedChecklist.Load(FirstInstall, config?.GettingStartedTipsPending ?? false);
        }
        catch
        {
            Settings.GettingStarted = GettingStartedChecklist.Load(FirstInstall, savedPending: false);
            _services.Repositories.Items.Add(fallbackRepository);
            _services.Repositories.SelectedId = fallbackRepository.Id;
            Settings.CurrentSection = PrimarySection.Dashboard;
            Settings.UpdateCheckInterval = UpdateCheckInterval.Manual;
            Settings.LastUpdateCheckUtc = null;
            Settings.ModUpdateCheckInterval = UpdateCheckInterval.Manual;
            _services.Tracking.LastCheckUtc = null;
            Settings.LatestReleaseTag = null;
            Settings.LatestReleaseTitle = null;
            Settings.LatestReleaseBody = null;
            Settings.LatestReleasePublishedAt = null;
            Settings.LatestReleaseUrl = null;
            Settings.LatestReleasePackageUrl = null;
            Settings.LatestReleaseSha256Url = null;
            Settings.CheckAppUpdatesOnStartup = false;
            Settings.InstallBackupLimitGb = ShellSettings.DefaultInstallBackupLimitGb;
            Settings.ReduceMotion = false;
            Settings.StoredThemePreference = null;
            Settings.MinimizeToTray = false;
            Settings.InterfaceDensity = InterfaceDensity.Comfortable;
            _services.Online.Page.CardLayoutMode = OnlineCardLayoutMode.List;
            Settings.WindowX = null;
            Settings.WindowY = null;
            Settings.WindowWidth = null;
            Settings.WindowHeight = null;
        }

        if (UpgradeBuiltInRepositoryMetadata())
        {
            SaveShell();
        }

        _applySelectedRepositoryToInputs();
    }

    private bool UpgradeBuiltInRepositoryMetadata()
    {
        bool changed = false;
        foreach (WorkspaceRepository repository in _services.Repositories.Items)
        {
            repository.CharacterCategoryMappings ??= [];
            repository.CharacterAvatarBindings ??= [];
            repository.CharacterSelectionOverrides ??= [];
            RepositoryPresetInfo? preset = BuiltInRepositories.Presets.FirstOrDefault(candidate =>
                RepositoryPresetMatching.IsForPreset(repository, candidate));
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

    public void SaveShell(bool throwOnError = false)
    {
        try
        {
            _beforeSave();
            var config = new BetaShellConfig
            {
                CurrentPrimarySection = Settings.CurrentSection.ToString().ToLowerInvariant(),
                OnlinePreferences = _services.Online.Preferences,
                LocalLibraries = _services.Workspace.LocalLibraries,
                SelectedRepositoryId = _services.Repositories.SelectedId,
                UpdateCheckInterval = ApplicationReleasePolicy.UpdateSettingToSave(Settings.UpdateCheckInterval.ToString(), Settings.LoadedUpdateCheckInterval),
                LastUpdateCheckUtc = Settings.LastUpdateCheckUtc?.ToString("O"),
                ModUpdateCheckInterval = Settings.ModUpdateCheckInterval.ToString(),
                ShortcutPanelHotkeyModifiers = _services.Shortcuts.HotkeyModifiers,
                ShortcutPanelHotkeyKey = _services.Shortcuts.HotkeyKey,
                ShortcutPanelHotkeyEnabled = _services.Shortcuts.HotkeyEnabled,
                ShortcutInputMode = _services.Shortcuts.InputMode.ToString(),
                ShortcutOverlayOpacity = _services.Shortcuts.OverlayOpacity,
                ShortcutOverlayElementOpacity = _services.Shortcuts.OverlayElementOpacity,
                ShortcutHideUnsupported = _services.Shortcuts.HideUnsupported,
                OverlaySlotSwitchEnabled = _services.Shortcuts.OverlaySlotSwitchEnabled,
                CapturePresetPreviews = _services.Shortcuts.CapturePresetPreviews,
                OverlayCompactSlots = _services.Shortcuts.OverlayCompactSlots,
                OverlayCompactView = _services.Shortcuts.OverlayCompactView,
                OverlayPlacements = _services.Shortcuts.OverlayPlacements.Count == 0 ? null : _services.Shortcuts.OverlayPlacements.ToList(),
                OverlayCompactFontSize = _services.Shortcuts.OverlayCompactFontSize,
                OverlayAutoContrast = _services.Shortcuts.OverlayAutoContrast,
                KeepMenuStateVariables = _services.Shortcuts.KeepMenuStateVariables,
                LoaderAutoSaveOriginals = _services.Slots.LoaderAutoSaveOriginals.Count == 0 ? null : new Dictionary<string, int>(_services.Slots.LoaderAutoSaveOriginals),
                PresetPreviewFrameLocked = _services.PresetPreviews.FrameLocked,
                PresetPreviewDefaultRegion = _services.PresetPreviews.DefaultRegion,
                ShortcutOverlayTextColor = _services.Shortcuts.OverlayTextColor,
                LastModUpdateCheckUtc = _services.Tracking.LastCheckUtc?.ToString("O"),
                LatestReleaseTag = Settings.LatestReleaseTag,
                LatestReleaseTitle = Settings.LatestReleaseTitle,
                LatestReleaseBody = Settings.LatestReleaseBody,
                LatestReleasePublishedAt = Settings.LatestReleasePublishedAt,
                LatestReleaseUrl = Settings.LatestReleaseUrl,
                LatestReleasePackageUrl = Settings.LatestReleasePackageUrl,
                LatestReleaseSha256Url = Settings.LatestReleaseSha256Url,
                CheckAppUpdatesOnStartup = ApplicationReleasePolicy.UpdateSettingToSave<bool?>(Settings.CheckAppUpdatesOnStartup, Settings.LoadedCheckAppUpdatesOnStartup),
                InstallBackupLimitGb = Settings.InstallBackupLimitGb,
                ReduceMotion = Settings.ReduceMotion,
                ThemePreference = ThemePreferences.Format(Settings.ThemePreference),
                MinimizeToTray = Settings.MinimizeToTray,
                ShowTitleBarSearch = Settings.ShowTitleBarSearch,
                SearchStyle = Settings.DetailedSearch ? "detailed" : "brief",
                ClosedPageHints = [.. Settings.ClosedPageHints.Order(StringComparer.Ordinal)],
                SearchUsed = Settings.SearchUsed,
                ShortcutsVisited = Settings.ShortcutsVisited,
                NavigationCollapsed = Settings.NavigationCollapsed,
                InterfaceDensity = Settings.InterfaceDensity == InterfaceDensity.Compact ? "compact" : "comfortable",
                OnlineCardLayout = _services.Online.Page.CardLayoutMode == OnlineCardLayoutMode.Grid ? "grid" : "list",
                RepositoryWorkspaceView = _services.Workspace.ModList.View.ToString().ToLowerInvariant(),
                WorkspaceCharacterWidth = _services.Workspace.ModList.CharacterPaneWidth,
                WorkspaceDetailWidth = _services.Workspace.ModList.DetailPaneWidth,
                UpdatesView = UpdatesViewModel.FormatView(_services.Updates.View),
                SelectedConfigurationProfileId = _services.Combinations.SelectedId,
                EnableConflictDetection = Settings.EnableConflictDetection,
                LastInstallTransactionPath = _services.Install.LastTransactionPath,
                ConfigurationProfiles = [.. _services.Combinations.Profiles],
                ModPersistentSlots = [.. _services.Slots.Items],
                CopyDeployments = _services.Install.CopyRecordsSnapshot(),
                DefaultSlotSeeds = _services.Slots.DefaultSeeds.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToList()),
                DefaultSlotsBackfilled = [.. _services.Slots.DefaultsBackfilled.Order(StringComparer.Ordinal)],
                WindowX = Settings.WindowX,
                WindowY = Settings.WindowY,
                WindowWidth = Settings.WindowWidth,
                WindowHeight = Settings.WindowHeight,
                WindowMaximized = Settings.WindowMaximized,
                WhatsNewSeenVersion = Settings.WhatsNewSeenVersion,
                Repositories = [.. _services.Repositories.Items],
                RepositoryGamesExplicit = true,
                GettingStartedTipsPending = Settings.GettingStarted.IsPending,
                ExtensionData = _services.ShellConfig.ExtensionData
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            WriteConfigurationTextWithBackup(_services.ShellConfig.ShellConfigPath, json);
        }
        catch (Exception ex)
        {
            if (throwOnError) throw;
            _log("Save repository configuration", ex);
            _services.Workspace.Status = T("Configuration.FailedToSaveRepositoryConfig");
        }
    }

    public void LoadLegacy()
    {
        _bindings.Clear();
        _links.Clear();
        _services.Tracking.Origins.Clear();

        if (!File.Exists(_services.ShellConfig.ConfigPath))
        {
            return;
        }

        try
        {
            LegacyConfig config = LegacyConfigFile.Parse(ReadConfigurationTextWithRecovery(_services.ShellConfig.ConfigPath, LegacyConfigFile.IsValid));
            if (config.SourceDirectory is not null) _services.Paths.Source = config.SourceDirectory;
            if (config.TargetDirectory is not null) _services.Paths.Target = config.TargetDirectory;
            if (config.LauncherPath is not null) _services.Paths.Launcher = config.LauncherPath;
            Settings.LegacyDarkTheme = config.DarkTheme;
            if (config.Language is not null)
            {
                _services.Language.Load(UiLanguagePolicy.FromConfig(config.Language, UiText.Languages) ?? _services.Language.Current);
            }

            foreach ((string modPath, List<ShortcutBinding> bindings) in config.Bindings) _bindings[modPath] = bindings;
            foreach ((string modPath, string link) in config.ModLinks) _links[modPath] = link;
            foreach ((string modPath, TrackedModOrigin origin) in config.ModOrigins) _services.Tracking.Origins[modPath] = origin;
        }
        catch (Exception ex)
        {
            _log("Load configuration", ex);
            _services.Workspace.Status = T("Configuration.FailedToLoadConfigOlderConfig");
        }

        if (_services.Tracking.Deduplicate())
        {
            Save();
        }

    }

    public bool Save()
    {
        try
        {
            _saveSelectedRepositoryFromInputs();
            WriteLegacyConfig();
            SaveShell();
            return true;
        }
        catch (Exception ex)
        {
            _log("Save configuration", ex);
            _services.Workspace.Status = T("Configuration.FailedToSaveConfigButThe");
            return false;
        }
    }

    // config.ini: paths, language, and the Mod bindings, links and origins. Throws when it cannot be written.
    public void WriteLegacyConfig()
    {
        var config = new LegacyConfig
        {
            SourceDirectory = _services.Paths.Source,
            TargetDirectory = _services.Paths.Target,
            LauncherPath = _services.Paths.Launcher,
            DarkTheme = _services.Appearance.IsDark,
            Language = UiLanguagePolicy.ToConfig(_services.Language.Current)
        };
        foreach ((string modPath, List<ShortcutBinding> bindings) in _bindings) config.Bindings[modPath] = bindings;
        foreach ((string modPath, string link) in _links) config.ModLinks[modPath] = link;
        foreach ((string modPath, TrackedModOrigin origin) in _services.Tracking.Origins) config.ModOrigins[modPath] = origin;

        WriteConfigurationTextWithBackup(_services.ShellConfig.ConfigPath, LegacyConfigFile.Format(config));
    }

    private string ReadConfigurationTextWithRecovery(string filePath, Func<string, bool> validator)
    {
        string text = _services.ShellConfig.Read(filePath, validator, out bool restoredFromBackup);
        if (restoredFromBackup)
        {
            _services.Workspace.Status = T("Configuration.WasDamagedAndTheLatestBackup", Path.GetFileName(filePath));
        }

        return text;
    }

    private void WriteConfigurationTextWithBackup(string filePath, string content)
    {
        _services.ShellConfig.Write(filePath, content);
    }

    public static UpdateCheckInterval ParseUpdateCheckInterval(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out UpdateCheckInterval interval)
            ? interval
            : UpdateCheckInterval.Manual;
    }
}
