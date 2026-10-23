namespace IntegratedModManager.Core;

public sealed class OnlinePreviewCandidate
{
    public string Url { get; set; } = string.Empty;

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public long FileSizeBytes { get; set; }

    public bool IsFallbackThumbnail { get; set; }

    public int SourceOrder { get; set; }
}

public static class OnlinePreviewSelectionPolicy
{
    public static OnlinePreviewCandidate? SelectBest(IEnumerable<OnlinePreviewCandidate>? candidates)
    {
        return candidates?
            .Where(IsUsable)
            .OrderByDescending(IsClearEnough)
            .ThenBy(candidate => candidate.IsFallbackThumbnail)
            .ThenByDescending(candidate => Math.Min(candidate.PixelWidth, candidate.PixelHeight))
            .ThenByDescending(GetPixelCount)
            .ThenByDescending(candidate => candidate.FileSizeBytes)
            .ThenBy(candidate => candidate.SourceOrder)
            .FirstOrDefault();
    }

    public static bool IsClearEnough(OnlinePreviewCandidate candidate)
    {
        if (!IsUsable(candidate))
        {
            return false;
        }

        int shortestEdge = Math.Min(candidate.PixelWidth, candidate.PixelHeight);
        int longestEdge = Math.Max(candidate.PixelWidth, candidate.PixelHeight);
        double aspectRatio = (double)longestEdge / shortestEdge;
        return shortestEdge >= 360
            && longestEdge >= 720
            && GetPixelCount(candidate) >= 300_000
            && aspectRatio <= 3.2d;
    }

    private static bool IsUsable(OnlinePreviewCandidate candidate)
    {
        return candidate is not null
            && !string.IsNullOrWhiteSpace(candidate.Url)
            && candidate.PixelWidth > 0
            && candidate.PixelHeight > 0
            && candidate.FileSizeBytes > 0;
    }

    private static long GetPixelCount(OnlinePreviewCandidate candidate)
    {
        return (long)candidate.PixelWidth * candidate.PixelHeight;
    }
}
