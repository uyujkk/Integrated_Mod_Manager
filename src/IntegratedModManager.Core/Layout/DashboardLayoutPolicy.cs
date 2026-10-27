namespace IntegratedModManager.Core.Layout;

/// <summary>Breakpoints of the dashboard page, measured on the page's own width.</summary>
public static class DashboardLayoutPolicy
{
    /// <summary>2: two columns (summary and main area side by side); 1: one column; 0: narrow one column.</summary>
    public static int Mode(double pageWidth)
        => pageWidth >= 1460 ? 2 : pageWidth >= 860 ? 1 : 0;

    public static int MetricColumns(double pageWidth)
        => pageWidth < 620 ? 1 : 3;

    // In the two-column mode the presets fill a narrow side column, one per row.
    public static int PresetColumns(double pageWidth)
        => Mode(pageWidth) switch
        {
            2 => 1,
            1 => 3,
            _ => pageWidth < 680 ? 1 : 2
        };
}
