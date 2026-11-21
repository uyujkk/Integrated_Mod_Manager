using IntegratedModManager.Core.Deployment;

namespace ModFolderCopier.WinUI;

// Issue #107: which copied Mod folders in the loader folder the manager made. The records live in beta-shell.json
// (CopyDeploymentLedger), never as a file inside a Mod folder where the loader could read it. A same-named folder
// the manager cannot recognise is an external folder: shown, kept, and taken over only when the user says so.
// Moved unchanged from the window (stage J finish, step 4).
internal sealed partial class InstallService
{
    private readonly List<CopyDeploymentRecord> _copyDeployments = [];
    // Set when a planner claimed an old copy by its content on a background thread; saved on the next chance.
    private volatile bool _copyRecordsChanged;

    /// <summary>The records themselves, for the character rename migration (#108), which rewrites them in place.</summary>
    public List<CopyDeploymentRecord> CopyDeployments => _copyDeployments;

    public CopyDeploymentLedger CopyLedger(WorkspaceRepository repository) => new(repository.Id, repository.SourcePath, _copyDeployments);

    /// <summary>For the planners: whether a copied folder is the manager's copy of a source Mod.</summary>
    public Func<string, string, bool> OwnsCopy(WorkspaceRepository repository)
    {
        CopyDeploymentLedger ledger = CopyLedger(repository);
        return (source, target) =>
        {
            bool owned = ledger.Owns(source, target);
            if (ledger.Changed) _copyRecordsChanged = true;
            return owned;
        };
    }

    public void SaveCopyRecordsIfChanged()
    {
        if (!_copyRecordsChanged) return;
        _copyRecordsChanged = false;
        _saveShellConfig();
    }

    public List<CopyDeploymentRecord> CopyRecordsSnapshot()
    {
        lock (_copyDeployments) return [.. _copyDeployments];
    }

    /// <summary>The records read from beta-shell.json (not saved again).</summary>
    public void LoadCopyRecords(IEnumerable<CopyDeploymentRecord> records)
    {
        lock (_copyDeployments)
        {
            _copyDeployments.Clear();
            _copyDeployments.AddRange(records);
        }
    }

    public void RestoreCopyRecords(IReadOnlyCollection<CopyDeploymentRecord> records)
    {
        LoadCopyRecords(records);
        _saveShellConfig();
    }

    /// <summary>The user took over external folders: from now on they are the named Mods' deployments.</summary>
    public void AdoptCopies(WorkspaceRepository repository, IEnumerable<string> deployedPaths)
    {
        CopyDeploymentLedger ledger = CopyLedger(repository);
        foreach (string deployed in deployedPaths)
            if (CopyDeploymentLedger.SourceForDeployedFolder(repository.SourcePath, deployed) is string source) ledger.Adopt(source, deployed);
        _saveShellConfig();
    }
}
