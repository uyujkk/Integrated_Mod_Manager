using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Presets;

/// <summary>
/// Persist variables that hold the state of a Mod's own on-screen menu rather than how the character
/// looks: whether the menu is open, its page, and where its panels sit. Restoring them would reopen or
/// move the menu. Deliberately narrow: "slider" or "page" alone can be a look setting, and a colour
/// wheel's position is the chosen colour, so those are kept.
/// </summary>
public static partial class TransientVariablePolicy
{
    public static bool IsTransient(string variable)
    {
        string name = variable.TrimStart('$');
        return ExactNames().IsMatch(name) || (LayoutCoordinate().IsMatch(name) && !name.Contains("colorwheel", StringComparison.OrdinalIgnoreCase));
    }

    public static (IReadOnlyList<ModPersistentValue> Kept, IReadOnlyList<ModPersistentValue> Transient) Split(IReadOnlyList<ModPersistentValue> values)
    {
        ModPersistentValue[] transient = values.Where(value => IsTransient(value.Variable)).ToArray();
        // Never empty a slot: a Mod whose every value looks like menu state keeps them all.
        return transient.Length == values.Count ? (values, []) : (values.Except(transient).ToArray(), transient);
    }

    // The menu switch itself, its page and its first-start flag.
    [GeneratedRegex(@"^(menu|gui|page|current_?page(_?number)?|firstload|menu_?orientation|menu_?ratio|select_?loc_?(col|row))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExactNames();

    // Where a menu, icon or panel is drawn: menulocx, pageiconwidthy, panel1offsetx, helpmenulocy.
    [GeneratedRegex(@"(menu|icon|page|panel\d*|randomize(auto)?)\w*?(loc|width|spacing|offset)_?[xy]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LayoutCoordinate();
}
