namespace IntegratedModManager.Core;

public static class PathSafety
{
    public static string ResolveInsideDirectory(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(relativePath);

        string fullRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string rootPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A path points outside the expected directory: " + relativePath);
        }

        return fullPath;
    }
}
