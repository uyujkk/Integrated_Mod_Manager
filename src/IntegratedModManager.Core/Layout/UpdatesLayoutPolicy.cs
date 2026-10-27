namespace IntegratedModManager.Core.Layout;

/// <summary>Breakpoints of the updates page, measured on the page's own width.</summary>
public static class UpdatesLayoutPolicy
{
    /// <summary>3: tracked Mods, download history, safety and backups; 2: tracked Mods beside the rest; 1: stacked.</summary>
    public static int Columns(double pageWidth) => pageWidth >= 1700 ? 3 : pageWidth >= 1100 ? 2 : 1;

    // Star widths of the tracked Mods column against the others in each mode.
    private static double TrackedModsWidth(double pageWidth) => Columns(pageWidth) switch
    {
        3 => pageWidth * 1.3 / 3.3,
        2 => pageWidth * 1.35 / 2.2,
        _ => pageWidth
    };

    /// <summary>The tracked Mods list shows two cards per row once its column fits them.</summary>
    public static bool TrackedModsInTwoColumns(double pageWidth) => TrackedModsWidth(pageWidth) >= 880;
}
