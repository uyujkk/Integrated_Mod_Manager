using IntegratedModManager.Core.Deployment;

namespace IntegratedModManager.Core.Repository;

/// <summary>Why a character folder cannot be renamed now (issue #108).</summary>
public enum CharacterRenameBlock
{
    None,
    InvalidName,
    NameTaken,
    SourceMissing,
    // The loader folder is set but cannot be read, so links into the character cannot be found.
    LoaderUnavailable,
    // Some links in the loader folder cannot be read; they might point into the character.
    LinksUnreadable
}

/// <summary>A deployed link into the character folder, and where it points after the rename.</summary>
public sealed record CharacterRenameLink(string LinkPath, string OldTarget, string NewTarget);

/// <summary>
/// Everything a character rename moves: the folder, the links in the loader folder that point into it, and the
/// mapping every saved path or name goes through. Prepared before anything changes.
/// </summary>
public sealed class CharacterRenamePlan
{
    internal CharacterRenamePlan(string oldPath, string newPath, CharacterRenameBlock block,
        IReadOnlyList<CharacterRenameLink> links, IReadOnlyList<string> unreadable)
    {
        OldPath = oldPath;
        NewPath = newPath;
        Block = block;
        Links = links;
        UnreadableLinks = unreadable;
    }

    public string OldPath { get; }
    public string NewPath { get; }
    public string OldName => Path.GetFileName(OldPath);
    public string NewName => Path.GetFileName(NewPath);
    public CharacterRenameBlock Block { get; }
    public bool CanRename => Block == CharacterRenameBlock.None;
    public IReadOnlyList<CharacterRenameLink> Links { get; }

    /// <summary>Links in the loader folder whose target could not be read.</summary>
    public IReadOnlyList<string> UnreadableLinks { get; }

    /// <summary>The path after the rename, or <see langword="null"/> when it is not in the character folder.</summary>
    public string? MapFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        if (string.Equals(full, OldPath, StringComparison.OrdinalIgnoreCase)) return NewPath;
        string prefix = OldPath + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? NewPath + full[OldPath.Length..] : null;
    }

    /// <summary>
    /// A repository-relative path ("Character\Mod" or "Character/Mod") after the rename, keeping its separator, or
    /// <see langword="null"/> when it is not in the character folder.
    /// </summary>
    public string? MapRelative(string relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        int separator = relative.IndexOfAny(['\\', '/']);
        string first = separator < 0 ? relative : relative[..separator];
        if (!string.Equals(first, OldName, StringComparison.OrdinalIgnoreCase)) return null;
        return separator < 0 ? NewName : NewName + relative[separator..];
    }

    /// <summary>A character name after the rename, or <see langword="null"/> when it is another character.</summary>
    public string? MapName(string name) => string.Equals(name, OldName, StringComparison.OrdinalIgnoreCase) ? NewName : null;
}

/// <summary>The saved data that holds paths or names of the character, migrated by the caller.</summary>
public interface ICharacterRenameReferences
{
    /// <summary>Rewrites the references in memory.</summary>
    void Migrate(CharacterRenamePlan plan);

    /// <summary>Puts the references back as they were before <see cref="Migrate"/>.</summary>
    void Revert();

    /// <summary>Writes the references to disk; throws when it cannot.</summary>
    void Save();
}

/// <summary>A rename that did not complete. When <see cref="RolledBack"/> is false, <see cref="Unrestored"/> says what is left.</summary>
public sealed class CharacterRenameException : Exception
{
    public CharacterRenameException(Exception cause, IReadOnlyList<string> unrestored)
        : base(cause.Message, cause)
    {
        Unrestored = unrestored;
    }

    public IReadOnlyList<string> Unrestored { get; }
    public bool RolledBack => Unrestored.Count == 0;
}

/// <summary>
/// Renames a character folder as one recoverable operation (issue #108): the links into it are rebuilt to the new
/// path and every saved reference is migrated, and when any step fails the steps done so far are undone in reverse.
/// </summary>
public static class CharacterRename
{
    public static CharacterRenamePlan Prepare(string characterPath, string newName, string? loaderRoot)
        => Prepare(characterPath, newName, loaderRoot, DirectoryLinkDeployment.TryGetStoredTarget);

    // readTarget: the target stored in a link, or null when it cannot be read.
    internal static CharacterRenamePlan Prepare(string characterPath, string newName, string? loaderRoot, Func<string, string?> readTarget)
    {
        string oldPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(characterPath));
        string parent = Path.GetDirectoryName(oldPath) ?? oldPath;
        string name = (newName ?? string.Empty).Trim();
        string newPath = IsValidName(name) ? Path.Combine(parent, name) : oldPath;
        CharacterRenamePlan Blocked(CharacterRenameBlock block, IReadOnlyList<string>? unreadable = null)
            => new(oldPath, newPath, block, [], unreadable ?? []);

        if (!IsValidName(name)) return Blocked(CharacterRenameBlock.InvalidName);
        if (!Directory.Exists(oldPath)) return Blocked(CharacterRenameBlock.SourceMissing);
        if (Directory.Exists(newPath) || File.Exists(newPath)) return Blocked(CharacterRenameBlock.NameTaken);
        if (string.IsNullOrWhiteSpace(loaderRoot)) return new(oldPath, newPath, CharacterRenameBlock.None, [], []);

        var plan = new CharacterRenamePlan(oldPath, newPath, CharacterRenameBlock.None, [], []);
        var links = new List<CharacterRenameLink>();
        var unreadableLinks = new List<string>();
        try
        {
            if (!Directory.Exists(loaderRoot)) return Blocked(CharacterRenameBlock.LoaderUnavailable);
            // The attributes come with the listing, so a link is recognised even when it cannot be opened.
            foreach (DirectoryInfo entry in new DirectoryInfo(loaderRoot).EnumerateDirectories())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) continue;
                string? target = readTarget(entry.FullName);
                if (target is null) { unreadableLinks.Add(entry.FullName); continue; }
                if (plan.MapFullPath(target) is string mapped) links.Add(new(entry.FullName, target, mapped));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Blocked(CharacterRenameBlock.LoaderUnavailable);
        }

        if (unreadableLinks.Count > 0) return Blocked(CharacterRenameBlock.LinksUnreadable, unreadableLinks);
        links.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.LinkPath, b.LinkPath));
        return new(oldPath, newPath, CharacterRenameBlock.None, links, []);
    }

    /// <summary>
    /// Moves the folder, rebuilds the links and migrates and saves the references. On failure everything done is
    /// undone and the references are saved again as they were; a <see cref="CharacterRenameException"/> reports it.
    /// </summary>
    public static void Execute(CharacterRenamePlan plan, ICharacterRenameReferences references)
        => Execute(plan, references, DirectoryLinkDeployment.CreateJunction);

    internal static void Execute(CharacterRenamePlan plan, ICharacterRenameReferences references, Action<string, string> createJunction)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(references);
        if (!plan.CanRename) throw new InvalidOperationException("The rename is blocked: " + plan.Block);

        var undo = new Stack<(string Step, Action Action)>();
        bool saveTried = false;
        try
        {
            foreach (CharacterRenameLink link in plan.Links)
            {
                DirectoryLinkDeployment.RemoveLink(link.LinkPath);
                undo.Push(("link " + link.LinkPath + " -> " + link.OldTarget, () => createJunction(link.LinkPath, link.OldTarget)));
            }

            Directory.Move(plan.OldPath, plan.NewPath);
            undo.Push(("folder " + plan.NewPath + " -> " + plan.OldPath, () => Directory.Move(plan.NewPath, plan.OldPath)));

            foreach (CharacterRenameLink link in plan.Links)
            {
                createJunction(link.LinkPath, link.NewTarget);
                undo.Push(("remove link " + link.LinkPath, () => DirectoryLinkDeployment.RemoveLink(link.LinkPath)));
            }

            references.Migrate(plan);
            undo.Push(("saved references", references.Revert));
            saveTried = true;
            references.Save();
        }
        catch (Exception ex)
        {
            var unrestored = new List<string>();
            while (undo.Count > 0)
            {
                (string step, Action action) = undo.Pop();
                try { action(); }
                catch (Exception undoError) { unrestored.Add(step + ": " + undoError.Message); }
            }

            if (saveTried)
            {
                try { references.Save(); }
                catch (Exception saveError) { unrestored.Add("saved references: " + saveError.Message); }
            }

            throw new CharacterRenameException(ex, unrestored);
        }
    }

    private static bool IsValidName(string name)
        => name.Length > 0 && name is not ("." or "..") && !name.EndsWith('.')
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}

/// <summary>Records each in-memory change so a failed rename can put it back (used by <see cref="ICharacterRenameReferences"/>).</summary>
public sealed class ReferenceUndoLog
{
    private readonly List<Action> _undo = [];

    public int Count => _undo.Count;

    /// <summary>Changes a value when the mapping returns a new one.</summary>
    public void Rewrite(Func<string> get, Action<string> set, Func<string, string?> map)
    {
        string old = get();
        if (map(old) is not string mapped || string.Equals(mapped, old, StringComparison.Ordinal)) return;
        set(mapped);
        _undo.Add(() => set(old));
    }

    /// <summary>Moves dictionary entries to their mapped keys.</summary>
    public void Rekey<TValue>(IDictionary<string, TValue> dictionary, Func<string, string?> map)
    {
        foreach (string key in dictionary.Keys.ToArray())
        {
            if (map(key) is not string mapped || string.Equals(mapped, key, StringComparison.Ordinal)) continue;
            TValue value = dictionary[key];
            bool replaced = dictionary.TryGetValue(mapped, out TValue? previous);
            dictionary.Remove(key);
            dictionary[mapped] = value;
            _undo.Add(() =>
            {
                dictionary.Remove(mapped);
                if (replaced) dictionary[mapped] = previous!;
                dictionary[key] = value;
            });
        }
    }

    /// <summary>Rewrites the entries of a list or set of paths.</summary>
    public void RewriteAll(ICollection<string> items, Func<string, string?> map)
    {
        string[] before = [.. items];
        if (!before.Any(item => map(item) is string mapped && !string.Equals(mapped, item, StringComparison.Ordinal))) return;
        items.Clear();
        foreach (string item in before) items.Add(map(item) ?? item);
        _undo.Add(() =>
        {
            items.Clear();
            foreach (string item in before) items.Add(item);
        });
    }

    /// <summary>Any other change, with how to undo it.</summary>
    public void Add(Action undo) => _undo.Add(undo);

    /// <summary>Undoes everything, newest first, and starts over.</summary>
    public void Undo()
    {
        for (int i = _undo.Count - 1; i >= 0; i--) _undo[i]();
        _undo.Clear();
    }
}
