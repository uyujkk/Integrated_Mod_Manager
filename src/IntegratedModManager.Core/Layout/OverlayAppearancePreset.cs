namespace IntegratedModManager.Core.Layout;

/// <summary>A one-click combination of the overlay's own settings. Text colour is left to the user.</summary>
public sealed record OverlayAppearancePreset(string Id, int BackgroundOpacity, int ElementOpacity, bool CompactView)
{
    public static readonly IReadOnlyList<OverlayAppearancePreset> All =
    [
        new("clear", 85, 90, false),
        new("balanced", 50, 70, false),
        new("minimal", 15, 45, true)
    ];
}
