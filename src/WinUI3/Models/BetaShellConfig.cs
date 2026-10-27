using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

public sealed class BetaShellConfig
{
    public uint ShortcutPanelHotkeyModifiers { get; set; } = 3;
    public uint ShortcutPanelHotkeyKey { get; set; } = 0x4B;
    public bool ShortcutPanelHotkeyEnabled { get; set; } = true;
    public string? ShortcutInputMode { get; set; }
    public int? ShortcutOverlayOpacity { get; set; }
    public bool? ShortcutHideUnsupported { get; set; }
    public string? ShortcutOverlayTextColor { get; set; }
    public bool? OverlaySlotSwitchEnabled { get; set; }
    public Dictionary<string, LocalLibraryState>? LocalLibraries { get; set; }
    public OnlineWorkspacePreferences? OnlinePreferences { get; set; }

    public string RepositoryWorkspaceView { get; set; } = "list";
    public string? CurrentPrimarySection { get; set; }

    public string? SelectedRepositoryId { get; set; }

    public string? UpdateCheckInterval { get; set; }

    public string? LastUpdateCheckUtc { get; set; }

    public string? ModUpdateCheckInterval { get; set; }

    public string? LastModUpdateCheckUtc { get; set; }

    public string? LatestReleaseTag { get; set; }

    public string? LatestReleaseTitle { get; set; }

    public string? LatestReleaseBody { get; set; }

    public string? LatestReleasePublishedAt { get; set; }

    public string? LatestReleaseUrl { get; set; }

    public string? LatestReleasePackageUrl { get; set; }

    public string? LatestReleaseSha256Url { get; set; }

    public bool? CheckAppUpdatesOnStartup { get; set; }

    public double? InstallBackupLimitGb { get; set; }

    public bool ReduceMotion { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool NavigationCollapsed { get; set; }

    public string? InterfaceDensity { get; set; }

    public string? OnlineCardLayout { get; set; }

    public string? SelectedConfigurationProfileId { get; set; }

    public bool? EnableConflictDetection { get; set; }

    public string? LastInstallTransactionPath { get; set; }

    public List<ModConfigurationProfile> ConfigurationProfiles { get; set; } = [];

    public List<ModPersistentSlot> ModPersistentSlots { get; set; } = [];

    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public int? WindowWidth { get; set; }
    public bool? WindowMaximized { get; set; }
    public string? WhatsNewSeenVersion { get; set; }

    public int? WindowHeight { get; set; }

    public List<WorkspaceRepository> Repositories { get; set; } = [];
}
