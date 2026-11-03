namespace IntegratedModManager.Core.Presets;

/// <summary>
/// Every Mod gets one slot holding its declared defaults, the only way back once d3dx_user.ini holds
/// other values. A repository is back-filled once: each Mod without a default slot gets one, including
/// Mods that already had other slots. After that only Mods it has never seen do, so a default slot the
/// user deleted does not come back.
/// </summary>
public static class DefaultSlotPolicy
{
    // Mods (paths relative to the repository) to give a default slot now. Known is null before a repository's first scan.
    public static IReadOnlyList<string> ModsNeedingDefault(IEnumerable<string> current, IReadOnlyCollection<string>? known,
        bool backfilled, IEnumerable<string> withDefault)
    {
        var have = new HashSet<string>(withDefault, StringComparer.OrdinalIgnoreCase);
        return current.Where(mod => !have.Contains(mod)
                && (!backfilled || known is null || !known.Contains(mod, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // An automatic default slot follows the UI language while it still has one of the default names;
    // returns the new name, or null to keep it (renamed by the user, already right, or taken by another slot).
    public static string? Relabel(string name, IReadOnlyCollection<string> defaultNames, string wanted, IEnumerable<string> otherNames)
        => name != wanted && defaultNames.Contains(name, StringComparer.Ordinal)
            && !otherNames.Contains(wanted, StringComparer.OrdinalIgnoreCase) ? wanted : null;
}
