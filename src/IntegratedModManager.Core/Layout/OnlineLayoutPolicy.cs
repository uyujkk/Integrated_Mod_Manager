namespace IntegratedModManager.Core.Layout;

// The names are stored in browse sessions (OnlineBrowseSession.Layout), so they must not change.
public enum OnlineCardLayoutMode
{
    List,
    Grid
}

/// <param name="Collapsed">No rail: the character list moves into the toolbar.</param>
public readonly record struct OnlineCharacterRailLayout(bool Horizontal, double ColumnWidth, bool Collapsed = false);

public readonly record struct OnlineGridTileLayout(int Columns, double ItemWidth, double ItemHeight, double ImageHeight);

public static class OnlineLayoutPolicy
{
    public const double PreferredTileWidth = 300;
    public const int MaxTileColumns = 8;
    // Title (two lines), author/status row, stats/actions row and padding below the image.
    public const double TileBodyHeight = 128;
    // The tile keeps a 5 px margin on every side inside its grid cell.
    public const double TileMargin = 5;

    /// <summary>Below this content width a side rail would leave the results too narrow for the toolbar.</summary>
    public const double CollapsedRailWidth = 720;

    /// <summary>The search box moves to its own toolbar row rather than shrink below this width.</summary>
    public const double MinSearchWidth = 240;

    /// <summary>The header status keeps at least this much room before the refresh button drops its label.</summary>
    public const double MinHeaderStatusWidth = 120;

    /// <summary>A short window must reserve height for the Mod list, not a second toolbar row.</summary>
    public static OnlineCharacterRailLayout CharacterRail(double contentWidth, double windowHeight)
    {
        if (contentWidth < CollapsedRailWidth) return new(false, 0, true);
        bool narrow = contentWidth < 1040;
        double width = narrow ? 152 : contentWidth < 1600 ? 188 : 220;
        return new(narrow && windowHeight >= 740, width);
    }

    /// <summary>
    /// Grid tiles fill the row exactly; the preview keeps a 16:9 shape, so wider tiles
    /// get taller images instead of empty space under the text.
    /// </summary>
    public static OnlineGridTileLayout GridTiles(double availableWidth)
    {
        double width = double.IsFinite(availableWidth) && availableWidth > 0 ? availableWidth : PreferredTileWidth;
        int columns = Math.Clamp((int)Math.Floor(width / PreferredTileWidth), 1, MaxTileColumns);
        double itemWidth = Math.Floor(width / columns);
        double imageHeight = Math.Clamp(Math.Round((itemWidth - 2 * TileMargin) * 9 / 16), 120, 240);
        return new(columns, itemWidth, imageHeight + TileBodyHeight + 2 * TileMargin, imageHeight);
    }

    /// <summary>Search, sort and the filter buttons share one row while the search box keeps its minimum width.</summary>
    public static bool ToolbarOnTwoRows(double resultsWidth, double controlsWidth)
        => resultsWidth - controlsWidth < MinSearchWidth;

    /// <summary>The refresh button shows its label only when the status beside it keeps its minimum room.</summary>
    public static bool ShowRefreshLabel(double headerWidth, double otherWidth, double labelWidth)
        => headerWidth - otherWidth - labelWidth >= MinHeaderStatusWidth;
}
