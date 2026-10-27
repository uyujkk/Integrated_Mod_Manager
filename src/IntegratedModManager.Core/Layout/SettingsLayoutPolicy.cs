namespace IntegratedModManager.Core.Layout;

public static class SettingsLayoutPolicy
{
    // 3: two-column overview of every card, top-aligned at their own heights; the page scrolls,
    // so height no longer decides the mode. 2: section list beside one card; 1: section grid above it.
    public static int Mode(double contentWidth)
        => contentWidth >= 1080 ? 3 : contentWidth >= 760 ? 2 : 1;

    public static double FocusHeight(double viewportHeight, double headerHeight, double navigationHeight)
        => Math.Max(1, viewportHeight - headerHeight - navigationHeight - 28);
}
