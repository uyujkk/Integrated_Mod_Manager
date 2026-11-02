namespace IntegratedModManager.Core.Layout;

/// <summary>How readable the overlay stays over the game: element backing and text shadow.</summary>
public static class OverlayAppearancePolicy
{
    public const int DefaultElementOpacity = 70;
    public const int MaximumBackgroundOpacityWithShadow = 59;
    private const int MinimumHoverAlpha = 96;

    public static int ClampElementOpacity(int? percent) => Math.Clamp(percent ?? DefaultElementOpacity, 0, 100);

    public static byte ElementAlpha(int percent) => (byte)Math.Round(Math.Clamp(percent, 0, 100) * 2.55);

    // Pointer feedback must stay visible when the backing is turned down or off.
    public static byte HoverAlpha(int percent) => Math.Max(ElementAlpha(percent), (byte)MinimumHoverAlpha);

    public static bool ShowTextShadow(int backgroundPercent) => backgroundPercent <= MaximumBackgroundOpacityWithShadow;

    // Dark text needs a light backing and shadow, otherwise both would hide it.
    public static bool UsesLightBacking(byte red, byte green, byte blue) =>
        0.299 * red + 0.587 * green + 0.114 * blue < 128;
}
