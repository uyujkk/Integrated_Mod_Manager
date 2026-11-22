using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

internal sealed partial class WorkspaceService
{
    /// <summary>
    /// A Mod's deployment state in the loader folder: not configured, not deployed, linked or copied (stage J finish,
    /// step 7b: moved from the workspace page unchanged, for the window's lists without the page).
    /// </summary>
    public string FolderCopyState(string targetDir, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return T("Localization.NotConfigured");
        }

        string targetPath = ModDeploymentLayout.DeployedPath(targetDir, directoryPath);
        if (!Directory.Exists(targetPath))
        {
            return T("Localization.NotDeployed");
        }

        return DirectoryLinkDeployment.IsDirectoryLink(targetPath) ? T("Localization.Linked") : T("Localization.Copied");
    }

    /// <summary>A character of the repository was renamed (#108): its local tags and tag filters name the new folder.</summary>
    public void RenameCharacterTags(WorkspaceRepository repository, CharacterRenamePlan plan, ReferenceUndoLog log)
    {
        if (LocalLibraries.TryGetValue(WorkspaceIdentity.Library(repository.Id, repository.SourcePath), out LocalLibraryState? tags))
        {
            log.Rekey(tags.ModTags, plan.MapRelative);
            foreach (LocalModFilter filter in tags.Filters)
                log.Rewrite(() => filter.Character, value => filter.Character = value, plan.MapRelative);
        }
    }
}
