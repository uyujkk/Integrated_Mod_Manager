using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Issue #108: a character rename moves everything that names the character or a path inside it, as one operation
/// (CharacterRename): the links in the loader folder, presets, state slots, default slots, bindings, links, origins,
/// tags, per-character settings, copy-mode records and the character-follow references. A failure puts all of it back.
/// Moved from the window in stage J finish, step 8b; every reference now lives in a service.
/// </summary>
internal sealed class CharacterRenameReferences(AppServices services, WorkspaceRepository repository) : ICharacterRenameReferences
{
    private readonly ReferenceUndoLog _log = new();
    private string? _followFile;
    private string? _followBefore;
    private string? _followText;

    public void Migrate(CharacterRenamePlan plan)
    {
        MigrateCharacterReferences(plan);
        (_followFile, _followBefore, _followText) = services.ShortcutPanel.MigrateCharacterFollowProfile(repository, plan, _log);
    }

    public void Revert()
    {
        _log.Undo();
        _followText = _followBefore;
    }

    public void Save()
    {
        services.Configuration.WriteLegacyConfig();
        services.Configuration.SaveShell(throwOnError: true);
        if (_followFile is not null && _followText is not null) WriteFileAtomically(_followFile, _followText);
    }

    private void MigrateCharacterReferences(CharacterRenamePlan plan)
    {
        services.Combinations.RenameCharacter(repository, plan, _log);
        services.Slots.RenameCharacter(repository, plan, _log);

        _log.Rekey(services.ShortcutBindings.Bindings, plan.MapFullPath);
        _log.Rekey(services.ShortcutBindings.Links, plan.MapFullPath);
        services.Tracking.RenameCharacter(plan, _log);

        _log.Rekey(repository.CharacterAvatarBindings, plan.MapName);
        _log.Rekey(repository.CharacterSelectionOverrides, plan.MapName);
        services.Install.RenameCharacter(repository, plan, _log);
        services.Workspace.RenameCharacterTags(repository, plan, _log);
    }

    private static void WriteFileAtomically(string path, string text)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
    }
}
