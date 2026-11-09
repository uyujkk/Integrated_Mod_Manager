using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Presets;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Why the preview cleanup left something in place.</summary>
public enum PreviewCleanupSkipReason
{
    /// <summary>A junction or symbolic link: the cleanup never deletes through one.</summary>
    LinkedFolder,

    /// <summary>Not a file this store writes.</summary>
    NotAPreview
}

/// <param name="Link">The link that made the cleanup stop, when that was the reason.</param>
public sealed record PreviewCleanupSkip(string Path, PreviewCleanupSkipReason Reason, string? Link);

public sealed record PreviewCleanupResult(int Removed, IReadOnlyList<PreviewCleanupSkip> Skipped);

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

    /// <summary>
    /// Removes previews no preset refers to, and files left by an interrupted write. It deletes only what this store
    /// writes ("repository/preset.jpg" and its ".jpg.tmp"), never follows a junction or symbolic link (the folder
    /// itself, one of its parents or a repository folder), and keeps everything else; the result says what it left
    /// and why, for the log.
    /// </summary>
    public PreviewCleanupResult RemoveOrphans(IEnumerable<string?> kept)
    {
        var skipped = new List<PreviewCleanupSkip>();
        if (!Directory.Exists(_root)) return new PreviewCleanupResult(0, skipped);
        if (LinkInPath(_root) is string link)
        {
            skipped.Add(new PreviewCleanupSkip(_root, PreviewCleanupSkipReason.LinkedFolder, link));
            return new PreviewCleanupResult(0, skipped);
        }
        var keep = kept.Select(Resolve).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        foreach (string folder in Directory.GetDirectories(_root))
        {
            if (IsLink(folder))
            {
                skipped.Add(new PreviewCleanupSkip(folder, PreviewCleanupSkipReason.LinkedFolder, folder));
                continue;
            }
            if (!IsSegment(Path.GetFileName(folder)))
            {
                skipped.Add(new PreviewCleanupSkip(folder, PreviewCleanupSkipReason.NotAPreview, null));
                continue;
            }
            foreach (string entry in Directory.GetFileSystemEntries(folder))
            {
                if (Directory.Exists(entry))
                {
                    skipped.Add(new PreviewCleanupSkip(entry, IsLink(entry) ? PreviewCleanupSkipReason.LinkedFolder : PreviewCleanupSkipReason.NotAPreview, null));
                    continue;
                }
                if (keep.Contains(entry)) continue;
                if (!IsOwnedFile(Path.GetFileName(entry)) || IsLink(entry))
                {
                    skipped.Add(new PreviewCleanupSkip(entry, PreviewCleanupSkipReason.NotAPreview, null));
                    continue;
                }
                File.Delete(entry);
                removed++;
            }
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        return new PreviewCleanupResult(removed, skipped);
    }

    // A preview this store writes, or the temporary file of an interrupted write.
    private static bool IsOwnedFile(string name)
    {
        if (name.EndsWith(Extension + ".tmp", StringComparison.Ordinal)) name = name[..^".tmp".Length];
        return name.EndsWith(Extension, StringComparison.Ordinal) && IsSegment(name[..^Extension.Length]);
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    // The first junction or symbolic link on the way to the folder (the folder included), or null.
    private static string? LinkInPath(string folder)
    {
        for (DirectoryInfo? current = new(folder); current is not null; current = current.Parent)
        {
            if (current.Exists && IsLink(current.FullName)) return current.FullName;
        }
        return null;
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
