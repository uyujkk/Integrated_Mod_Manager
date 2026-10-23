namespace IntegratedModManager.Core;

public static class SettingsLayoutPolicy
{
    // The overview contains two stacked cards per column plus the resources footer.
    // Width alone cannot establish that those cards fit without clipping.
    public static int Mode(double contentWidth, double viewportHeight)
        => contentWidth >= 1080 && viewportHeight >= 1040 ? 3
            : contentWidth >= 760 ? 2 : 1;

    public static double FocusHeight(double viewportHeight, double headerHeight, double navigationHeight)
        => Math.Max(1, viewportHeight - headerHeight - navigationHeight - 28);
}
