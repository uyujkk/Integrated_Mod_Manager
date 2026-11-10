using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;

namespace ModFolderCopier.WinUI;

// Each Mod gets a state slot named "Default" holding its declared defaults: once d3dx_user.ini holds other
// values, it is the only way back to them. DefaultSlotPolicy decides which Mods need one; the slot's name
// follows the UI language until the user renames it.
public sealed partial class MainWindow
{
    // Repository id -> Mods (relative to the repository) already offered a default slot.
    private readonly Dictionary<string, HashSet<string>> _defaultSlotSeeds = new(StringComparer.Ordinal);
    // Repositories whose Mods have all been offered a default slot, including Mods seen before default slots existed.
    private readonly HashSet<string> _defaultSlotsBackfilled = new(StringComparer.Ordinal);

    // Before the first preset of a Mod is saved. A scan normally got there first; this covers a Mod saved before it.
    private async Task EnsureDefaultSlotAsync(WorkspaceRepository repository, string modPath, string relative)
    {
        bool known = _defaultSlotSeeds.TryGetValue(repository.Id, out HashSet<string>? seen) && seen.Contains(relative);
        if (DefaultSlotPolicy.ModsNeedingDefault([relative], known ? [relative] : [], _defaultSlotsBackfilled.Contains(repository.Id),
                DefaultSlotMods(repository)).Count == 0) return;
        try
        {
            IReadOnlyList<ModPersistentValue> values = await Task.Run(() =>
                ModPersistentPresetEngine.DeclaredDefaults(modPath, ModDeploymentLayout.DeployedName(modPath)));
            AddDefaultSlot(repository, relative, values);
            (_defaultSlotSeeds.TryGetValue(repository.Id, out HashSet<string>? seeds) ? seeds
                : _defaultSlotSeeds[repository.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(relative);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogApplicationIssue("Read Mod defaults for a default slot", ex);
        }
    }

    private IEnumerable<string> DefaultSlotMods(WorkspaceRepository repository) => _modPersistentSlots
        .Where(slot => slot.IsModDefault && slot.RepositoryId == repository.Id).Select(slot => slot.ModRelativePath);

    private bool AddDefaultSlot(WorkspaceRepository repository, string relative, IReadOnlyList<ModPersistentValue> values)
    {
        if (values.Count == 0 || DefaultSlotMods(repository).Contains(relative, StringComparer.OrdinalIgnoreCase)) return false;
        _modPersistentSlots.Add(new ModPersistentSlot
        {
            RepositoryId = repository.Id,
            ModRelativePath = relative,
            Name = T("PersistentSlots.DefaultSlotName"),
            IsModDefault = true,
            Values = [.. values]
        });
        return true;
    }

    // A default slot restores what the Mod declares now, so it follows a Mod update that changed its defaults.
    // Falls back to the stored values when the Mod cannot be read or declares nothing any more.
    private static IReadOnlyList<ModPersistentValue> ValuesToRestore(ModPersistentSlot slot, string modPath)
    {
        if (!slot.IsModDefault) return slot.Values;
        try
        {
            IReadOnlyList<ModPersistentValue> current = ModPersistentPresetEngine.DeclaredDefaults(modPath, ModDeploymentLayout.DeployedName(modPath));
            return current.Count > 0 ? current : slot.Values;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogApplicationIssue("Read current Mod defaults", ex);
            return slot.Values;
        }
    }

    private async Task SeedDefaultSlotsAsync(WorkspaceRepository repository, IReadOnlyList<FirstLevelFolderItem> characters)
    {
        try
        {
            string[] modPaths = characters.SelectMany(character => character.Children).Select(mod => mod.Path).ToArray();
            string[]? known = _defaultSlotSeeds.TryGetValue(repository.Id, out HashSet<string>? seen) ? [.. seen] : null;
            bool backfilled = _defaultSlotsBackfilled.Contains(repository.Id);
            string[] withDefault = [.. DefaultSlotMods(repository)];
            var found = await Task.Run(() =>
            {
                var mods = modPaths.Select(path => (Path: path, Relative: TryGetCurrentModRelativePath(repository, path)))
                    .Where(mod => mod.Relative is not null).ToArray();
                IReadOnlyList<string> fresh = DefaultSlotPolicy.ModsNeedingDefault(mods.Select(mod => mod.Relative!), known, backfilled, withDefault);
                var defaults = new List<(string Relative, IReadOnlyList<ModPersistentValue> Values)>();
                var unreadable = new List<string>();
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
                        unreadable.Add(mod.Relative!);
                    }
                }
                return (All: DefaultSlotPolicy.Seen(mods.Select(mod => mod.Relative!), unreadable), Defaults: defaults);
            });
            if (_onlineView._onlineWindowClosed || !ReferenceEquals(GetSelectedRepository(), repository)) return;

            bool firstScan = !_defaultSlotSeeds.TryGetValue(repository.Id, out HashSet<string>? seeds) || _defaultSlotsBackfilled.Add(repository.Id);
            seeds ??= _defaultSlotSeeds[repository.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int before = seeds.Count;
            seeds.UnionWith(found.All);
            int created = 0;
            _defaultSlotsBackfilled.Add(repository.Id);
            foreach ((string relative, IReadOnlyList<ModPersistentValue> values) in found.Defaults)
            {
                if (AddDefaultSlot(repository, relative, values)) created++;
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
        string[] names = UiText.Languages.Select(language => UiText.Get(language, "PersistentSlots.DefaultSlotName")).Distinct().ToArray();
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
