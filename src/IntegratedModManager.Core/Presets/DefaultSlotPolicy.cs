namespace IntegratedModManager.Core.Presets;

/// <summary>
/// A Mod that is new to a repository gets one slot holding its declared defaults. The repository
/// remembers which Mods it has seen, so a deleted default slot does not come back, and its first scan
/// only records the Mods it already had.
/// </summary>
public static class DefaultSlotPolicy
{
    // Mods (paths relative to the repository) seen for the first time. Known is null on a repository's first scan.
    public static IReadOnlyList<string> NewMods(IEnumerable<string> current, IReadOnlyCollection<string>? known)
        => known is null ? []
            : current.Where(mod => !known.Contains(mod, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // An automatic default slot follows the UI language while it still has one of the default names;
    // returns the new name, or null to keep it (renamed by the user, already right, or taken by another slot).
    public static string? Relabel(string name, IReadOnlyCollection<string> defaultNames, string wanted, IEnumerable<string> otherNames)
        => name != wanted && defaultNames.Contains(name, StringComparer.Ordinal)
            && !otherNames.Contains(wanted, StringComparer.OrdinalIgnoreCase) ? wanted : null;
}
