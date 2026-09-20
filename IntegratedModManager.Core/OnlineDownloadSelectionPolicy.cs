namespace IntegratedModManager.Core;

public sealed class OnlineDownloadCandidate
{
    public string FileId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.MinValue;

    public bool IsArchived { get; set; }

    public bool IsSupportedArchive { get; set; }

    public string Version { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public static class OnlineDownloadSelectionPolicy
{
    public static OnlineDownloadCandidate? SelectDefault(IEnumerable<OnlineDownloadCandidate>? candidates)
    {
        List<OnlineDownloadCandidate> usable = GetUsable(candidates);
        return SelectNewest(usable.Where(candidate => candidate.IsSupportedArchive && !candidate.IsArchived))
            ?? SelectNewest(usable.Where(candidate => candidate.IsSupportedArchive))
            ?? SelectNewest(usable.Where(candidate => !candidate.IsArchived))
            ?? SelectNewest(usable);
    }

    public static IReadOnlyList<OnlineDownloadCandidate> OrderForManualSelection(
        IEnumerable<OnlineDownloadCandidate>? candidates)
    {
        return GetUsable(candidates)
            .OrderBy(candidate => candidate.IsArchived)
            .ThenByDescending(candidate => candidate.IsSupportedArchive)
            .ThenByDescending(candidate => candidate.AddedAt)
            .ThenByDescending(candidate => ParseNumericFileId(candidate.FileId))
            .ThenBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? SelectCharacterFolder(
        IEnumerable<string>? childFolders,
        IEnumerable<string?>? characterAliases)
    {
        if (childFolders is null || characterAliases is null)
        {
            return null;
        }

        string[] aliases = characterAliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => NormalizeFolderLookup(alias!))
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (aliases.Length == 0)
        {
            return null;
        }

        List<CharacterFolderMatch> matches = childFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => new CharacterFolderMatch(
                folder,
                ScoreCharacterFolder(Path.GetFileName(folder), aliases)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        // A tied best score is not safe enough for automatic routing. Let the user choose instead.
        if (matches.Count > 1 && matches[0].Score == matches[1].Score)
        {
            return null;
        }

        return matches[0].Path;
    }

    private static OnlineDownloadCandidate? SelectNewest(IEnumerable<OnlineDownloadCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.AddedAt)
            .ThenByDescending(candidate => ParseNumericFileId(candidate.FileId))
            .FirstOrDefault();
    }

    private static List<OnlineDownloadCandidate> GetUsable(IEnumerable<OnlineDownloadCandidate>? candidates)
    {
        return candidates?
            .Where(candidate => candidate is not null && !string.IsNullOrWhiteSpace(candidate.DownloadUrl))
            .ToList()
            ?? [];
    }

    private static long ParseNumericFileId(string fileId)
    {
        return long.TryParse(fileId, out long value) ? value : 0;
    }

    private static int ScoreCharacterFolder(string? folderName, IReadOnlyCollection<string> aliases)
    {
        string folder = NormalizeFolderLookup(folderName ?? string.Empty);
        if (folder.Length == 0)
        {
            return 0;
        }

        int bestScore = 0;
        foreach (string alias in aliases)
        {
            if (string.Equals(folder, alias, StringComparison.OrdinalIgnoreCase))
            {
                bestScore = Math.Max(bestScore, 10_000 + alias.Length);
                continue;
            }

            // Short aliases such as initials are useful for an exact folder name, but unsafe for
            // partial matching because they can occur in many unrelated folder names.
            if (alias.Length < 3)
            {
                continue;
            }

            if (folder.StartsWith(alias, StringComparison.OrdinalIgnoreCase)
                || folder.EndsWith(alias, StringComparison.OrdinalIgnoreCase))
            {
                bestScore = Math.Max(bestScore, 7_000 + alias.Length);
            }
            else if (folder.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                bestScore = Math.Max(bestScore, 6_000 + alias.Length);
            }
            else if (folder.Length >= 3 && alias.Contains(folder, StringComparison.OrdinalIgnoreCase))
            {
                bestScore = Math.Max(bestScore, 5_000 + folder.Length);
            }
        }

        return bestScore;
    }

    private static string NormalizeFolderLookup(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private sealed record CharacterFolderMatch(string Path, int Score);
}
