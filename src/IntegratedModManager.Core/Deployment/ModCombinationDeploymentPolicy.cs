namespace IntegratedModManager.Core.Deployment;

/// <param name="UnownedTargets">
/// Copied folders with a repository Mod's name that the manager cannot recognise as its own (issue #107): they are
/// never removed or replaced; one in the way of a wanted Mod blocks the restore until the user takes it over.
/// </param>
public sealed record ModCombinationDeploymentPlan(
    IReadOnlyList<string> DesiredSources, IReadOnlyList<string> InstallSources, IReadOnlyList<string> RemoveTargets,
    IReadOnlyList<string>? UnownedTargets = null)
{
    public IReadOnlyList<string> Unowned => UnownedTargets ?? [];
}

// Repository convention: character/Mod, deployed as Mods/Mod. Unknown target
// folders are not removed; ambiguous names and foreign junctions fail closed. A copied folder counts as a
// deployment only when ownsCopy (the CopyDeploymentLedger) recognises it; without it every same-named copy does.
public static class ModCombinationDeploymentPolicy
{
    public static IReadOnlyList<string> Capture(string sourceRoot, string targetRoot, Func<string, string, bool>? ownsCopy = null)
    {
        ValidateSeparateRoots(sourceRoot, targetRoot);
        var known = EnumerateMods(sourceRoot);
        ValidateTargetRoot(targetRoot);
        var enabled = new List<string>();
        foreach (var group in known.GroupBy(ModDeploymentLayout.DeployedName, StringComparer.OrdinalIgnoreCase))
        {
            string target = Path.Combine(targetRoot, group.Key!);
            if (!Directory.Exists(target)) continue;
            string[] matches = DirectoryLinkDeployment.IsDirectoryLink(target)
                ? group.Where(source => LinkMatches(source, target)).ToArray()
                : group.Where(source => ownsCopy is null || ownsCopy(source, target)).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("More than one repository Mod has the deployed folder name: " + group.Key);
            if (matches.Length == 1) enabled.Add(Path.GetRelativePath(sourceRoot, matches[0]));
        }
        return enabled.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static ModCombinationDeploymentPlan Plan(
        string sourceRoot, string targetRoot, IReadOnlyList<string> relativePaths, bool useLinks, Func<string, string, bool>? ownsCopy = null)
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
        if (desired.GroupBy(ModDeploymentLayout.DeployedName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Two saved Mods use the same target folder name. Rename or separate them first.");

        string[] enabled = Capture(sourceRoot, targetRoot, ownsCopy).Select(relative => Path.GetFullPath(Path.Combine(sourceRoot, relative))).ToArray();
        // Copies named like a repository Mod that no repository Mod owns: kept, never replaced.
        var enabledTargets = enabled.Select(source => ModDeploymentLayout.DeployedPath(targetRoot, source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] unowned = ownsCopy is null ? [] : known.Select(source => ModDeploymentLayout.DeployedPath(targetRoot, source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(target => Directory.Exists(target) && !DirectoryLinkDeployment.IsDirectoryLink(target) && !enabledTargets.Contains(target))
            .OrderBy(target => target, StringComparer.OrdinalIgnoreCase).ToArray();
        var install = new List<string>();
        foreach (string source in desired)
        {
            string target = ModDeploymentLayout.DeployedPath(targetRoot, source);
            if (File.Exists(target)) throw new IOException("A file occupies a required Mod folder: " + target);
            if (Directory.Exists(target) && DirectoryLinkDeployment.IsDirectoryLink(target)
                && !known.Any(candidate => LinkMatches(candidate, target)))
                throw new InvalidDataException("A required target is a junction outside this repository: " + target);
            // An external copy in the way is neither replaced nor counted as this Mod; the caller blocks the restore.
            if (unowned.Contains(target, StringComparer.OrdinalIgnoreCase)) continue;
            bool matches = Directory.Exists(target) && (useLinks
                ? DirectoryLinkDeployment.IsDirectoryLink(target) && LinkMatches(source, target)
                : !DirectoryLinkDeployment.IsDirectoryLink(target));
            if (!matches) install.Add(source);
        }
        var desiredSet = desired.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] remove = enabled.Where(source => !desiredSet.Contains(source) || install.Any(item =>
                ModDeploymentLayout.SameDeployedName(item, source)))
            .Select(source => ModDeploymentLayout.DeployedPath(targetRoot, source))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ModCombinationDeploymentPlan(desired, install, remove, unowned);
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

    // The same check as single-Mod deployment (DeploymentPathSeparation), links included.
    private static void ValidateSeparateRoots(string sourceRoot, string targetRoot) => DeploymentPathSeparation.EnsureSeparate(sourceRoot, targetRoot);

    private static bool LinkMatches(string source, string target)
    {
        string? actual = DirectoryLinkDeployment.TryGetTarget(target);
        return actual is not null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual))
            .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)), StringComparison.OrdinalIgnoreCase);
    }
}
