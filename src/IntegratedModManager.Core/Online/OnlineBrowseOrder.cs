using System.Globalization;

namespace IntegratedModManager.Core.Online;

// The names are stored in browse sessions (OnlineBrowseSession.Sort), so they must not change.
public enum OnlineSortMode
{
    Hotness,
    Downloads,
    Likes,
    Views,
    Updated
}

public static class OnlineBrowseOrder
{
    /// <summary>Orders a page of Mods; ties fall back to hotness (to downloads when sorting by hotness).</summary>
    public static IEnumerable<OnlineModCard> Sort(IEnumerable<OnlineModCard> mods, OnlineSortMode mode) => mode switch
    {
        OnlineSortMode.Downloads => mods.OrderByDescending(mod => mod.Downloads).ThenByDescending(mod => mod.HotnessScore),
        OnlineSortMode.Likes => mods.OrderByDescending(mod => mod.Likes).ThenByDescending(mod => mod.HotnessScore),
        OnlineSortMode.Views => mods.OrderByDescending(mod => mod.Views).ThenByDescending(mod => mod.HotnessScore),
        OnlineSortMode.Updated => mods.OrderByDescending(mod => mod.UpdatedAt).ThenByDescending(mod => mod.HotnessScore),
        _ => mods.OrderByDescending(mod => mod.HotnessScore).ThenByDescending(mod => mod.Downloads)
    };

    /// <summary>
    /// Short counts for cards: 万 and 亿 in Chinese, K and M in English; small values keep
    /// their digits with group separators.
    /// </summary>
    public static string CompactCount(int value, bool chinese, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (chinese)
        {
            return value >= 100_000_000 ? (value / 100_000_000d).ToString("0.#", culture) + "亿"
                : value >= 10_000 ? (value / 10_000d).ToString("0.#", culture) + "万"
                : value.ToString("N0", culture);
        }

        return value >= 1_000_000 ? (value / 1_000_000d).ToString("0.#", culture) + "M"
            : value >= 1_000 ? (value / 1_000d).ToString("0.#", culture) + "K"
            : value.ToString("N0", culture);
    }
}

/// <summary>The server-side page of the online list: which page is shown and how many there are.</summary>
public sealed class OnlinePaging
{
    private int _currentPage = 1;

    public int CurrentPage
    {
        get => _currentPage;
        set => _currentPage = Math.Max(1, value);
    }

    public int TotalPages { get; private set; } = 1;

    public int TotalCount { get; private set; }

    public bool HasPrevious => CurrentPage > 1;

    public bool HasNext => CurrentPage < TotalPages;

    /// <summary>Totals are unknown until the next page result arrives.</summary>
    public void ClearTotals()
    {
        TotalCount = 0;
        TotalPages = 1;
    }

    /// <summary>Back to the first page with unknown totals, for a new repository, character or author.</summary>
    public void Reset()
    {
        CurrentPage = 1;
        ClearTotals();
    }

    public void ApplyResult(int totalCount, int totalPages)
    {
        TotalCount = Math.Max(0, totalCount);
        TotalPages = Math.Max(1, totalPages);
    }
}
