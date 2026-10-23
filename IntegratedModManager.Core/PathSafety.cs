namespace IntegratedModManager.Core;

public static class PathSafety
{
    public static string ResolveInsideDirectory(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(relativePath);

        // TrimEndingDirectorySeparator keeps a drive root as "D:\"; trimming it to "D:"
        // would make Path.Combine resolve against the drive's current directory.
        string fullRoot = Normalize(rootPath);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!IsSameOrInsideNormalized(fullPath, fullRoot))
        {
            throw new InvalidDataException("A path points outside the expected directory: " + relativePath);
        }

        return fullPath;
    }

    /// <summary>True when <paramref name="candidatePath"/> is the root itself or a descendant of it.</summary>
    public static bool IsSameOrInside(string candidatePath, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return IsSameOrInsideNormalized(Normalize(candidatePath), Normalize(rootPath));
    }

    /// <summary>True only for a descendant; the root itself is not inside.</summary>
    public static bool IsStrictlyInside(string candidatePath, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string candidate = Normalize(candidatePath);
        string root = Normalize(rootPath);
        return !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            && IsSameOrInsideNormalized(candidate, root);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInsideNormalized(string candidate, string root)
    {
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
