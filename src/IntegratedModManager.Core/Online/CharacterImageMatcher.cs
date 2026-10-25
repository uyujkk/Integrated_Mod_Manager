namespace IntegratedModManager.Core.Online;

public sealed record CharacterScreenRegion(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
        && X >= 0 && Y >= 0 && Width >= .01 && Height >= .01 && X + Width <= 1.00001 && Y + Height <= 1.00001;
}

/// <summary>Small local HUD templates, with an unknown state and temporal confirmation.</summary>
public sealed class CharacterImageMatcher
{
    public const int Side = 24;
    public const int FeatureBytes = Side * Side * 3;
    private string? candidate;
    private int streak;
    public void Reset() { candidate = null; streak = 0; }

    public string? Observe(byte[] frame, IReadOnlyDictionary<string, byte[]> templates,
        out double confidence, double threshold = .94, double margin = .025)
    {
        confidence = 0;
        if (frame.Length != FeatureBytes || !HasDetail(frame)) { Reset(); return null; }
        var matches = templates.Where(p => p.Value.Length == FeatureBytes && HasDetail(p.Value))
            .Select(p => (p.Key, Score: Similarity(frame, p.Value)))
            .OrderByDescending(p => p.Score).ToArray();
        if (matches.Length == 0) { Reset(); return null; }
        confidence = matches[0].Score;
        if (confidence < threshold || (matches.Length > 1 && confidence - matches[1].Score < margin))
        { Reset(); return null; }
        if (candidate == matches[0].Key) streak++; else { candidate = matches[0].Key; streak = 1; }
        return streak >= 3 ? candidate : null;
    }

    public static bool HasDetail(byte[] pixels)
    {
        if (pixels.Length != FeatureBytes) return false;
        var luminance = Enumerable.Range(0, Side * Side)
            .Select(i => (pixels[i * 3] + pixels[i * 3 + 1] + pixels[i * 3 + 2]) / 3d).ToArray();
        double mean = luminance.Average();
        return luminance.Average(x => (x - mean) * (x - mean)) >= 100;
    }

    public static byte[] Extract(byte[] bgra, int width, int height, CharacterScreenRegion region)
    {
        if (!region.IsValid || width <= 0 || height <= 0 || (long)width * height * 4 != bgra.LongLength)
            throw new ArgumentException("Invalid image or region.");
        var result = new byte[FeatureBytes];
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                int px = Math.Clamp((int)((region.X + (x + .5) * region.Width / Side) * width), 0, width - 1);
                int py = Math.Clamp((int)((region.Y + (y + .5) * region.Height / Side) * height), 0, height - 1);
                for (int c = 0; c < 3; c++) result[(y * Side + x) * 3 + c] = bgra[(py * width + px) * 4 + c];
            }
        return result;
    }

    private static double Similarity(byte[] a, byte[] b)
    {
        long error = 0;
        for (int i = 0; i < a.Length; i++) error += Math.Abs(a[i] - b[i]);
        return 1 - error / (255d * a.Length);
    }
}
