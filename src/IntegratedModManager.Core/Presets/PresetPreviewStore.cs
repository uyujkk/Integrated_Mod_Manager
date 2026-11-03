using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Presets;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Preview images of presets, stored as "repository/preset.jpg" under one folder. Encoding is left to the window layer.</summary>
public sealed class PresetPreviewStore(string root)
{
    public const int MaxLongEdge = 640;
    private const string Extension = ".jpg";
    private readonly string _root = Path.GetFullPath(root);

    public string Write(string repositoryId, string presetId, byte[] jpeg)
    {
        string relative = $"{Segment(repositoryId)}/{Segment(presetId)}{Extension}";
        string path = Resolve(relative)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, jpeg);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
        return relative;
    }

    /// <summary>The full path of a stored value, or null when the value points anywhere but a preview in this folder.</summary>
    public string? Resolve(string? previewFile)
    {
        if (string.IsNullOrWhiteSpace(previewFile)) return null;
        string[] parts = previewFile.Split('/');
        if (parts.Length != 2 || !IsSegment(parts[0]) || !parts[1].EndsWith(Extension, StringComparison.Ordinal)
            || !IsSegment(parts[1][..^Extension.Length])) return null;
        return Path.Combine(_root, parts[0], parts[1]);
    }

    public void Delete(string? previewFile)
    {
        if (Resolve(previewFile) is string path) File.Delete(path);
    }

    /// <summary>Removes previews no preset refers to, and files left by an interrupted write. Returns how many were removed.</summary>
    public int RemoveOrphans(IEnumerable<string?> kept)
    {
        if (!Directory.Exists(_root)) return 0;
        var keep = kept.Select(Resolve).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        foreach (string folder in Directory.GetDirectories(_root))
        {
            foreach (string file in Directory.GetFiles(folder).Where(file => !keep.Contains(file)))
            {
                File.Delete(file);
                removed++;
            }
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        return removed;
    }

    public static (int Width, int Height) ThumbnailSize(int width, int height)
    {
        int longEdge = Math.Max(width, height);
        if (longEdge <= MaxLongEdge) return (width, height);
        return (Math.Max(1, width * MaxLongEdge / longEdge), Math.Max(1, height * MaxLongEdge / longEdge));
    }

    /// <summary>The part of a selection on screen that lies inside the game, relative to the game; null when too little remains.</summary>
    public static PixelRect? ClipToGame(PixelRect game, PixelRect selection)
    {
        const int minimumEdge = 32;
        int left = Math.Max(game.X, selection.X), top = Math.Max(game.Y, selection.Y);
        int right = Math.Min(game.X + game.Width, selection.X + selection.Width);
        int bottom = Math.Min(game.Y + game.Height, selection.Y + selection.Height);
        return right - left < minimumEdge || bottom - top < minimumEdge
            ? null : new PixelRect(left - game.X, top - game.Y, right - left, bottom - top);
    }

    /// <summary>A pixel area as shares of the game window, so a saved region survives a change of resolution.</summary>
    public static CharacterScreenRegion RegionOf(PixelRect area, int gameWidth, int gameHeight) => new(
        (double)area.X / gameWidth, (double)area.Y / gameHeight, (double)area.Width / gameWidth, (double)area.Height / gameHeight);

    /// <summary>The pixels of a saved region in a game window of this size; null when it is damaged or too small.</summary>
    public static PixelRect? RegionToPixels(CharacterScreenRegion region, int gameWidth, int gameHeight)
    {
        if (!region.IsValid) return null;
        int x = (int)Math.Round(region.X * gameWidth), y = (int)Math.Round(region.Y * gameHeight);
        return ClipToGame(new PixelRect(0, 0, gameWidth, gameHeight),
            new PixelRect(x, y, (int)Math.Round(region.Width * gameWidth), (int)Math.Round(region.Height * gameHeight)));
    }

    public static byte[] CropBgra(byte[] bgra, int frameWidth, PixelRect area)
    {
        byte[] cropped = new byte[checked(area.Width * area.Height * 4)];
        for (int row = 0; row < area.Height; row++)
            Buffer.BlockCopy(bgra, ((area.Y + row) * frameWidth + area.X) * 4, cropped, row * area.Width * 4, area.Width * 4);
        return cropped;
    }

    /// <summary>True for the black frame an exclusive full-screen game gives a desktop capture. Samples every 16th BGRA pixel.</summary>
    public static bool IsNearlyBlack(byte[] bgra)
    {
        const int threshold = 16, step = 16 * 4;
        int samples = 0, lit = 0;
        for (int i = 0; i + 2 < bgra.Length; i += step)
        {
            samples++;
            if (bgra[i] > threshold || bgra[i + 1] > threshold || bgra[i + 2] > threshold) lit++;
        }
        return lit * 100 < samples;
    }

    private static string Segment(string id) => IsSegment(id) ? id : throw new ArgumentException("The id cannot name a preview file.", nameof(id));

    private static bool IsSegment(string value) => value.Length is > 0 and <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
