using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Deployment;

/// <summary>How a repository (or Mod) path and a loader (or deployed) path overlap.</summary>
public enum PathOverlap
{
    /// <summary>Separate: deploying cannot touch the source.</summary>
    None,

    /// <summary>The same folder, possibly reached through a junction or symbolic link.</summary>
    Same,

    /// <summary>The target lies inside the source, such as a loader folder set to a character folder.</summary>
    TargetInsideSource,

    /// <summary>The source lies inside the target.</summary>
    SourceInsideTarget
}

/// <summary>
/// The one check that the repository and the loader Mods folder are separate (issue #105). Combination presets and
/// every single-Mod entry use it before any backup, copy or delete. Paths are compared as written and after
/// following every junction and symbolic link on the way, so a link cannot hide that two folders are one.
/// </summary>
public static class DeploymentPathSeparation
{
    public static PathOverlap Compare(string sourcePath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        PathOverlap written = CompareResolved(Normalize(sourcePath), Normalize(targetPath));
        return written != PathOverlap.None ? written : CompareResolved(ResolveLinks(sourcePath), ResolveLinks(targetPath));
    }

    /// <summary>Throws when the folders overlap; the message names both paths.</summary>
    public static void EnsureSeparate(string sourcePath, string targetPath)
    {
        if (Compare(sourcePath, targetPath) != PathOverlap.None)
            throw new InvalidDataException($"Repository and loader Mods folders must be separate, non-nested directories: {sourcePath} · {targetPath}");
    }

    /// <summary>
    /// The path with every junction or symbolic link on the way replaced by its final target. A part that cannot be
    /// read stays as written.
    /// </summary>
    public static string ResolveLinks(string path)
    {
        string full = Normalize(path);
        string root = Path.GetPathRoot(full) ?? string.Empty;
        string current = root;
        foreach (string part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                var info = new DirectoryInfo(current);
                if (info.Exists && info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } final)
                    current = Normalize(final.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable: compare it as written.
            }
        }
        return Normalize(current);
    }

    private static PathOverlap CompareResolved(string source, string target)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return PathOverlap.Same;
        if (PathSafety.IsSameOrInside(target, source)) return PathOverlap.TargetInsideSource;
        if (PathSafety.IsSameOrInside(source, target)) return PathOverlap.SourceInsideTarget;
        return PathOverlap.None;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
