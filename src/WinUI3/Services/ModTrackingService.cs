using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mods installed from the Online page and tracked for updates, with the last update check's results (stage J
/// finish, step 5: moved from the window and the Online page unchanged). The records are saved in config.ini by the
/// window's configuration code; installs add them, the Updates page checks and removes them.
/// </summary>
internal sealed class ModTrackingService(RepositoryService repositories)
{
    /// <summary>The tracked Mods, by installed folder.</summary>
    public Dictionary<string, TrackedModOrigin> Origins { get; } = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>The results of the last update check, shown on the Updates page.</summary>
    public List<TrackedModUpdateResult> Results { get; } = [];

    /// <summary>
    /// Drops the records of a Mod that was installed again elsewhere (only the newest existing install is kept, per
    /// repository). Returns whether anything was removed, so the caller saves.
    /// </summary>
    public bool Deduplicate()
    {
        IReadOnlyList<string> stalePaths = OnlineTrackingScopePolicy.FindStaleDuplicatePaths(
            Origins.Values.Select(origin => new OnlineTrackedInstallation(
                origin.Path, OnlineInstallationDetectionPolicy.BuildIdentity(origin.ItemId, origin.ProfileUrl),
                Directory.Exists(origin.Path), origin.LastKnownUpdatedAt)),
            repositories.Items.Select(repository => repository.SourcePath));
        foreach (string path in stalePaths)
        {
            Remove(path);
        }
        return stalePaths.Count > 0;
    }

    /// <summary>Stops tracking the Mod in this folder.</summary>
    public void Remove(string path)
    {
        Origins.Remove(path);
        Results.RemoveAll(result => string.Equals(result.Path, path, StringComparison.CurrentCultureIgnoreCase));
    }
}
