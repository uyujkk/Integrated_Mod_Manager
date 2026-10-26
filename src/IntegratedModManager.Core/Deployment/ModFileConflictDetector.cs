using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Deployment;

/// <summary>
/// Finds installed Mods in the loader folder that ship a file at the same relative path as
/// the Mod being installed. Previews, readmes and licenses at the Mod root are ignored;
/// anything in a subfolder always counts. Broken links are skipped (cleanup is offered
/// separately) and at most 100 conflicts are reported.
/// </summary>
public static class ModFileConflictDetector
{
    private const int MaxConflicts = 100;

    /// <returns>Entries formatted as "InstalledModFolder  →  relative\path".</returns>
    public static List<string> Detect(string sourcePath, string targetRoot, IEnumerable<string> targetPathsToIgnore)
    {
        ArgumentNullException.ThrowIfNull(targetPathsToIgnore);
        HashSet<string> ignoredTargets = targetPathsToIgnore
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> sourceFiles = SafeDirectoryTree.Inspect(sourcePath).Files
            .Select(path => Path.GetRelativePath(sourcePath, path))
            .Where(IsConflictRelevantRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<string>();
        foreach (string installedModPath in Directory.GetDirectories(targetRoot))
        {
            if (ignoredTargets.Contains(Path.GetFullPath(installedModPath)))
            {
                continue;
            }

            string inspectionRoot = installedModPath;
            if (DirectoryLinkDeployment.IsBrokenLink(installedModPath))
            {
                // Its source was deleted or renamed: nothing to compare, and it must not
                // stop every other installation. Cleanup is offered before deployment.
                continue;
            }

            if (DirectoryLinkDeployment.IsDirectoryLink(installedModPath))
            {
                inspectionRoot = DirectoryLinkDeployment.TryGetTarget(installedModPath)
                    ?? throw ErrorTags.Tag(
                        new IOException("Unable to resolve an installed directory junction."),
                        InstallTransactionErrorKind.UnresolvedInstalledLink,
                        installedModPath);
            }

            foreach (string file in SafeDirectoryTree.Inspect(inspectionRoot).Files)
            {
                string relativePath = Path.GetRelativePath(inspectionRoot, file);
                if (IsConflictRelevantRelativePath(relativePath) && sourceFiles.Contains(relativePath))
                {
                    conflicts.Add($"{Path.GetFileName(installedModPath)}  →  {relativePath}");
                    if (conflicts.Count >= MaxConflicts)
                    {
                        return conflicts;
                    }
                }
            }
        }

        return conflicts;
    }

    public static bool IsConflictRelevantRelativePath(string relativePath)
    {
        if (relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
        {
            return true;
        }

        string fileName = Path.GetFileName(relativePath);
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        if (baseName.Equals("preview", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("cover", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("thumbnail", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !fileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("license", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    }
}
