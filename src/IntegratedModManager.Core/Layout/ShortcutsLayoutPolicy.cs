namespace IntegratedModManager.Core.Layout;

/// <summary>Breakpoints of the shortcuts page, measured on the page's own width.</summary>
public static class ShortcutsLayoutPolicy
{
    /// <summary>Width of the settings column beside the bindings.</summary>
    public const double SettingsColumnWidth = 360;

    /// <summary>The settings column sits beside the bindings; below this it folds above them.</summary>
    public static bool SettingsBesideBindings(double pageWidth) => pageWidth >= 1100;

    /// <summary>The Mod and window pickers share one row with the send toggle; below this they stack.</summary>
    public static bool ToolbarInOneRow(double pageWidth) => pageWidth >= 860;

    /// <summary>Binding cards per row: one per ~280 DIP of list width, at most four.</summary>
    public static int BindingColumns(double listWidth) => Math.Clamp((int)((listWidth + 8) / 288), 1, 4);
}
