using System.Globalization;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineBrowseOrderTests
{
    private static OnlineModCard Mod(string title, int downloads = 0, int likes = 0, int views = 0, double hotness = 0, int day = 1) => new()
    {
        Title = title, Downloads = downloads, Likes = likes, Views = views, HotnessScore = hotness,
        UpdatedAt = new DateTimeOffset(2026, 10, day, 0, 0, 0, TimeSpan.Zero)
    };

    private static readonly OnlineModCard[] Page =
    [
        Mod("a", downloads: 10, likes: 3, views: 100, hotness: 5, day: 3),
        Mod("b", downloads: 30, likes: 1, views: 300, hotness: 9, day: 1),
        Mod("c", downloads: 20, likes: 3, views: 200, hotness: 7, day: 2)
    ];

    [Theory]
    [InlineData(OnlineSortMode.Hotness, "bca")]
    [InlineData(OnlineSortMode.Downloads, "bca")]
    [InlineData(OnlineSortMode.Views, "bca")]
    [InlineData(OnlineSortMode.Updated, "acb")]
    public void Sort_OrdersByTheChosenValueDescending(OnlineSortMode mode, string expected)
        => Assert.Equal(expected, string.Concat(OnlineBrowseOrder.Sort(Page, mode).Select(mod => mod.Title)));

    [Fact]
    public void Sort_BreaksTiesByHotness()
        => Assert.Equal("cab", string.Concat(OnlineBrowseOrder.Sort(Page, OnlineSortMode.Likes).Select(mod => mod.Title)));

    [Fact]
    public void Sort_ByHotnessBreaksTiesByDownloads()
    {
        OnlineModCard[] tied = [Mod("x", downloads: 1, hotness: 4), Mod("y", downloads: 2, hotness: 4)];
        Assert.Equal("yx", string.Concat(OnlineBrowseOrder.Sort(tied, OnlineSortMode.Hotness).Select(mod => mod.Title)));
    }

    [Theory]
    [InlineData(999, true, "999")]
    [InlineData(9_999, true, "9,999")]
    [InlineData(10_000, true, "1万")]
    [InlineData(69_279, true, "6.9万")]
    [InlineData(150_000_000, true, "1.5亿")]
    [InlineData(999, false, "999")]
    [InlineData(3_365, false, "3.4K")]
    [InlineData(2_500_000, false, "2.5M")]
    public void CompactCount_UsesTheLanguagesUnits(int value, bool chinese, string expected)
        => Assert.Equal(expected, OnlineBrowseOrder.CompactCount(value, chinese, CultureInfo.InvariantCulture));

    [Fact]
    public void Paging_StartsOnTheOnlyPage()
    {
        var paging = new OnlinePaging();
        Assert.Equal((1, 1, 0), (paging.CurrentPage, paging.TotalPages, paging.TotalCount));
        Assert.False(paging.HasPrevious);
        Assert.False(paging.HasNext);
    }

    [Fact]
    public void Paging_ResultEnablesNextUntilTheLastPage()
    {
        var paging = new OnlinePaging();
        paging.ApplyResult(664, 17);
        Assert.True(paging.HasNext);
        paging.CurrentPage = 17;
        Assert.True(paging.HasPrevious);
        Assert.False(paging.HasNext);
    }

    [Fact]
    public void Paging_KeepsAtLeastOnePage()
    {
        var paging = new OnlinePaging { CurrentPage = 0 };
        paging.ApplyResult(-5, 0);
        Assert.Equal((1, 1, 0), (paging.CurrentPage, paging.TotalPages, paging.TotalCount));
    }

    [Fact]
    public void Paging_ResetReturnsToTheFirstPageWithUnknownTotals()
    {
        var paging = new OnlinePaging { CurrentPage = 4 };
        paging.ApplyResult(100, 5);
        paging.Reset();
        Assert.Equal((1, 1, 0), (paging.CurrentPage, paging.TotalPages, paging.TotalCount));
    }

    [Fact]
    public void Paging_ClearTotalsKeepsThePage()
    {
        var paging = new OnlinePaging { CurrentPage = 3 };
        paging.ApplyResult(100, 5);
        paging.ClearTotals();
        Assert.Equal((3, 1, 0), (paging.CurrentPage, paging.TotalPages, paging.TotalCount));
    }
}
