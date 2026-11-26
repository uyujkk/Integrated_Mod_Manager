namespace IntegratedModManager.Core.Repository;

/// <summary>
/// Pages and commands, then the feature directory (every action and where it is), characters and Mods.
/// </summary>
public enum CommandPaletteKind { Command, Character, Mod, Feature }

/// <summary>
/// One searchable palette row. <see cref="Key"/> tells the window what to open or run. A feature row also carries
/// other words it is found by (<see cref="Keywords"/>), its keyboard shortcut and the area it belongs to.
/// </summary>
public readonly record struct CommandPaletteEntry(CommandPaletteKind Kind, string Title, string Detail, string Key,
    string Keywords = "", string Shortcut = "", int Area = 0);

/// <summary>Pure ranking for the Ctrl+K palette; it never opens or changes anything itself.</summary>
public static class CommandPaletteSearch
{
    // An empty query lists only the commands, so the palette opens on something useful
    // instead of the first few hundred Mods.
    public static IReadOnlyList<CommandPaletteEntry> Rank(IEnumerable<CommandPaletteEntry> entries, string? query, int limit)
    {
        if (limit <= 0) return [];
        string[] terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return entries.Where(entry => entry.Kind == CommandPaletteKind.Command).Take(limit).ToArray();
        return entries
            .Select((entry, index) => (entry, index, score: Score(entry, terms)))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score)
            .ThenBy(match => match.index)
            .Take(limit)
            .Select(match => match.entry)
            .ToArray();
    }

    /// <summary>
    /// The detailed palette before anything is typed: every feature, area by area in the order the areas are numbered,
    /// and inside an area in the order they were given.
    /// </summary>
    public static IReadOnlyList<CommandPaletteEntry> Directory(IEnumerable<CommandPaletteEntry> entries)
        => entries.Where(entry => entry.Kind == CommandPaletteKind.Feature)
            .Select((entry, index) => (entry, index))
            .OrderBy(item => item.entry.Area).ThenBy(item => item.index)
            .Select(item => item.entry)
            .ToArray();

    /// <summary>0 when any term is missing; otherwise the sum of each term's best match.</summary>
    public static int Score(CommandPaletteEntry entry, IReadOnlyList<string> terms)
    {
        int total = 0;
        foreach (string term in terms)
        {
            int score = TermScore(entry.Title, entry.Detail, entry.Keywords, term);
            if (score == 0) return 0;
            total += score;
        }
        return total;
    }

    // Exact title, then title prefix, then the start of a word inside the title
    // ("cas" in "Outfit-Casual"), then anywhere in the title, then another word for it, then only the detail line.
    private static int TermScore(string title, string detail, string keywords, string term)
    {
        if (string.Equals(title, term, StringComparison.OrdinalIgnoreCase)) return 100;
        int index = title.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index == 0) return 60;
        if (index > 0) return char.IsLetterOrDigit(title[index - 1]) ? 20 : 40;
        if (keywords.Contains(term, StringComparison.OrdinalIgnoreCase)) return 10;
        return detail.Contains(term, StringComparison.OrdinalIgnoreCase) ? 5 : 0;
    }
}
