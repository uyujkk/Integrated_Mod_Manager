using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Combination presets (stage J finish, step 7, moved from the window unchanged): the saved presets of every repository,
/// the selected one, whether a preset is being saved, restored or bundled, and capturing the enabled combination with
/// its loader values. The workspace's preset card shows and restores them; the overlay saves one in game.
/// </summary>
internal sealed class CombinationService(AppServices services)
{
    private readonly AppServices _services = services;
    private Action _saveShellConfig = () => { };

    /// <summary>Called once by the window: saving beta-shell.json.</summary>
    public void Attach(Action saveShellConfig) => _saveShellConfig = saveShellConfig;

    /// <summary>Every combination preset, of every repository (saved in beta-shell.json).</summary>
    public List<ModConfigurationProfile> Profiles { get; } = [];

    /// <summary>The preset chosen on the workspace's preset card (saved in beta-shell.json).</summary>
    public string? SelectedId { get; set; }

    /// <summary>A preset is being saved, restored, exported or installed; the rest of the app waits.</summary>
    public bool IsApplying { get; private set; }

    /// <summary>Raised when <see cref="IsApplying"/> changes: the window locks its navigation, the workspace its controls.</summary>
    public event EventHandler? BusyChanged;

    public void SetBusy(bool busy)
    {
        IsApplying = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The selected repository's presets, by name.</summary>
    public IEnumerable<ModConfigurationProfile> CurrentRepositoryProfiles()
    {
        return Profiles
            .Where(profile => string.Equals(profile.RepositoryId, _services.Repositories.SelectedId, StringComparison.Ordinal))
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>The selected preset, when it belongs to the selected repository.</summary>
    public ModConfigurationProfile? Selected()
    {
        return Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, SelectedId, StringComparison.Ordinal)
            && string.Equals(profile.RepositoryId, _services.Repositories.SelectedId, StringComparison.Ordinal));
    }

    /// <summary>A character of the repository was renamed (#108): its presets name the new folder.</summary>
    public void RenameCharacter(WorkspaceRepository repository, CharacterRenamePlan plan, ReferenceUndoLog log)
    {
        foreach (ModConfigurationProfile profile in Profiles.Where(profile => profile.RepositoryId == repository.Id))
        {
            log.RewriteAll(profile.ModRelativePaths, plan.MapRelative);
            foreach (ModConfigurationPersistentState state in profile.PersistentStates)
                log.Rewrite(() => state.ModRelativePath, value => state.ModRelativePath = value, plan.MapRelative);
        }
    }

    /// <summary>A preset name not yet used in the selected repository: the name, or the name with " (2)", " (3)" and so on.</summary>
    public string UniqueName(string name)
    {
        var used = CurrentRepositoryProfiles().Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;
        for (int i = 2; ; i++) if (!used.Contains($"{name} ({i})")) return $"{name} ({i})";
    }

    // Mods sit two levels deep: character folder, then Mod folder.
    public static string[] RepositoryModRelativePaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateDirectories(root, "*", options)
            .SelectMany(character => Directory.EnumerateDirectories(character, "*", options))
            .Select(mod => Path.GetRelativePath(root, mod)).ToArray();
    }

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private List<string> CaptureCurrentConfigurationProfile()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null) throw new InvalidOperationException("Select a repository first.");
        return [.. ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath, _services.Install.OwnsCopy(repository))];
    }

    /// <summary>
    /// Read-only capture of the enabled combination (and loader state) into a preset. Shared by the workspace and the
    /// in-game overlay, which cannot show dialogs; returns null when an empty set is declined.
    /// </summary>
    public async Task<(int Mods, int Parameters)?> CaptureAsync(WorkspaceRepository repository, string name,
        ModConfigurationProfile? existing, bool includeState, string userIni, Func<Task<bool>> confirmEmpty)
    {
        List<string> paths = await Task.Run(CaptureCurrentConfigurationProfile);
        if (paths.Count == 0 && !await confirmEmpty()) return null;

        IReadOnlyList<ModPersistentSourceState> captured = includeState
            ? await Task.Run(() => ModPersistentPresetEngine.CaptureCombination(paths.Select(relative =>
                (Path.Combine(repository.SourcePath, relative), Path.GetFileName(relative))).ToArray(), userIni))
            : [];
        // Validate copy deployments too: their INIs may have changed independently. A deployment that is a
        // link back to its own source Mod has no separate INIs (it may predate switching the repository to copies).
        ModPersistentSourceState[] copies = captured
            .Where(state => !ModDeploymentLinks.IsLinkTo(Path.Combine(repository.TargetPath, state.DeployedRelativePath), state.ModDirectory))
            .Select(state => state with { ModDirectory = Path.Combine(repository.TargetPath, state.DeployedRelativePath) })
            .ToArray();
        if (copies.Any(state => state.Values.Count > 0) && !repository.UseDirectoryLinks)
            await Task.Run(() => ModPersistentPresetEngine.PlanCombinationRuntimeRestore(copies, userIni));
        var stillEnabled = await Task.Run(() => ModCombinationDeploymentPolicy.Capture(repository.SourcePath, repository.TargetPath, _services.Install.OwnsCopy(repository)));
        if (!stillEnabled.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(paths))
            throw new IOException(T("CombinationPresets.DeploymentChangedDuringCaptureTheCombination"));

        ModConfigurationProfile profile = existing ?? new ModConfigurationProfile { RepositoryId = repository.Id, Name = name };
        profile.ModRelativePaths = paths;
        profile.IncludesPersistentState = includeState;
        profile.PersistentStates = captured.Select(state => new ModConfigurationPersistentState
        {
            ModRelativePath = Path.GetRelativePath(repository.SourcePath, state.ModDirectory),
            Values = [.. state.Values]
        }).ToList();
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (existing is null) Profiles.Add(profile);
        SelectedId = profile.Id;
        if (includeState) repository.PersistentUserIniPath = userIni;
        _saveShellConfig();
        return (paths.Count, profile.PersistentStates.Sum(state => state.Values.Count));
    }
}
