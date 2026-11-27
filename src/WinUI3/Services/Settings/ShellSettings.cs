using IntegratedModManager.Core.Configuration;

namespace ModFolderCopier.WinUI;

/// <summary>The window's top-level sections (the navigation items).</summary>
internal enum PrimarySection
{
    Dashboard,
    Repository,
    Online,
    Updates,
    Settings,
    Shortcuts
}

internal enum InterfaceDensity
{
    Comfortable,
    Compact
}

internal enum UpdateCheckInterval
{
    Manual,
    Daily,
    EveryThreeDays,
    Weekly
}

/// <summary>
/// The shell's own saved settings (stage J finish, step 8a: moved from the window's fields unchanged): the section shown,
/// update checks and the last release found, backups, motion, theme, tray, title bar search, navigation, density,
/// conflict detection, the window's place, what's new and the getting-started tips. ConfigurationService loads and saves
/// them with the rest of beta-shell.json and config.ini; the window and its pages read and change them.
/// </summary>
internal sealed class ShellSettings
{
    public const double DefaultInstallBackupLimitGb = 5;

    public PrimarySection CurrentSection { get; set; } = PrimarySection.Dashboard;

    public UpdateCheckInterval UpdateCheckInterval { get; set; } = UpdateCheckInterval.Manual;
    /// <summary>The interval as read, kept for builds that cannot update themselves (ApplicationReleasePolicy).</summary>
    public string? LoadedUpdateCheckInterval { get; set; }
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public UpdateCheckInterval ModUpdateCheckInterval { get; set; } = UpdateCheckInterval.Manual;
    public bool CheckAppUpdatesOnStartup { get; set; }
    public bool? LoadedCheckAppUpdatesOnStartup { get; set; }

    public string? LatestReleaseTag { get; set; }
    public string? LatestReleaseTitle { get; set; }
    public string? LatestReleaseBody { get; set; }
    public string? LatestReleasePublishedAt { get; set; }
    public string? LatestReleaseUrl { get; set; }
    public string? LatestReleasePackageUrl { get; set; }
    public string? LatestReleaseSha256Url { get; set; }

    public double InstallBackupLimitGb { get; set; } = DefaultInstallBackupLimitGb;
    public bool EnableConflictDetection { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;
    /// <summary>The theme as read from beta-shell.json, before the window resolves it.</summary>
    public string? StoredThemePreference { get; set; }
    /// <summary>config.ini's older dark-theme switch.</summary>
    public bool? LegacyDarkTheme { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ShowTitleBarSearch { get; set; } = true;
    /// <summary>The search lists every feature with where it is (detailed) or only pages and commands until typed (brief).</summary>
    public bool DetailedSearch { get; set; } = true;
    public HashSet<string> ClosedPageHints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Features picked from the search before: they are used directly instead of shown.</summary>
    public HashSet<string> GuidedFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SearchUsed { get; set; }
    public bool ShortcutsVisited { get; set; }
    public bool NavigationCollapsed { get; set; }
    public InterfaceDensity InterfaceDensity { get; set; } = InterfaceDensity.Comfortable;
    /// <summary>Set while the Updates page's view is restored from the file, which is not a change to save.</summary>
    public bool RestoringUpdatesView { get; set; }

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public string? WhatsNewSeenVersion { get; set; }
    public GettingStartedChecklist GettingStarted { get; set; } = GettingStartedChecklist.Load(firstInstall: false, savedPending: false);
}
