namespace IntegratedModManager.Core.Layout;

/// <summary>Breakpoints of the updates page, measured on the page's own width.</summary>
public static class UpdatesLayoutPolicy
{
    /// <summary>
    /// 3: tracked Mods, download history, safety and backups; 2: tracked Mods beside the rest; 1: stacked.
    /// Compact list rows fit two columns from 900, so medium windows keep the page from scrolling.
    /// </summary>
    public static int Columns(double pageWidth) => pageWidth >= 1700 ? 3 : pageWidth >= 900 ? 2 : 1;

    /// <summary>
    /// The height the columns get when they sit side by side and the page itself does not scroll;
    /// null when the window is too short, and the page scrolls instead.
    /// </summary>
    public static double? FixedColumnHeight(double viewportHeight, double headerHeight)
    {
        double available = viewportHeight - headerHeight - 28;
        return double.IsFinite(available) && available >= 420 ? available : null;
    }
}
