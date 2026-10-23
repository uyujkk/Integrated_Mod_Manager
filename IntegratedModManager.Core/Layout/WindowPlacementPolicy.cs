namespace IntegratedModManager.Core.Layout;

public readonly record struct WindowRect(int X, int Y, int Width, int Height);

/// <summary>
/// Normal (non-maximized) window bounds in physical pixels. A normal window that fills the
/// screen looks maximized and leaves nothing to return to, so such bounds — and missing ones —
/// become a window sized to a fixed share of the work area, centered on it.
/// </summary>
public static class WindowPlacementPolicy
{
    public const double NormalShare = 0.8;
    public const double FillsShare = 0.95;
    public const int MinimumWidth = 1100;
    public const int MinimumHeight = 700;

    public static bool FillsWorkArea(WindowRect window, WindowRect workArea)
        => window.Width >= workArea.Width * FillsShare && window.Height >= workArea.Height * FillsShare;

    public static WindowRect Proportional(WindowRect workArea)
    {
        int width = Math.Min(workArea.Width, Math.Max(MinimumWidth, (int)Math.Round(workArea.Width * NormalShare)));
        int height = Math.Min(workArea.Height, Math.Max(MinimumHeight, (int)Math.Round(workArea.Height * NormalShare)));
        return new WindowRect(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height);
    }

    // Saved normal bounds, kept inside the work area; missing or screen-filling bounds become proportional.
    public static WindowRect Normal(WindowRect? saved, WindowRect workArea)
    {
        if (saved is not WindowRect window || FillsWorkArea(window, workArea))
        {
            return Proportional(workArea);
        }

        int width = Math.Min(Math.Max(MinimumWidth, window.Width), workArea.Width);
        int height = Math.Min(Math.Max(MinimumHeight, window.Height), workArea.Height);
        int x = Math.Clamp(window.X, workArea.X, workArea.X + workArea.Width - width);
        int y = Math.Clamp(window.Y, workArea.Y, workArea.Y + workArea.Height - height);
        WindowRect clamped = new(x, y, width, height);
        return FillsWorkArea(clamped, workArea) ? Proportional(workArea) : clamped;
    }
}
