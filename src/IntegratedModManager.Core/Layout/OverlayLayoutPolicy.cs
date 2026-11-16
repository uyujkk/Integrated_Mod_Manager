namespace IntegratedModManager.Core.Layout;

/// <summary>Where the overlay was on one display, in DIP relative to that display's work area (saved in beta-shell.json).</summary>
public sealed class OverlayPlacement
{
    /// <summary>The display: its device name and resolution, so another monitor on the same port is a new display.</summary>
    public string Display { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }

    /// <summary>The tallest the user made it; the panel is as tall as its content up to this.</summary>
    public double MaxHeight { get; set; }
}

/// <summary>A display's work area in physical pixels and its scale (DPI / 96).</summary>
public sealed record OverlayDisplay(string Key, WindowRect WorkArea, double Scale);

/// <summary>
/// The overlay's size and place: remembered per display (last used first), restored on that display while it is
/// connected and inside its work area, otherwise on the primary display at the default spot; snapped to the work
/// area's edges while it is dragged near them. Sizes are DIP, so a scale change on the display keeps the look.
/// </summary>
public static class OverlayLayoutPolicy
{
    public const double DefaultWidth = 420, DefaultMaxHeight = 860, MinimumWidth = 300, MinimumHeight = 120, Margin = 20, SnapDistance = 16;

    /// <summary>The display a key names, or null when it is not connected now.</summary>
    public static OverlayDisplay? Find(IReadOnlyList<OverlayDisplay> displays, string? key)
        => string.IsNullOrEmpty(key) ? null : displays.FirstOrDefault(display => display.Key == key);

    /// <summary>
    /// Where to open: the most recent placement whose display is connected, clamped into its work area; with none
    /// (first use, or its display was unplugged), the top right of the primary display at the default size.
    /// </summary>
    public static (WindowRect Bounds, double MaxHeight, OverlayDisplay Display) Restore(
        IReadOnlyList<OverlayPlacement> placements, IReadOnlyList<OverlayDisplay> displays, OverlayDisplay primary)
    {
        foreach (OverlayPlacement placement in placements)
        {
            if (Find(displays, placement.Display) is not { } display) continue;
            double maxHeight = Math.Max(MinimumHeight, placement.MaxHeight > 0 ? placement.MaxHeight : DefaultMaxHeight);
            return (Clamp(Bounds(display, placement.Left, placement.Top, placement.Width, maxHeight), display), maxHeight, display);
        }
        double scale = primary.Scale;
        double width = Math.Min(DefaultWidth, primary.WorkArea.Width / scale);
        double left = primary.WorkArea.Width / scale - width - Margin;
        return (Clamp(Bounds(primary, left, Margin, width, DefaultMaxHeight), primary), DefaultMaxHeight, primary);
    }

    /// <summary>The placement to remember for a window on a display (replacing that display's, moved to the front).</summary>
    public static List<OverlayPlacement> Remember(IReadOnlyList<OverlayPlacement> placements, WindowRect window, OverlayDisplay display, double maxHeight)
    {
        double scale = display.Scale;
        var placement = new OverlayPlacement
        {
            Display = display.Key,
            Left = Math.Round((window.X - display.WorkArea.X) / scale, 1),
            Top = Math.Round((window.Y - display.WorkArea.Y) / scale, 1),
            Width = Math.Round(window.Width / scale, 1),
            MaxHeight = Math.Round(Math.Max(MinimumHeight, maxHeight), 1)
        };
        var result = new List<OverlayPlacement> { placement };
        result.AddRange(placements.Where(other => other.Display != display.Key).Take(7));
        return result;
    }

    /// <summary>A window being dragged: an edge within <see cref="SnapDistance"/> DIP of the work area's edge sticks to it.</summary>
    public static WindowRect Snap(WindowRect moving, OverlayDisplay display)
    {
        double distance = SnapDistance * display.Scale;
        WindowRect area = display.WorkArea;
        int x = moving.X, y = moving.Y;
        if (Math.Abs(moving.X - area.X) <= distance) x = area.X;
        else if (Math.Abs(moving.X + moving.Width - (area.X + area.Width)) <= distance) x = area.X + area.Width - moving.Width;
        if (Math.Abs(moving.Y - area.Y) <= distance) y = area.Y;
        else if (Math.Abs(moving.Y + moving.Height - (area.Y + area.Height)) <= distance) y = area.Y + area.Height - moving.Height;
        return moving with { X = x, Y = y };
    }

    /// <summary>The display a window is mostly on (the largest overlap), or the nearest by its centre.</summary>
    public static OverlayDisplay? DisplayOf(IReadOnlyList<OverlayDisplay> displays, WindowRect window)
    {
        OverlayDisplay? best = null;
        long bestArea = 0;
        foreach (OverlayDisplay display in displays)
        {
            long overlap = Overlap(window, display.WorkArea);
            if (overlap > bestArea) (best, bestArea) = (display, overlap);
        }
        if (best is not null) return best;
        int cx = window.X + window.Width / 2, cy = window.Y + window.Height / 2;
        return displays.OrderBy(display => Distance(cx, cy, display.WorkArea)).FirstOrDefault();
    }

    /// <summary>The panel's height for its content: at least the minimum, at most what the user allowed and the work area.</summary>
    public static int FitHeight(double contentDip, double maxHeightDip, double scale, int workAreaHeight, int frame)
    {
        int wanted = (int)Math.Round(Math.Clamp(contentDip, MinimumHeight, Math.Max(MinimumHeight, maxHeightDip)) * scale) + frame;
        return Math.Max((int)Math.Round(MinimumHeight * scale), Math.Min(wanted, workAreaHeight - (int)Math.Round(Margin * 2 * scale)));
    }

    private static WindowRect Bounds(OverlayDisplay display, double left, double top, double width, double height)
    {
        double scale = display.Scale;
        return new WindowRect(display.WorkArea.X + (int)Math.Round(left * scale), display.WorkArea.Y + (int)Math.Round(top * scale),
            (int)Math.Round(Math.Max(MinimumWidth, width) * scale), (int)Math.Round(height * scale));
    }

    // Inside the work area: no larger than it, and moved in rather than shrunk.
    private static WindowRect Clamp(WindowRect window, OverlayDisplay display)
    {
        WindowRect area = display.WorkArea;
        int width = Math.Min(window.Width, area.Width), height = Math.Min(window.Height, area.Height);
        int x = Math.Clamp(window.X, area.X, area.X + area.Width - width);
        int y = Math.Clamp(window.Y, area.Y, area.Y + area.Height - height);
        return new WindowRect(x, y, width, height);
    }

    private static long Overlap(WindowRect a, WindowRect b)
    {
        long width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        long height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private static double Distance(int x, int y, WindowRect area)
    {
        double dx = Math.Max(0, Math.Max(area.X - x, x - (area.X + area.Width)));
        double dy = Math.Max(0, Math.Max(area.Y - y, y - (area.Y + area.Height)));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
