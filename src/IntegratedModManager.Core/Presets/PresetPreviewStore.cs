namespace IntegratedModManager.Core.Presets;

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
