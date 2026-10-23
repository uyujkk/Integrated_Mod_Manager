using System.Text.Json;

namespace IntegratedModManager.Core.Repository;

public sealed class OnlineBrowseSession
{
    public int Page { get; set; } = 1;
    public string Character { get; set; } = "";
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Search { get; set; } = "";
    public string Sort { get; set; } = "Hotness";
    public string Layout { get; set; } = "List";
    public int WorkFilter { get; set; }
    public double Offset { get; set; }
    public string Anchor { get; set; } = "";
    public double AnchorOffset { get; set; }

    public OnlineBrowseSession Sanitize() => new()
    {
        Page = Math.Clamp(Page, 1, 100000), Character = Limit(Character), AuthorId = Math.Max(0, AuthorId),
        AuthorName = Limit(AuthorName), Search = Limit(Search),
        Sort = Sort is "Downloads" or "Likes" or "Views" or "Updated" ? Sort : "Hotness",
        Layout = Layout == "Grid" ? "Grid" : "List", WorkFilter = Math.Clamp(WorkFilter, 0, 2),
        Offset = double.IsFinite(Offset) ? Math.Clamp(Offset, 0, 100000000) : 0,
        Anchor = Limit(Anchor), AnchorOffset = double.IsFinite(AnchorOffset) ? Math.Clamp(AnchorOffset, -100000, 100000) : 0
    };
    private static string Limit(string? value) => (value ?? "")[..Math.Min(value?.Length ?? 0, 1024)];
}

public static class WorkspaceIdentity
{
    // An ID alone is insufficient after a repository is repointed or its category changes.
    public static string Online(string id, string path, string source, string category)
        => JsonSerializer.Serialize(new[] { id, Root(path), source.Trim().ToUpperInvariant(), category.Trim() });

    public static string Library(string id, string path) => JsonSerializer.Serialize(new[] { id, Root(path) });

    public static string Relative(string root, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new ArgumentException("The Mod must be inside its repository.");
        return relative.Replace('\\', '/');
    }

    private static string Root(string path) => string.IsNullOrWhiteSpace(path) ? ""
        : Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant();
}

public sealed class LocalModFilter
{
    public string Name { get; set; } = "";
    public string Character { get; set; } = "";
    public string Search { get; set; } = "";
    public string Tag { get; set; } = "";
}

public enum TagEditMode { Add, Remove, Replace }

public sealed class LocalLibraryState
{
    public Dictionary<string, List<string>> ModTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LocalModFilter> Filters { get; set; } = [];

    public string[] TagsFor(string relativePath)
        => ModTags.TryGetValue(relativePath, out var tags) ? NormalizeTags(tags) : [];

    public static string[] NormalizeTags(IEnumerable<string>? tags) => (tags ?? [])
        .Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim())
        .Where(tag => tag.Length <= 32 && !tag.Any(char.IsControl))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();

    public void EditTags(IEnumerable<string> paths, IEnumerable<string> tags, TagEditMode mode)
    {
        string[] edits = NormalizeTags(tags);
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] existing = TagsFor(path);
            string[] result = mode switch
            {
                TagEditMode.Add => NormalizeTags(existing.Concat(edits)),
                TagEditMode.Remove => existing.Except(edits, StringComparer.OrdinalIgnoreCase).ToArray(),
                _ => edits
            };
            if (result.Length == 0) ModTags.Remove(path); else ModTags[path] = result.ToList();
        }
    }

    public bool Matches(string relativePath, string name, string? search, string? tag)
        => (string.IsNullOrEmpty(tag) || TagsFor(relativePath).Contains(tag, StringComparer.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(search) || name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || TagsFor(relativePath).Any(value => value.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)));

    public void SaveFilter(LocalModFilter filter)
    {
        filter.Name = filter.Name.Trim();
        if (filter.Name.Length is 0 or > 64) throw new ArgumentException("Filter names must contain 1–64 characters.");
        Filters.RemoveAll(item => item.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase));
        Filters.Insert(0, new LocalModFilter { Name = filter.Name, Character = filter.Character, Search = filter.Search, Tag = filter.Tag });
        Filters = Filters.Take(50).ToList();
    }

    public void RenameCharacter(string oldRelative, string newRelative)
    {
        string prefix = oldRelative.TrimEnd('/') + "/";
        foreach (string key in ModTags.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            ModTags[newRelative.TrimEnd('/') + "/" + key[prefix.Length..]] = ModTags[key];
            ModTags.Remove(key);
        }
        foreach (var filter in Filters.Where(item => item.Character.Equals(oldRelative, StringComparison.OrdinalIgnoreCase)))
            filter.Character = newRelative;
    }

    public void Normalize()
    {
        ModTags = (ModTags ?? []).GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => NormalizeTags(group.Last().Value).ToList(), StringComparer.OrdinalIgnoreCase);
        Filters = (Filters ?? []).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).Take(50).ToList();
        foreach (var filter in Filters)
        { filter.Character ??= ""; filter.Search ??= ""; filter.Tag ??= ""; }
    }
}

public sealed record OnlineAuthorVisit(int Id, string Name, string CategoryId, DateTimeOffset VisitedAt);
