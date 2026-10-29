using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;

namespace ModFolderCopier.WinUI;

// A Mod new to a repository gets a state slot named "Default" holding its declared defaults, so it has
// a preset from the start. DefaultSlotPolicy decides which Mods are new; the slot's name follows the
// UI language until the user renames it.
public sealed partial class MainWindow
{
    // Repository id -> Mods (relative to the repository) already offered a default slot.
    private readonly Dictionary<string, HashSet<string>> _defaultSlotSeeds = new(StringComparer.Ordinal);

    private async Task SeedDefaultSlotsAsync(WorkspaceRepository repository, IReadOnlyList<FirstLevelFolderItem> characters)
    {
        try
        {
            string[] modPaths = characters.SelectMany(character => character.Children).Select(mod => mod.Path).ToArray();
            string[]? known = _defaultSlotSeeds.TryGetValue(repository.Id, out HashSet<string>? seen) ? [.. seen] : null;
            var found = await Task.Run(() =>
            {
                var mods = modPaths.Select(path => (Path: path, Relative: TryGetCurrentModRelativePath(repository, path)))
                    .Where(mod => mod.Relative is not null).ToArray();
                IReadOnlyList<string> fresh = DefaultSlotPolicy.NewMods(mods.Select(mod => mod.Relative!), known);
                var defaults = new List<(string Relative, IReadOnlyList<ModPersistentValue> Values)>();
                foreach (var mod in mods.Where(mod => fresh.Contains(mod.Relative!, StringComparer.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var values = ModPersistentPresetEngine.DeclaredDefaults(mod.Path, ModDeploymentLayout.DeployedName(mod.Path));
                        if (values.Count > 0) defaults.Add((mod.Relative!, values));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        LogApplicationIssue("Read Mod defaults for a default slot", ex);
                    }
                }
                return (All: mods.Select(mod => mod.Relative!).ToArray(), Defaults: defaults);
            });
            if (_onlineWindowClosed || !ReferenceEquals(GetSelectedRepository(), repository)) return;

            bool firstScan = !_defaultSlotSeeds.TryGetValue(repository.Id, out HashSet<string>? seeds);
            seeds ??= _defaultSlotSeeds[repository.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int before = seeds.Count;
            seeds.UnionWith(found.All);
            int created = 0;
            foreach ((string relative, IReadOnlyList<ModPersistentValue> values) in found.Defaults)
            {
                if (_modPersistentSlots.Any(slot => slot.RepositoryId == repository.Id
                    && string.Equals(slot.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))) continue;
                _modPersistentSlots.Add(new ModPersistentSlot
                {
                    RepositoryId = repository.Id, ModRelativePath = relative,
                    Name = T("PersistentSlots.DefaultSlotName"), IsModDefault = true, Values = [.. values]
                });
                created++;
            }
            if (firstScan || seeds.Count != before || created > 0) SaveShellConfig();
            if (created > 0) RefreshPersistentSlotControls();
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Create default slots", ex);
        }
    }

    // Called on a language change: untouched default slots take the new language's name.
    private void RelabelDefaultSlots()
    {
        string[] names = [UiText.Chinese("PersistentSlots.DefaultSlotName"), UiText.English("PersistentSlots.DefaultSlotName")];
        string wanted = T("PersistentSlots.DefaultSlotName");
        bool changed = false;
        foreach (ModPersistentSlot slot in _modPersistentSlots.Where(slot => slot.IsModDefault))
        {
            var others = _modPersistentSlots.Where(other => other != slot && other.RepositoryId == slot.RepositoryId
                && string.Equals(other.ModRelativePath, slot.ModRelativePath, StringComparison.OrdinalIgnoreCase)).Select(other => other.Name);
            if (DefaultSlotPolicy.Relabel(slot.Name, names, wanted, others) is { } name)
            {
                slot.Name = name;
                changed = true;
            }
        }
        if (changed) SaveShellConfig();
    }
}
