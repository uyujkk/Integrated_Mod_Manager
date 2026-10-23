namespace IntegratedModManager.Core;

public enum RepositoryWorkspaceView { List, Covers, Presets }

public readonly record struct RepositoryWorkspaceLayout(
    double CategoryWidth, double DetailWidth, bool InlineDetails, int CoverColumns, double CoverWidth);

/// <summary>Pure presentation rules; switching a view never changes a deployment or saved preset.</summary>
public static class RepositoryWorkspacePolicy
{
    // Reserve the majority of a short viewport for browsable presets and members.
    // Recovery details scroll independently; its primary action stays outside that scroll.
    public static double StackedRecoveryHeight(double viewportHeight)
        => Math.Clamp((double.IsFinite(viewportHeight) ? Math.Max(0, viewportHeight) : 0) * .35, 120, 220);

    public static bool InlineCombinationRecovery(double width, double height)
        => double.IsFinite(width) && (width >= 780
            || (width >= 720 && double.IsFinite(height) && height > 0 && height < 420));

    public static RepositoryWorkspaceLayout Layout(double availableWidth)
    {
        double width = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : 0;
        double category = width >= 1200 ? 208 : width >= 700 ? 184 : 156;
        bool inline = width >= 980;
        double detail = inline ? Math.Clamp(width * .30, 336, 480) : 0;
        double gallery = Math.Max(160, width - category - 12);
        int columns = Math.Clamp((int)Math.Floor((gallery + 12) / 236), 1, 8);
        double cover = Math.Max(140, (gallery - (columns - 1) * 12) / columns);
        return new(category, detail, inline, columns, cover);
    }

    public static bool MatchesQuery(string name, string category, string? query)
    {
        string[] terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || category.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static string? PreserveSelection(IEnumerable<string> paths, string? selectedPath)
    {
        string[] available = paths.ToArray();
        return available.FirstOrDefault(path => string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault();
    }
}
