namespace IntegratedModManager.Core.Layout;

/// <summary>Breakpoints of the dashboard page, measured on the page's own width.</summary>
public static class DashboardLayoutPolicy
{
    /// <summary>Paths and the repository list side by side; below this they stack.</summary>
    public static bool UsesTwoColumns(double pageWidth) => pageWidth >= 1100;

    /// <summary>The current-repository strip keeps one row; below this its status and buttons wrap under the picker.</summary>
    public static bool StripFitsOneRow(double pageWidth) => pageWidth >= 860;
}
