namespace IntegratedModManager.Core.Repository;

public enum CommandPaletteKind { Command, Character, Mod }

/// <summary>One searchable palette row. <see cref="Key"/> tells the window what to open or run.</summary>
public readonly record struct CommandPaletteEntry(CommandPaletteKind Kind, string Title, string Detail, string Key);

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

    /// <summary>0 when any term is missing; otherwise the sum of each term's best match.</summary>
    public static int Score(CommandPaletteEntry entry, IReadOnlyList<string> terms)
    {
        int total = 0;
        foreach (string term in terms)
        {
            int score = TermScore(entry.Title, entry.Detail, term);
            if (score == 0) return 0;
            total += score;
        }
        return total;
    }

    // Exact title, then title prefix, then the start of a word inside the title
    // ("cas" in "Outfit-Casual"), then anywhere in the title, then only the detail line.
    private static int TermScore(string title, string detail, string term)
    {
        if (string.Equals(title, term, StringComparison.OrdinalIgnoreCase)) return 100;
        int index = title.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index == 0) return 60;
        if (index > 0) return char.IsLetterOrDigit(title[index - 1]) ? 20 : 40;
        return detail.Contains(term, StringComparison.OrdinalIgnoreCase) ? 5 : 0;
    }
}
