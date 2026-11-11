namespace IntegratedModManager.Core.Deployment;

/// <summary>
/// Issue #129: an online Mod that is not a character's (a weapon, UI, other…) goes into a repository folder named after
/// its category, which the workspace then shows like a character folder. The plan has the same shape as
/// <see cref="CharacterFolderPlanner"/>, and the folder is created the same way (<see cref="CharacterFolderPlanner.CreatePlannedFolder"/>),
/// so deploy, remove, presets, backups, copy records and the delete checks all work as for a character.
/// </summary>
public static class CategoryFolderPlanner
{
    private const int MaxLength = 64;

    /// <summary>
    /// A folder name for a category name: the part before the first slash ("Other/Misc" → "Other",
    /// "Entities/NPCs" → "Entities"), characters Windows does not allow in a name replaced by "_", spaces collapsed,
    /// dots and spaces trimmed from the end, at most 64 characters, and "_" added to a reserved device name (CON…).
    /// Null when nothing usable is left.
    /// </summary>
    public static string? FolderName(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;
        string first = categoryName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(first.Length);
        bool lastWasSpace = false;
        foreach (char character in first)
        {
            char next = invalid.Contains(character) || char.IsControl(character) ? '_' : char.IsWhiteSpace(character) ? ' ' : character;
            if (next == ' ' && lastWasSpace) continue;
            builder.Append(next);
            lastWasSpace = next == ' ';
        }
        string name = builder.ToString().Trim();
        if (name.Length > MaxLength) name = name[..MaxLength];
        name = name.TrimEnd('.', ' ');
        if (name.Length == 0 || name.All(character => character == '_')) return null;
        if (!CharacterFolderPlanner.IsSafeFolderName(name)) name += "_";
        return CharacterFolderPlanner.IsSafeFolderName(name) ? name : null;
    }

    /// <summary>
    /// Reuse the repository's folder with that name (any letter case), otherwise propose creating it; a file with the
    /// name, or a name that cannot be a folder, leaves the choice to the user. Never touches disk.
    /// </summary>
    public static CharacterFolderPlan Plan(string repositoryRoot, IReadOnlyCollection<string> childFolders,
        IReadOnlyCollection<string> childFileNames, string? categoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string? name = FolderName(categoryName);
        if (name is null) return new(CharacterFolderAction.AskUser, CharacterFolderReason.InvalidName, null);
        string? existing = childFolders.FirstOrDefault(folder => string.Equals(Path.GetFileName(folder.TrimEnd('\\', '/')), name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return new(CharacterFolderAction.UseExisting, CharacterFolderReason.Matched, existing);
        if (childFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return new(CharacterFolderAction.AskUser, CharacterFolderReason.NameConflict, null);
        return new(CharacterFolderAction.Create, CharacterFolderReason.NoMatchCreate, Path.Combine(repositoryRoot, name));
    }
}
