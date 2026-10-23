namespace IntegratedModManager.Core;

public readonly record struct DialogViewport(double MaxWidth, double ContentHeight);

/// <summary>Logical-pixel budget for bounded lists; commands remain outside the scroll region.</summary>
public static class DialogViewportPolicy
{
    public static DialogViewport Selection(double windowWidth, double windowHeight)
    {
        double width = double.IsFinite(windowWidth) ? Math.Max(0, windowWidth) : 0;
        double height = double.IsFinite(windowHeight) ? Math.Max(0, windowHeight) : 0;
        return new(Math.Clamp(width - 96, 280, 960), Math.Clamp(height - 260, 160, 580));
    }
}
