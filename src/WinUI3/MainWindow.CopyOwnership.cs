using IntegratedModManager.Core.Deployment;

namespace ModFolderCopier.WinUI;

// Issue #107: which copied Mod folders in the loader folder the manager made. The records live in beta-shell.json
// (CopyDeploymentLedger), never as a file inside a Mod folder where the loader could read it. A same-named folder
// the manager cannot recognise is an external folder: shown, kept, and taken over only when the user says so.
public sealed partial class MainWindow
{
    private readonly List<CopyDeploymentRecord> _copyDeployments = [];
    // Set when a planner claimed an old copy by its content on a background thread; saved on the next chance.
    private volatile bool _copyRecordsChanged;

    private CopyDeploymentLedger CopyLedger(WorkspaceRepository repository) => new(repository.Id, repository.SourcePath, _copyDeployments);

    /// <summary>For the planners: whether a copied folder is the manager's copy of a source Mod.</summary>
    private Func<string, string, bool> OwnsCopy(WorkspaceRepository repository)
    {
        CopyDeploymentLedger ledger = CopyLedger(repository);
        return (source, target) =>
        {
            bool owned = ledger.Owns(source, target);
            if (ledger.Changed) _copyRecordsChanged = true;
            return owned;
        };
    }

    private void SaveCopyRecordsIfChanged()
    {
        if (!_copyRecordsChanged) return;
        _copyRecordsChanged = false;
        SaveShellConfig();
    }

    private List<CopyDeploymentRecord> CopyRecordsSnapshot()
    {
        lock (_copyDeployments) return [.. _copyDeployments];
    }

    private void RestoreCopyRecords(IReadOnlyCollection<CopyDeploymentRecord> records)
    {
        lock (_copyDeployments)
        {
            _copyDeployments.Clear();
            _copyDeployments.AddRange(records);
        }
        SaveShellConfig();
    }

    // The repository Mod whose deployed folder this is, when exactly one has that name.
    private static string? SourceForDeployedFolder(WorkspaceRepository repository, string deployedPath)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(deployedPath));
        if (!Directory.Exists(repository.SourcePath)) return null;
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };
        string[] matches = Directory.EnumerateDirectories(repository.SourcePath, "*", options)
            .SelectMany(character => Directory.EnumerateDirectories(character, "*", options))
            .Where(mod => string.Equals(Path.GetFileName(mod), name, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>The user took over external folders: from now on they are the named Mods' deployments.</summary>
    private void AdoptCopies(WorkspaceRepository repository, IEnumerable<string> deployedPaths)
    {
        CopyDeploymentLedger ledger = CopyLedger(repository);
        foreach (string deployed in deployedPaths)
            if (SourceForDeployedFolder(repository, deployed) is string source) ledger.Adopt(source, deployed);
        SaveShellConfig();
    }
}
