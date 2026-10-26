using System.Text;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Repository;

public enum CharacterAvatarSource { None, CustomFile, BoundCharacter, MatchedCharacter }

public sealed record CharacterAvatar(CharacterAvatarSource Source, string? FilePath, OnlineCharacterOption? Character)
{
    public static CharacterAvatar None { get; } = new(CharacterAvatarSource.None, null, null);

    public string? ImageUrl => Character?.AvatarUrl is { Length: > 0 } url ? url : null;
}

// Avatars for first-level (character) folders of a repository. In order:
//   1. an image the user put in the folder: .imm_avatar.* (set from the app) or avatar.*;
//   2. a character the user bound the folder to (folder name -> catalog key);
//   3. the catalog character whose key, Chinese or English name, or alias equals the folder name.
// Matching is exact after normalization (width, case, spaces and punctuation): no fuzzy
// matching, so a short folder name never picks up a longer, different character.
public static class CharacterFolderAvatars
{
    public const string CustomFileName = ".imm_avatar";
    public const string PreviousCustomFileName = ".imm_avatar.previous";
    public static readonly IReadOnlyList<string> ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    public static CharacterAvatar Resolve(
        string folderPath,
        IReadOnlyList<OnlineCharacterOption> catalog,
        IReadOnlyDictionary<string, string> bindings)
    {
        string? file = FindCustomFile(folderPath);
        if (file is not null)
        {
            return new CharacterAvatar(CharacterAvatarSource.CustomFile, file, null);
        }

        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        if (bindings.TryGetValue(folderName, out string? key) && !string.IsNullOrWhiteSpace(key))
        {
            OnlineCharacterOption? bound = catalog.FirstOrDefault(character => string.Equals(character.Key, key, StringComparison.OrdinalIgnoreCase));
            if (bound is not null)
            {
                return new CharacterAvatar(CharacterAvatarSource.BoundCharacter, null, bound);
            }
        }

        OnlineCharacterOption? matched = Match(folderName, catalog);
        return matched is null ? CharacterAvatar.None : new CharacterAvatar(CharacterAvatarSource.MatchedCharacter, null, matched);
    }

    // Key first, then display names, then aliases. Two different characters on the same level is ambiguous: no match.
    public static OnlineCharacterOption? Match(string folderName, IReadOnlyList<OnlineCharacterOption> catalog)
    {
        string name = Normalize(folderName);
        if (name.Length == 0)
        {
            return null;
        }

        Func<OnlineCharacterOption, IEnumerable<string>>[] levels =
        [
            character => [character.Key],
            character => [character.DisplayNameZh, character.DisplayNameEn],
            character => character.Aliases
        ];
        foreach (var namesOf in levels)
        {
            OnlineCharacterOption[] matches = catalog
                .Where(character => namesOf(character).Any(candidate => !string.IsNullOrWhiteSpace(candidate) && Normalize(candidate) == name))
                .DistinctBy(character => character.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }
            if (matches.Length > 1)
            {
                return null;
            }
        }

        return null;
    }

    public static string Normalize(string value)
    {
        return OnlineCharacterCatalogMatcher.NormalizeLookup((value ?? string.Empty).Normalize(NormalizationForm.FormKC));
    }

    public static string? FindCustomFile(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return null;
        }

        foreach (string baseName in new[] { CustomFileName, "avatar" })
        {
            foreach (string extension in ImageExtensions)
            {
                string path = Path.Combine(folderPath, baseName + extension);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                {
                    return path;
                }
            }
        }

        return null;
    }

    // Copies the image into the folder as .imm_avatar.<ext>. An avatar set earlier is kept as
    // .imm_avatar.previous.<ext> (replacing an older backup), so the last change can be undone.
    public static string SetCustomAvatar(string folderPath, string imagePath)
    {
        string extension = Path.GetExtension(imagePath).ToLowerInvariant();
        if (!ImageExtensions.Contains(extension))
        {
            throw new InvalidDataException("Choose a PNG, JPG, WEBP, BMP or GIF image.");
        }
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException("The character folder does not exist: " + folderPath);
        }

        string target = Path.Combine(folderPath, CustomFileName + extension);
        string temporary = Path.Combine(folderPath, CustomFileName + ".tmp-" + Guid.NewGuid().ToString("N"));
        using (FileStream source = ImageFileReadPolicy.OpenRead(imagePath))
        using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            source.CopyTo(destination);
        }

        try
        {
            BackUpCurrent(folderPath);
            File.Move(temporary, target);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        return target;
    }

    // Removes the avatar set from the app, keeping it as the previous backup.
    public static bool RemoveCustomAvatar(string folderPath)
    {
        return Directory.Exists(folderPath) && BackUpCurrent(folderPath);
    }

    // Puts the previous avatar back; the current one becomes the new backup.
    public static bool RestorePreviousAvatar(string folderPath)
    {
        string? previous = ImageExtensions.Select(extension => Path.Combine(folderPath, PreviousCustomFileName + extension)).FirstOrDefault(File.Exists);
        if (previous is null)
        {
            return false;
        }

        string extension = Path.GetExtension(previous);
        string swap = Path.Combine(folderPath, CustomFileName + ".restore-" + Guid.NewGuid().ToString("N") + extension);
        File.Move(previous, swap);
        BackUpCurrent(folderPath);
        File.Move(swap, Path.Combine(folderPath, CustomFileName + extension));
        return true;
    }

    private static bool BackUpCurrent(string folderPath)
    {
        string[] current = ImageExtensions.Select(extension => Path.Combine(folderPath, CustomFileName + extension)).Where(File.Exists).ToArray();
        if (current.Length == 0)
        {
            return false;
        }

        foreach (string old in ImageExtensions.Select(extension => Path.Combine(folderPath, PreviousCustomFileName + extension)).Where(File.Exists))
        {
            File.Delete(old);
        }
        File.Move(current[0], Path.Combine(folderPath, PreviousCustomFileName + Path.GetExtension(current[0])));
        foreach (string extra in current.Skip(1))
        {
            File.Delete(extra);
        }
        return true;
    }
}
