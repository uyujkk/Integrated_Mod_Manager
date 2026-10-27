namespace IntegratedModManager.Core.Repository;

public enum RepositoryWorkspaceView { List, Covers, Presets }

public readonly record struct RepositoryWorkspaceLayout(
    double CategoryWidth, double DetailWidth, bool InlineDetails, int CoverColumns, double CoverWidth);

/// <summary>Cover cards in the gallery: the repeater width, the row length and the card sizes.</summary>
public readonly record struct RepositoryCoverGrid(
    double GalleryWidth, int Columns, double MinItemWidth, double ItemHeight, double PreviewHeight);

/// <summary>Why the Mod list shows nothing, which decides its message and whether it offers an import.</summary>
public enum ModListEmptyKind { None, NoMatches, NoMods, NoCharacter }

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

    /// <summary>Items whose name contains the trimmed keyword, ignoring case, in their original order.</summary>
    public static IEnumerable<T> FilterByName<T>(IEnumerable<T> items, Func<T, string> name, string? keyword)
    {
        string term = (keyword ?? string.Empty).Trim();
        return term.Length == 0 ? items : items.Where(item => name(item).Contains(term, StringComparison.CurrentCultureIgnoreCase));
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

    /// <summary>
    /// Cards keep a 236 px minimum and stretch to fill the row (up to eight); their height
    /// follows the viewer so a short window still shows a whole row, and the preview takes
    /// what the title and status rows leave.
    /// </summary>
    public static RepositoryCoverGrid CoverGrid(double viewerWidth, double viewerHeight)
    {
        double width = double.IsFinite(viewerWidth) ? viewerWidth : 0;
        double height = double.IsFinite(viewerHeight) ? viewerHeight : 0;
        double gallery = Math.Max(140, width - 24);
        int columns = Math.Clamp((int)Math.Floor((gallery + 8) / 244), 1, 8);
        double itemHeight = Math.Clamp(height - 8, 140, 240);
        return new(gallery, columns, Math.Min(236, gallery), itemHeight, Math.Clamp(itemHeight - 86, 50, 140));
    }

    public static ModListEmptyKind EmptyKind(int totalMods, int visibleMods, bool hasCharacter)
        => visibleMods > 0 ? ModListEmptyKind.None
            : totalMods > 0 ? ModListEmptyKind.NoMatches
            : hasCharacter ? ModListEmptyKind.NoMods
            : ModListEmptyKind.NoCharacter;
}
