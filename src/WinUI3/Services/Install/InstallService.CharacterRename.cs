using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

// Issue #108: why a character rename cannot start (moved unchanged from the window, stage J finish, step 4). The
// migration of the references (presets, slots, bindings and the rest) is CharacterRenameReferences (step 8b).
internal sealed partial class InstallService
{
    public string CharacterRenameBlockText(CharacterRenamePlan plan, WorkspaceRepository repository) => plan.Block switch
    {
        CharacterRenameBlock.InvalidName => T("LocalLibrary.RenameInvalidName"),
        CharacterRenameBlock.NameTaken => T("LocalLibrary.TheTargetNameAlreadyExistsChoose"),
        CharacterRenameBlock.SourceMissing => T("LocalLibrary.RenameSourceMissing", plan.OldPath),
        CharacterRenameBlock.LoaderUnavailable => T("LocalLibrary.RenameLoaderUnavailable", repository.TargetPath),
        CharacterRenameBlock.LinksUnreadable => T("LocalLibrary.RenameLinksUnreadable", string.Join(Environment.NewLine, plan.UnreadableLinks)),
        _ => string.Empty
    };

    /// <summary>The copy records of the renamed character name its new source path and, inside its folder, its new place.</summary>
    public void RenameCharacter(WorkspaceRepository repository, CharacterRenamePlan plan, ReferenceUndoLog log)
        => IntegratedModManager.Core.Deployment.CopyDeploymentLedger.RenameCharacter(CopyDeployments, repository.Id, plan, log);
}
