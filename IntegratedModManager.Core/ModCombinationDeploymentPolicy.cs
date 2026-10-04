namespace IntegratedModManager.Core;

public sealed record ModCombinationDeploymentPlan(
    IReadOnlyList<string> DesiredSources, IReadOnlyList<string> InstallSources, IReadOnlyList<string> RemoveTargets);

// Repository convention: character/Mod, deployed as Mods/Mod. Unknown target
// folders are not removed; ambiguous names and foreign junctions fail closed.
public static class ModCombinationDeploymentPolicy
{
    public static IReadOnlyList<string> Capture(string sourceRoot, string targetRoot)
    {
        ValidateSeparateRoots(sourceRoot, targetRoot);
        var known = EnumerateMods(sourceRoot);
        ValidateTargetRoot(targetRoot);
        var enabled = new List<string>();
        foreach (var group in known.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string target = Path.Combine(targetRoot, group.Key!);
            if (!Directory.Exists(target)) continue;
            string[] matches = DirectoryLinkDeployment.IsDirectoryLink(target)
                ? group.Where(source => LinkMatches(source, target)).ToArray() : group.ToArray();
            if (matches.Length > 1) throw new InvalidDataException("More than one repository Mod has the deployed folder name: " + group.Key);
            if (matches.Length == 1) enabled.Add(Path.GetRelativePath(sourceRoot, matches[0]));
        }
        return enabled.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static ModCombinationDeploymentPlan Plan(
        string sourceRoot, string targetRoot, IReadOnlyList<string> relativePaths, bool useLinks)
    {
        ValidateSeparateRoots(sourceRoot, targetRoot);
        var known = EnumerateMods(sourceRoot);
        ValidateTargetRoot(targetRoot);
        var knownSet = known.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desired = new List<string>();
        foreach (string relative in relativePaths)
        {
            string[] parts = relative.Replace('/', '\\').Split('\\');
            if (Path.IsPathRooted(relative) || parts.Length != 2
                || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.Contains(':')))
                throw new InvalidDataException("A combination contains an unsafe or non-character/Mod path.");
            string path = Path.GetFullPath(Path.Combine(sourceRoot, relative));
            if (!knownSet.Contains(path)) throw new DirectoryNotFoundException("A saved Mod is missing or disabled: " + relative);
            if (desired.Contains(path, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("The combination contains a duplicate Mod.");
            desired.Add(path);
        }
        if (desired.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Two saved Mods use the same target folder name. Rename or separate them first.");

        string[] enabled = Capture(sourceRoot, targetRoot).Select(relative => Path.GetFullPath(Path.Combine(sourceRoot, relative))).ToArray();
        var install = new List<string>();
        foreach (string source in desired)
        {
            string target = Path.Combine(targetRoot, Path.GetFileName(source));
            if (File.Exists(target)) throw new IOException("A file occupies a required Mod folder: " + target);
            if (Directory.Exists(target) && DirectoryLinkDeployment.IsDirectoryLink(target)
                && !known.Any(candidate => LinkMatches(candidate, target)))
                throw new InvalidDataException("A required target is a junction outside this repository: " + target);
            bool matches = Directory.Exists(target) && (useLinks
                ? DirectoryLinkDeployment.IsDirectoryLink(target) && LinkMatches(source, target)
                : !DirectoryLinkDeployment.IsDirectoryLink(target));
            if (!matches) install.Add(source);
        }
        var desiredSet = desired.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] remove = enabled.Where(source => !desiredSet.Contains(source) || install.Any(item =>
                Path.GetFileName(item).Equals(Path.GetFileName(source), StringComparison.OrdinalIgnoreCase)))
            .Select(source => Path.Combine(targetRoot, Path.GetFileName(source)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ModCombinationDeploymentPlan(desired, install, remove);
    }

    private static List<string> EnumerateMods(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("The repository folder does not exist.");
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return Directory.EnumerateDirectories(sourceRoot, "*", options)
            .Where(path => !Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
            .SelectMany(first => Directory.EnumerateDirectories(first, "*", options))
            .Where(path => !Path.GetFileName(path).StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath).ToList();
    }

    private static void ValidateTargetRoot(string targetRoot)
    {
        if (!Directory.Exists(targetRoot)) throw new DirectoryNotFoundException("The target Mods folder does not exist.");
        if (DirectoryLinkDeployment.IsDirectoryLink(targetRoot)) throw new InvalidDataException("The target Mods root cannot itself be a directory link.");
    }

    private static void ValidateSeparateRoots(string sourceRoot, string targetRoot)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot));
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Repository and loader Mods folders must be separate, non-nested directories.");
    }

    private static bool LinkMatches(string source, string target)
    {
        string? actual = DirectoryLinkDeployment.TryGetTarget(target);
        return actual is not null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual))
            .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)), StringComparison.OrdinalIgnoreCase);
    }
}
