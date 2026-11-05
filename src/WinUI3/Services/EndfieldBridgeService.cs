using System.Diagnostics;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.EndfieldLaunch;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The integrated Endfield launch for Endfield repositories: whether it applies, where XXMI is, the launch
/// profile, settings migrated from the standalone bridge, and whether a launch or a settings dialog is
/// running. The dashboard and the Mod workspace both use it, so it is a service and not part of a page
/// (docs/development/UI-Structure-Plan.md, stage J4). The launch itself is the AGPL-3.0-only
/// IntegratedModManager.EndfieldLaunch component.
/// </summary>
internal sealed class EndfieldBridgeService
{
    private bool _operationInProgress;
    private bool _launchInProgress;

    /// <summary>Called with a description and the exception when something worth logging fails.</summary>
    public Action<string, Exception> LogIssue { get; set; } = (_, _) => { };

    /// <summary>Raised when a settings dialog or a launch starts or ends.</summary>
    public event EventHandler? BusyChanged;

    /// <summary>The settings dialog is open.</summary>
    public bool OperationInProgress
    {
        get => _operationInProgress;
        set
        {
            if (_operationInProgress == value) return;
            _operationInProgress = value;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A launch is waiting for the game to start.</summary>
    public bool LaunchInProgress
    {
        get => _launchInProgress;
        set
        {
            if (_launchInProgress == value) return;
            _launchInProgress = value;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsBusy => _operationInProgress || _launchInProgress;

    // Manager 4.1.1–4.2.0 downloaded the standalone bridge here; read only to migrate its settings.
    public static string LegacyToolsRoot => Path.Combine(AppContext.BaseDirectory, "cache", "optional-tools");

    public static string LaunchExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ModFolderCopier.WinUI.exe");

    public static bool CanUse(WorkspaceRepository? repository) => repository is not null
        && EndfieldBridgeIntegration.IsEndfield(repository.OnlineCategoryId, repository.OnlineGameName);

    public static EndfieldLaunchService CreateLaunchService() => new(Program.EndfieldLaunchDataDirectory, LaunchExecutablePath);

    public static IEnumerable<string> DefaultLoaderPaths()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(appData, "XXMI Launcher", "Resources", "Bin", "XXMI Launcher.exe");
        yield return Path.Combine(appData, "XXMI Launcher", "XXMI Launcher.exe");
        yield return Path.Combine(programFiles, "XXMI Launcher", "Resources", "Bin", "XXMI Launcher.exe");
    }

    /// <summary>The given launcher, or the first XXMI Launcher found in its usual places.</summary>
    public static string ResolveLoader(string? launcherPath) => EndfieldBridgeIntegration.ResolveLoader(launcherPath, DefaultLoaderPaths());

    /// <summary>Fills empty Endfield settings from a standalone bridge 0.3.x and adopts its EFMI restore point.</summary>
    public bool ImportLegacySettings(WorkspaceRepository repository, EndfieldLaunchService service)
    {
        if (!EndfieldBridgeIntegration.HasLegacyInstallation(LegacyToolsRoot)) return false;
        string legacy = EndfieldBridgeIntegration.LegacyInstallDirectory(LegacyToolsRoot);
        bool changed = false;
        try
        {
            changed |= service.AdoptLegacyRestoreState(legacy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndfieldLaunchException or System.Text.Json.JsonException)
        {
            LogIssue("Adopt legacy bridge restore point", ex);
        }

        if (!string.IsNullOrWhiteSpace(repository.EndfieldGamePath) || EndfieldLaunchService.ReadLegacyProfile(legacy) is not { } profile)
        {
            return changed;
        }

        repository.EndfieldGamePath = profile.Game;
        repository.EndfieldParentPath = profile.Parent;
        repository.EndfieldGameArguments = profile.GameArguments;
        repository.EndfieldUseMods = profile.WithMods;
        if (string.IsNullOrWhiteSpace(repository.LauncherPath) && !string.IsNullOrWhiteSpace(profile.Xxmi)) repository.LauncherPath = profile.Xxmi;
        // The risk was already accepted explicitly when the standalone bridge was enabled.
        if (repository.UseEndfieldBridge) repository.EndfieldRiskAccepted = true;
        return true;
    }

    public static LaunchProfile BuildLaunchProfile(WorkspaceRepository repository, bool english, string? sessionId = null) => new()
    {
        Game = repository.EndfieldGamePath.Trim(),
        Parent = repository.EndfieldParentPath.Trim(),
        Xxmi = ResolveLoader(repository.LauncherPath),
        GameArguments = repository.EndfieldGameArguments.Trim(),
        WithMods = repository.EndfieldUseMods,
        Language = english ? "en-US" : "zh-CN",
        SessionId = sessionId ?? string.Empty
    };

    /// <summary>The launch's last status for the session, polled until it ends, the time runs out or the window closes.</summary>
    public static async Task<LaunchStatus?> WaitForLaunchStatusAsync(LaunchStore store, string sessionId, TimeSpan timeout, Func<bool> stop)
    {
        var stopwatch = Stopwatch.StartNew();
        LaunchStatus? latest = null;
        while (stopwatch.Elapsed < timeout && !stop())
        {
            await Task.Delay(300);
            LaunchStatus? status = store.ReadStatus();
            if (status is null || status.SessionId != sessionId) continue;
            latest = status;
            if (LaunchStates.IsTerminal(status.State)) break;
        }

        return latest;
    }
}
