using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Deployment;

public enum CharacterFolderAction { UseExisting, Create, AskUser }

public enum CharacterFolderReason
{
    Matched,
    NoMatchCreate,
    Ambiguous,
    UnknownCharacter,
    MultipleCharacters,
    MixedNamingLanguage,
    NoNameInLanguage,
    InvalidName,
    NameConflict
}

public sealed record CharacterFolderPlan(CharacterFolderAction Action, CharacterFolderReason Reason, string? Path);

/// <summary>
/// Issue #6: reuse a character folder when one matches, otherwise propose creating one in the
/// repository's naming language. Anything uncertain is left to the user. Planning never touches disk;
/// <see cref="CreatePlannedFolder"/> creates only after the user confirmed.
/// </summary>
public static class CharacterFolderPlanner
{
    private static readonly string[] MultipleCharacterSeparators = ["&", "/", ",", "，", "、", "+", " and ", " x ", " X "];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static CharacterFolderPlan Plan(
        string repositoryRoot,
        IReadOnlyCollection<string> childFolders,
        IReadOnlyCollection<string> childFileNames,
        IReadOnlyCollection<string> aliases,
        string? characterName,
        string? chineseName,
        string? englishName,
        bool interfaceIsChinese)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (aliases.Count == 0 || string.IsNullOrWhiteSpace(characterName))
            return Ask(CharacterFolderReason.UnknownCharacter);
        if (MultipleCharacterSeparators.Any(separator => characterName.Contains(separator, StringComparison.Ordinal)))
            return Ask(CharacterFolderReason.MultipleCharacters);

        var (match, ambiguous) = OnlineDownloadSelectionPolicy.MatchCharacterFolder(childFolders, aliases);
        if (match is not null) return new(CharacterFolderAction.UseExisting, CharacterFolderReason.Matched, match);
        if (ambiguous) return Ask(CharacterFolderReason.Ambiguous);

        bool? chinese = NamingLanguage(childFolders);
        if (chinese is null && childFolders.Any(IsCharacterLikeFolder)) return Ask(CharacterFolderReason.MixedNamingLanguage);
        bool useChinese = chinese ?? interfaceIsChinese;
        string? name = (useChinese ? chineseName : englishName)?.Trim();
        if (string.IsNullOrEmpty(name)) return Ask(CharacterFolderReason.NoNameInLanguage);
        if (!IsSafeFolderName(name)) return Ask(CharacterFolderReason.InvalidName);
        if (childFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || childFolders.Any(folder => string.Equals(System.IO.Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase)))
            return Ask(CharacterFolderReason.NameConflict);
        return new(CharacterFolderAction.Create, CharacterFolderReason.NoMatchCreate, System.IO.Path.Combine(repositoryRoot, name));

        static CharacterFolderPlan Ask(CharacterFolderReason reason) => new(CharacterFolderAction.AskUser, reason, null);
    }

    /// <summary>true = Chinese names, false = English names, null = mixed or no character-like folders.</summary>
    internal static bool? NamingLanguage(IEnumerable<string> childFolders)
    {
        bool anyChinese = false, anyEnglish = false;
        foreach (string folder in childFolders.Where(IsCharacterLikeFolder))
        {
            if (System.IO.Path.GetFileName(folder).Any(IsCjk)) anyChinese = true;
            else anyEnglish = true;
        }
        return anyChinese == anyEnglish ? null : anyChinese;
    }

    private static bool IsCharacterLikeFolder(string folder)
    {
        string name = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'));
        return name.Length > 0 && !name.StartsWith('.') && !name.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)
            && name.Any(character => IsCjk(character) || char.IsAsciiLetter(character));
    }

    private static bool IsCjk(char character) => character is >= '一' and <= '鿿' or >= '㐀' and <= '䶿';

    public static bool IsSafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name != name.Trim() || name.EndsWith('.')) return false;
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..") return false;
        string stem = name.Split('.')[0];
        return !ReservedNames.Contains(stem);
    }

    /// <summary>
    /// Creates the confirmed folder after re-checking it is a direct, non-linked child of the repository.
    /// An existing plain folder (e.g. created by a concurrent download) is reused; nothing is ever deleted.
    /// </summary>
    public static string CreatePlannedFolder(string repositoryRoot, string plannedPath)
    {
        string root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(repositoryRoot));
        string target = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(plannedPath));
        string name = System.IO.Path.GetFileName(target);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (!string.Equals(System.IO.Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase) || !IsSafeFolderName(name))
            throw new IOException("The new character folder must be a direct child of the repository.");
        if (File.Exists(target)) throw new IOException("A file with the character folder name already exists.");
        if (Directory.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The character folder is a link; choose a folder manually.");
            return target;
        }
        Directory.CreateDirectory(target);
        return target;
    }
}
