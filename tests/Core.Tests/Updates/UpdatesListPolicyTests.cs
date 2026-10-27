using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;

namespace IntegratedModManager.Core.Tests;

public sealed class UpdatesListPolicyTests
{
    [Theory]
    [InlineData(false, false, false, TrackedModStatus.NotChecked)]
    [InlineData(false, true, true, TrackedModStatus.NotChecked)]
    [InlineData(true, true, true, TrackedModStatus.Failed)]
    [InlineData(true, false, true, TrackedModStatus.Update)]
    [InlineData(true, false, false, TrackedModStatus.UpToDate)]
    public void Status_FollowsTheLastCheck(bool isChecked, bool failed, bool hasUpdate, TrackedModStatus expected)
        => Assert.Equal(expected, UpdatesListPolicy.Status(isChecked, failed, hasUpdate));

    [Theory]
    [InlineData(TrackedModStatus.Update, TrackedModFilter.All, true)]
    [InlineData(TrackedModStatus.NotChecked, TrackedModFilter.All, true)]
    [InlineData(TrackedModStatus.Update, TrackedModFilter.Updates, true)]
    [InlineData(TrackedModStatus.UpToDate, TrackedModFilter.Updates, false)]
    [InlineData(TrackedModStatus.Failed, TrackedModFilter.Failed, true)]
    [InlineData(TrackedModStatus.NotChecked, TrackedModFilter.Failed, false)]
    [InlineData(TrackedModStatus.UpToDate, TrackedModFilter.UpToDate, true)]
    [InlineData(TrackedModStatus.NotChecked, TrackedModFilter.UpToDate, false)]
    public void TrackedFilter_KeepsTheChosenStatus(TrackedModStatus status, TrackedModFilter filter, bool expected)
        => Assert.Equal(expected, UpdatesListPolicy.Matches(status, filter));

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("OUTFIT", true)]
    [InlineData("demo night", true)]
    [InlineData("demo formal", false)]
    public void Search_NeedsEveryTermInTitleOrPath(string? query, bool expected)
        => Assert.Equal(expected, UpdatesListPolicy.MatchesSearch("Outfit-Night", @"G:\Repo\Demo B\Outfit-Night", query));

    [Fact]
    public void Order_PutsUpdatesThenFailuresFirst()
    {
        (string Title, TrackedModStatus Status)[] items =
        [
            ("b", TrackedModStatus.UpToDate), ("a", TrackedModStatus.UpToDate), ("c", TrackedModStatus.Failed),
            ("d", TrackedModStatus.Update), ("e", TrackedModStatus.NotChecked), ("A2", TrackedModStatus.Update)
        ];
        Assert.Equal("A2 d c e a b", string.Join(' ', UpdatesListPolicy.Order(items, item => item.Status, item => item.Title).Select(item => item.Title)));
    }

    [Theory]
    [InlineData(DownloadTaskState.Completed, DownloadHistoryFilter.All, true)]
    [InlineData(DownloadTaskState.Completed, DownloadHistoryFilter.Completed, true)]
    [InlineData(DownloadTaskState.Failed, DownloadHistoryFilter.Completed, false)]
    [InlineData(DownloadTaskState.Failed, DownloadHistoryFilter.Failed, true)]
    [InlineData(DownloadTaskState.Canceled, DownloadHistoryFilter.Canceled, true)]
    [InlineData(DownloadTaskState.Completed, DownloadHistoryFilter.Canceled, false)]
    public void HistoryFilter_KeepsTheChosenOutcome(DownloadTaskState state, DownloadHistoryFilter filter, bool expected)
        => Assert.Equal(expected, UpdatesListPolicy.Matches(state, filter));
}
