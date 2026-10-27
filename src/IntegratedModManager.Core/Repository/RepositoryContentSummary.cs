namespace IntegratedModManager.Core.Repository;

/// <summary>
/// What the dashboard shows for one repository: whether its folders exist and how many
/// character folders (first level) and Mods (second level) the source folder holds.
/// </summary>
public readonly record struct RepositoryContentSummary(bool SourceReady, bool TargetReady, int CategoryCount, int ModCount)
{
    public bool IsReady => SourceReady && TargetReady;

    /// <summary>Each character folder with its Mod count, in name order; empty when the source is missing.</summary>
    public IReadOnlyList<RepositoryCategorySummary> Categories { get; init; } = [];

    /// <summary>Counts folders only; unreadable folders count as empty so one bad folder cannot fail the scan.</summary>
    public static RepositoryContentSummary Scan(string? sourcePath, string? targetPath)
    {
        bool sourceReady = DirectoryExists(sourcePath);
        bool targetReady = DirectoryExists(targetPath);
        if (!sourceReady)
        {
            return new RepositoryContentSummary(false, targetReady, 0, 0);
        }

        string[] categories;
        try
        {
            categories = Directory.GetDirectories(sourcePath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RepositoryContentSummary(true, targetReady, 0, 0);
        }

        int mods = 0;
        var summaries = new List<RepositoryCategorySummary>(categories.Length);
        foreach (string category in categories.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            int count = 0;
            try
            {
                count = Directory.GetDirectories(category).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable folders count as empty.
            }
            mods += count;
            summaries.Add(new RepositoryCategorySummary(Path.GetFileName(category), category, count));
        }

        return new RepositoryContentSummary(true, targetReady, categories.Length, mods) { Categories = summaries };
    }

    private static bool DirectoryExists(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>One character folder of a repository and how many Mods it holds.</summary>
public sealed record RepositoryCategorySummary(string Name, string Path, int ModCount);

public enum PathFieldStatus { Empty, Ready, Missing }

public static class PathFieldStatuses
{
    /// <summary>Whether a path box points at something that exists: a folder, or a file for the launcher.</summary>
    public static PathFieldStatus Evaluate(string? path, bool expectFile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathFieldStatus.Empty;
        }

        try
        {
            string trimmed = path.Trim();
            bool exists = expectFile ? File.Exists(trimmed) : Directory.Exists(trimmed);
            return exists ? PathFieldStatus.Ready : PathFieldStatus.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return PathFieldStatus.Missing;
        }
    }
}
