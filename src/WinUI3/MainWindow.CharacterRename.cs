using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

// Issue #108: a character rename moves everything that names the character or a path inside it, as one operation
// (CharacterRename): the links in the loader folder, presets, state slots, default slots, bindings, links, origins,
// tags, per-character settings and the character-follow references. A failure puts all of it back.
public sealed partial class MainWindow
{
    private sealed class CharacterRenameReferences(MainWindow window, WorkspaceRepository repository) : ICharacterRenameReferences
    {
        private readonly ReferenceUndoLog _log = new();
        private string? _followFile;
        private string? _followBefore;
        private string? _followText;

        public void Migrate(CharacterRenamePlan plan)
        {
            window.MigrateCharacterReferences(repository, plan, _log);
            (_followFile, _followBefore, _followText) = window.MigrateCharacterFollowProfile(repository, plan, _log);
        }

        public void Revert()
        {
            _log.Undo();
            _followText = _followBefore;
        }

        public void Save()
        {
            window.WriteLegacyConfig();
            window.SaveShellConfig(throwOnError: true);
            if (_followFile is not null && _followText is not null) WriteFileAtomically(_followFile, _followText);
        }
    }

    private void MigrateCharacterReferences(WorkspaceRepository repository, CharacterRenamePlan plan, ReferenceUndoLog log)
    {
        foreach (ModConfigurationProfile profile in _configurationProfiles.Where(profile => profile.RepositoryId == repository.Id))
        {
            log.RewriteAll(profile.ModRelativePaths, plan.MapRelative);
            foreach (ModConfigurationPersistentState state in profile.PersistentStates)
                log.Rewrite(() => state.ModRelativePath, value => state.ModRelativePath = value, plan.MapRelative);
        }

        foreach (ModPersistentSlot slot in _modPersistentSlots.Where(slot => slot.RepositoryId == repository.Id))
            log.Rewrite(() => slot.ModRelativePath, value => slot.ModRelativePath = value, plan.MapRelative);
        if (_defaultSlotSeeds.TryGetValue(repository.Id, out HashSet<string>? seeds)) log.RewriteAll(seeds, plan.MapRelative);

        log.Rekey(_modBindings, plan.MapFullPath);
        log.Rekey(_modLinks, plan.MapFullPath);
        log.Rekey(_trackedModOrigins, plan.MapFullPath);
        foreach (TrackedModOrigin origin in _trackedModOrigins.Values)
            log.Rewrite(() => origin.Path, value => origin.Path = value, plan.MapFullPath);

        log.Rekey(repository.CharacterAvatarBindings, plan.MapName);
        log.Rekey(repository.CharacterSelectionOverrides, plan.MapName);
        if (_localLibraries.TryGetValue(WorkspaceIdentity.Library(repository.Id, repository.SourcePath), out LocalLibraryState? tags))
        {
            log.Rekey(tags.ModTags, plan.MapRelative);
            foreach (LocalModFilter filter in tags.Filters)
                log.Rewrite(() => filter.Character, value => filter.Character = value, plan.MapRelative);
        }
    }

    // The character-follow references of the repository: keyed by character name, with each character's preferred Mod.
    // Returns the file, its text before and after; the file is written by Save.
    private (string? File, string? Before, string? After) MigrateCharacterFollowProfile(WorkspaceRepository repository, CharacterRenamePlan plan, ReferenceUndoLog log)
    {
        string file = CharacterFollowProfilePath(repository.SourcePath);
        if (string.Equals(_characterProfilePath, file, StringComparison.OrdinalIgnoreCase))
        {
            log.Rekey(_characterProfile.Templates, plan.MapName);
            log.Rekey(_characterPreferredMods, plan.MapName);
            foreach (string role in _characterPreferredMods.Keys.ToArray())
                log.Rewrite(() => _characterPreferredMods[role], value => _characterPreferredMods[role] = value, plan.MapFullPath);
        }

        if (!File.Exists(file)) return (null, null, null);
        string before = File.ReadAllText(file);
        CharacterFollowProfile profile = JsonSerializer.Deserialize<CharacterFollowProfile>(before) ?? new();
        var scratch = new ReferenceUndoLog();
        profile.Templates = new(profile.Templates ?? [], StringComparer.OrdinalIgnoreCase);
        profile.PreferredMods = new(profile.PreferredMods ?? [], StringComparer.OrdinalIgnoreCase);
        scratch.Rekey(profile.Templates, plan.MapName);
        scratch.Rekey(profile.PreferredMods, plan.MapName);
        foreach (string role in profile.PreferredMods.Keys.ToArray())
            scratch.Rewrite(() => profile.PreferredMods[role], value => profile.PreferredMods[role] = value, plan.MapFullPath);
        return scratch.Count == 0 ? (null, null, null) : (file, before, JsonSerializer.Serialize(profile));
    }

    private static string CharacterFollowProfilePath(string repository)
    {
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repository.ToUpperInvariant())))[..24];
        return Path.Combine(AppContext.BaseDirectory, "character-references", name + ".json");
    }

    private static void WriteFileAtomically(string path, string text)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
    }

    private string CharacterRenameBlockText(CharacterRenamePlan plan, WorkspaceRepository repository) => plan.Block switch
    {
        CharacterRenameBlock.InvalidName => T("LocalLibrary.RenameInvalidName"),
        CharacterRenameBlock.NameTaken => T("LocalLibrary.TheTargetNameAlreadyExistsChoose"),
        CharacterRenameBlock.SourceMissing => T("LocalLibrary.RenameSourceMissing", plan.OldPath),
        CharacterRenameBlock.LoaderUnavailable => T("LocalLibrary.RenameLoaderUnavailable", repository.TargetPath),
        CharacterRenameBlock.LinksUnreadable => T("LocalLibrary.RenameLinksUnreadable", string.Join(Environment.NewLine, plan.UnreadableLinks)),
        _ => string.Empty
    };
}
