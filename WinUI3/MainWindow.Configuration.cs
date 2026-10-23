using System.Globalization;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core;
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
            string shellJson = ReadConfigurationTextWithRecovery(_shellConfigPath, IsValidShellConfiguration);
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
            StatusTextBlock.Text = L("保存仓库配置失败。", "Failed to save repository config.");
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
            string configText = ReadConfigurationTextWithRecovery(_configPath, IsValidLegacyConfiguration);
            foreach (string line in configText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("source_dir=", StringComparison.Ordinal))
                {
                    SourceTextBox.Text = DecodeValue(line["source_dir=".Length..]);
                }
                else if (line.StartsWith("target_dir=", StringComparison.Ordinal))
                {
                    TargetTextBox.Text = DecodeValue(line["target_dir=".Length..]);
                }
                else if (line.StartsWith("launcher_path=", StringComparison.Ordinal))
                {
                    LauncherTextBox.Text = DecodeValue(line["launcher_path=".Length..]);
                }
                else if (line.StartsWith("theme=", StringComparison.Ordinal))
                {
                    _isDarkTheme = string.Equals(DecodeValue(line["theme=".Length..]), "dark", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("language=", StringComparison.Ordinal))
                {
                    string language = DecodeValue(line["language=".Length..]);
                    _currentLanguage = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                        ? AppLanguage.EnUs
                        : AppLanguage.ZhCn;
                }
                else if (line.StartsWith("binding=", StringComparison.Ordinal))
                {
                    string[] parts = line["binding=".Length..].Split('\t');
                    if (parts.Length == 3)
                    {
                        string modPath = DecodeValue(parts[0]);
                        if (!_modBindings.TryGetValue(modPath, out List<ShortcutBinding>? bindings))
                        {
                            bindings = [];
                            _modBindings[modPath] = bindings;
                        }

                        bindings.Add(new ShortcutBinding(DecodeValue(parts[1]), DecodeValue(parts[2])));
                    }
                }
                else if (line.StartsWith("binding_auto=", StringComparison.Ordinal))
                {
                    string[] parts = line["binding_auto=".Length..].Split('\t');
                    if (parts.Length == 6
                        && int.TryParse(DecodeValue(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bindingIndex))
                    {
                        string modPath = DecodeValue(parts[0]);
                        if (_modBindings.TryGetValue(modPath, out List<ShortcutBinding>? bindings)
                            && bindingIndex >= 0
                            && bindingIndex < bindings.Count)
                        {
                            ShortcutBinding binding = bindings[bindingIndex];
                            binding.AutoSection = DecodeValue(parts[2]);
                            binding.AutoBehavior = DecodeValue(parts[3]);
                            binding.AutoTarget = DecodeValue(parts[4]);
                            binding.AutoSourceFile = DecodeValue(parts[5]);
                        }
                    }
                }
                else if (line.StartsWith("mod_link=", StringComparison.Ordinal))
                {
                    string[] parts = line["mod_link=".Length..].Split('\t');
                    if (parts.Length == 2)
                    {
                        _modLinks[DecodeValue(parts[0])] = DecodeValue(parts[1]);
                    }
                }
                else if (line.StartsWith("mod_origin=", StringComparison.Ordinal))
                {
                    string[] parts = line["mod_origin=".Length..].Split('\t');
                    if (parts.Length >= 7)
                    {
                        string path = DecodeValue(parts[0]);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        int.TryParse(DecodeValue(parts[2]), out int itemId);
                        _trackedModOrigins[path] = new TrackedModOrigin
                        {
                            Path = path,
                            SourceSite = DecodeValue(parts[1]),
                            ItemId = itemId,
                            Title = DecodeValue(parts[3]),
                            ProfileUrl = DecodeValue(parts[4]),
                            PreviewUrl = DecodeValue(parts[5]),
                            LastKnownUpdatedAt = TryParseDateTimeOffset(DecodeValue(parts[6])),
                            DownloadFileId = parts.Length > 7 ? DecodeValue(parts[7]) : string.Empty,
                            DownloadFileName = parts.Length > 8 ? DecodeValue(parts[8]) : string.Empty
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Load configuration", ex);
            StatusTextBlock.Text = L("配置读取失败，已忽略旧配置。", "Failed to load config. Older config values were ignored.");
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

            var lines = new List<string>
            {
                "source_dir=" + EncodeValue(SourceTextBox.Text ?? string.Empty),
                "target_dir=" + EncodeValue(TargetTextBox.Text ?? string.Empty),
                "launcher_path=" + EncodeValue(LauncherTextBox.Text ?? string.Empty),
                "theme=" + EncodeValue(_isDarkTheme ? "dark" : "light"),
                "language=" + EncodeValue(_currentLanguage == AppLanguage.EnUs ? "en" : "zh")
            };

            foreach ((string path, List<ShortcutBinding> bindings) in _modBindings.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    ShortcutBinding binding = bindings[bindingIndex];
                    if (string.IsNullOrWhiteSpace(binding.Shortcut) && string.IsNullOrWhiteSpace(binding.Action))
                    {
                        continue;
                    }

                    lines.Add("binding=" + EncodeValue(path) + "\t" + EncodeValue(binding.Shortcut) + "\t" + EncodeValue(binding.Action));
                    if (binding.HasAutoDescription)
                    {
                        lines.Add("binding_auto="
                            + EncodeValue(path) + "\t"
                            + EncodeValue(bindingIndex.ToString(CultureInfo.InvariantCulture)) + "\t"
                            + EncodeValue(binding.AutoSection) + "\t"
                            + EncodeValue(binding.AutoBehavior) + "\t"
                            + EncodeValue(binding.AutoTarget) + "\t"
                            + EncodeValue(binding.AutoSourceFile));
                    }
                }
            }

            foreach ((string path, string link) in _modLinks.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(link))
                {
                    lines.Add("mod_link=" + EncodeValue(path) + "\t" + EncodeValue(link));
                }
            }

            foreach ((string path, TrackedModOrigin origin) in _trackedModOrigins.OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                lines.Add("mod_origin="
                    + EncodeValue(path) + "\t"
                    + EncodeValue(origin.SourceSite) + "\t"
                    + EncodeValue(origin.ItemId.ToString()) + "\t"
                    + EncodeValue(origin.Title) + "\t"
                    + EncodeValue(origin.ProfileUrl) + "\t"
                    + EncodeValue(origin.PreviewUrl ?? string.Empty) + "\t"
                    + EncodeValue(origin.LastKnownUpdatedAt?.ToString("O") ?? string.Empty) + "\t"
                    + EncodeValue(origin.DownloadFileId) + "\t" + EncodeValue(origin.DownloadFileName));
            }

            WriteConfigurationTextWithBackup(_configPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            SaveShellConfig();
            return true;
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Save configuration", ex);
            StatusTextBlock.Text = L("配置保存失败，但不影响当前使用。", "Failed to save config, but the app can continue.");
            return false;
        }
    }

    private string ReadConfigurationTextWithRecovery(string filePath, Func<string, bool> validator)
    {
        try
        {
            string text = File.ReadAllText(filePath, Encoding.UTF8);
            if (!validator(text))
            {
                throw new InvalidDataException($"Invalid configuration file: {Path.GetFileName(filePath)}");
            }

            return text;
        }
        catch when (TryRestoreLatestConfigurationBackup(filePath, validator, out string restoredText))
        {
            StatusTextBlock.Text = L(
                $"配置 {Path.GetFileName(filePath)} 损坏，已自动恢复最近备份。",
                $"{Path.GetFileName(filePath)} was damaged and the latest backup was restored automatically.");
            return restoredText;
        }
    }

    private void WriteConfigurationTextWithBackup(string filePath, string content)
    {
        string directory = Path.GetDirectoryName(filePath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);

        if (File.Exists(filePath)
            && (!_lastConfigurationBackupUtc.TryGetValue(filePath, out DateTimeOffset lastBackup)
                || DateTimeOffset.UtcNow - lastBackup >= TimeSpan.FromMinutes(10)))
        {
            Directory.CreateDirectory(_configurationBackupPath);
            string backupName = $"{Path.GetFileName(filePath)}.{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.bak";
            File.Copy(filePath, Path.Combine(_configurationBackupPath, backupName), overwrite: false);
            _lastConfigurationBackupUtc[filePath] = DateTimeOffset.UtcNow;
            PruneConfigurationBackups(filePath, keepCount: 10);
        }

        string temporaryPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private bool TryRestoreLatestConfigurationBackup(
        string filePath,
        Func<string, bool> validator,
        out string restoredText)
    {
        restoredText = string.Empty;
        try
        {
            if (!Directory.Exists(_configurationBackupPath))
            {
                return false;
            }

            foreach (string backupPath in Directory.EnumerateFiles(
                         _configurationBackupPath,
                         Path.GetFileName(filePath) + ".*.bak",
                         SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                string candidate = File.ReadAllText(backupPath, Encoding.UTF8);
                if (!validator(candidate))
                {
                    continue;
                }

                string restoreTemporaryPath = filePath + ".restore-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(restoreTemporaryPath, candidate, new UTF8Encoding(false));
                File.Move(restoreTemporaryPath, filePath, overwrite: true);
                restoredText = candidate;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void PruneConfigurationBackups(string filePath, int keepCount)
    {
        try
        {
            foreach (string staleBackup in Directory.EnumerateFiles(
                         _configurationBackupPath,
                         Path.GetFileName(filePath) + ".*.bak",
                         SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(Math.Max(1, keepCount)))
            {
                File.Delete(staleBackup);
            }
        }
        catch
        {
        }
    }

    private static bool IsValidShellConfiguration(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidLegacyConfiguration(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.StartsWith("source_dir=", StringComparison.Ordinal));
    }

    private static string EncodeValue(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string DecodeValue(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch
        {
            return string.Empty;
        }
    }
}
