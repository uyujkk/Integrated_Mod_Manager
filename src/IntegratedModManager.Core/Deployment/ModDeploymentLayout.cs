using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Deployment;

// Repository convention: <repository>/<character folder>/<Mod folder>. A Mod deploys to
// <loader Mods>/<Mod folder name>, so the deployed folder name (and the 3DMigoto runtime
// keys derived from it) do not depend on the character folder.
public static class ModDeploymentLayout
{
    public static string DeployedName(string modSourcePath)
    {
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(modSourcePath));
    }

    public static string DeployedPath(string loaderRoot, string modSourcePath)
    {
        return Path.Combine(loaderRoot, DeployedName(modSourcePath));
    }

    public static bool SameDeployedName(string left, string right)
    {
        return string.Equals(DeployedName(left), DeployedName(right), StringComparison.OrdinalIgnoreCase);
    }

    // The first-level folder that contains the Mod, or null when the path is not
    // <repository>/<character>/<Mod>.
    public static string? CharacterFolder(string repositoryRoot, string modSourcePath)
    {
        string mod = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modSourcePath));
        string? character = Path.GetDirectoryName(mod);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        return character is not null
            && PathSafety.IsStrictlyInside(character, root)
            && string.Equals(Path.GetDirectoryName(character), root, StringComparison.OrdinalIgnoreCase)
                ? character
                : null;
    }
}

// Whether a character folder may have several Mods deployed at once. Single selection is
// the default: enabling a Mod (as a directory link) replaces the links of its siblings.
public static class CharacterSelectionPolicy
{
    public static bool AllowsMultiple(string characterFolderName, bool multipleByDefault, IReadOnlyDictionary<string, bool>? overrides)
    {
        if (overrides is not null)
        {
            foreach ((string name, bool allowsMultiple) in overrides)
            {
                if (string.Equals(name, characterFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    return allowsMultiple;
                }
            }
        }

        return multipleByDefault;
    }

    // The character folders that a set of Mods deploys more than one Mod from, among those
    // that only allow a single Mod. Used to warn before saving or applying such a set.
    public static IReadOnlyList<string> SingleSelectionViolations(
        string repositoryRoot,
        IEnumerable<string> modSourcePaths,
        bool multipleByDefault,
        IReadOnlyDictionary<string, bool>? overrides)
    {
        return modSourcePaths
            .Select(path => ModDeploymentLayout.CharacterFolder(repositoryRoot, path))
            .OfType<string>()
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && !AllowsMultiple(group.Key!, multipleByDefault, overrides))
            .Select(group => group.Key!)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
