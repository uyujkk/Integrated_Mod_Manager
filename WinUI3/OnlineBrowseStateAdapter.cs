using System.Collections.ObjectModel;

namespace ModFolderCopier.WinUI;

// No XAML dependency: tests exercise the same collection updates used by both views.
internal static class OnlineBrowseStateAdapter
{
    public static bool Synchronize<T>(ObservableCollection<T> current, IReadOnlyList<T> desired) where T : class
    {
        bool changed = false;
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < current.Count && ReferenceEquals(current[i], desired[i])) continue;
            int existing = -1;
            for (int j = i + 1; j < current.Count; j++)
            {
                if (ReferenceEquals(current[j], desired[i])) { existing = j; break; }
            }
            if (existing >= 0) current.Move(existing, i);
            else current.Insert(i, desired[i]);
            changed = true;
        }
        while (current.Count > desired.Count)
        {
            current.RemoveAt(current.Count - 1);
            changed = true;
        }
        return changed;
    }

    public static bool MatchesSearch(string? title, string? character, string? author,
        string? search, IEnumerable<string>? characterAliases = null)
    {
        string term = search?.Trim() ?? string.Empty;
        if (term.Length == 0) return true;
        if (term.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
        {
            term = term[7..].Trim();
            return term.Length > 0 && Contains(author, term);
        }
        return Contains(title, term) || Contains(character, term) || Contains(author, term)
            || characterAliases?.Any(alias => Contains(alias, term)) == true;
    }

    private static bool Contains(string? value, string term)
        => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
