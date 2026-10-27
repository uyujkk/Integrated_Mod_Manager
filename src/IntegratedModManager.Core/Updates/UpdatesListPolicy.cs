using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Updates;

/// <summary>Where a tracked Mod stands after the last check.</summary>
public enum TrackedModStatus { Update, Failed, NotChecked, UpToDate }

/// <summary>The tracked Mods list's chips.</summary>
public enum TrackedModFilter { All, Updates, Failed, UpToDate }

/// <summary>The download history's chips.</summary>
public enum DownloadHistoryFilter { All, Completed, Failed, Canceled }

/// <summary>Filtering and ordering for the updates page's two long lists.</summary>
public static class UpdatesListPolicy
{
    public static TrackedModStatus Status(bool isChecked, bool checkFailed, bool hasUpdate)
        => !isChecked ? TrackedModStatus.NotChecked
            : checkFailed ? TrackedModStatus.Failed
            : hasUpdate ? TrackedModStatus.Update
            : TrackedModStatus.UpToDate;

    public static bool Matches(TrackedModStatus status, TrackedModFilter filter) => filter switch
    {
        TrackedModFilter.Updates => status == TrackedModStatus.Update,
        TrackedModFilter.Failed => status == TrackedModStatus.Failed,
        TrackedModFilter.UpToDate => status == TrackedModStatus.UpToDate,
        _ => true
    };

    /// <summary>Every search term must appear in the title or the local path.</summary>
    public static bool MatchesSearch(string title, string path, string? query)
    {
        string[] terms = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term => title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || path.Contains(term, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Updates first, then failed checks, unchecked Mods and up-to-date ones; by title within each.</summary>
    public static IEnumerable<T> Order<T>(IEnumerable<T> items, Func<T, TrackedModStatus> status, Func<T, string> title)
        => items.OrderBy(item => (int)status(item)).ThenBy(title, StringComparer.CurrentCultureIgnoreCase);

    public static bool Matches(DownloadTaskState state, DownloadHistoryFilter filter) => filter switch
    {
        DownloadHistoryFilter.Completed => state == DownloadTaskState.Completed,
        DownloadHistoryFilter.Failed => state == DownloadTaskState.Failed,
        DownloadHistoryFilter.Canceled => state == DownloadTaskState.Canceled,
        _ => true
    };
}
