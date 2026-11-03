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

    /// <summary>Average perceived brightness (0–255) of BGRA pixels, sampling every 8th pixel.</summary>
    public static double AverageLuminance(byte[] bgra)
    {
        const int step = 8 * 4;
        double total = 0;
        int samples = 0;
        for (int i = 0; i + 2 < bgra.Length; i += step)
        {
            total += 0.114 * bgra[i] + 0.587 * bgra[i + 1] + 0.299 * bgra[i + 2];
            samples++;
        }
        return samples == 0 ? 0 : total / samples;
    }

    /// <summary>
    /// Whether text over a scene this bright should be dark. The two thresholds keep a scene near the middle
    /// from making the text flip back and forth.
    /// </summary>
    public static bool PrefersDarkText(double luminance, bool darkNow) => darkNow ? luminance > 110 : luminance > 165;

    // Dark text needs a light backing and shadow, otherwise both would hide it.
    public static bool UsesLightBacking(byte red, byte green, byte blue) =>
        0.299 * red + 0.587 * green + 0.114 * blue < 128;
}
