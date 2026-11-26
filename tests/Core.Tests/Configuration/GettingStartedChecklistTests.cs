using IntegratedModManager.Core.Configuration;

namespace IntegratedModManager.Core.Tests;

public sealed class GettingStartedChecklistTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void Load_ShowsOnAFirstInstallOrKeepsWhatWasSaved(bool firstInstall, bool saved, bool pending)
        => Assert.Equal(pending, GettingStartedChecklist.Load(firstInstall, saved).IsPending);

    [Fact]
    public void Upgrade_NeverShowsTheCard()
        => Assert.False(GettingStartedChecklist.Load(firstInstall: false, savedPending: false).IsPending);

    [Fact]
    public void CloseHidesItAndShowBringsItBack()
    {
        GettingStartedChecklist checklist = GettingStartedChecklist.Load(true, false);
        checklist.Close();
        Assert.False(checklist.IsPending);
        checklist.Show();
        Assert.True(checklist.IsPending);
    }

    [Fact]
    public void EachTaskTicksOffFromWhatTheAppKnows()
    {
        var progress = new GettingStartedProgress(PathsReady: true, HasMods: false, HasDeployed: false, UsedSearch: true, VisitedShortcuts: false);
        Assert.True(GettingStartedChecklist.IsDone(GettingStartedTask.Paths, progress));
        Assert.False(GettingStartedChecklist.IsDone(GettingStartedTask.FirstMod, progress));
        Assert.False(GettingStartedChecklist.IsDone(GettingStartedTask.Deploy, progress));
        Assert.True(GettingStartedChecklist.IsDone(GettingStartedTask.Search, progress));
        Assert.False(GettingStartedChecklist.IsDone(GettingStartedTask.Shortcuts, progress));
        Assert.Equal(2, GettingStartedChecklist.DoneCount(progress));
        Assert.False(GettingStartedChecklist.AllDone(progress));
    }

    [Fact]
    public void NextIsTheFirstOpenTaskInOrder()
    {
        Assert.Equal(GettingStartedTask.Paths, GettingStartedChecklist.Next(default));
        Assert.Equal(GettingStartedTask.Deploy,
            GettingStartedChecklist.Next(new GettingStartedProgress(true, true, false, false, false)));
        // Tasks can be done in any order; the first open one is still next.
        Assert.Equal(GettingStartedTask.FirstMod,
            GettingStartedChecklist.Next(new GettingStartedProgress(true, false, false, true, true)));
    }

    [Fact]
    public void AllDoneLeavesNothingNext()
    {
        var progress = new GettingStartedProgress(true, true, true, true, true);
        Assert.True(GettingStartedChecklist.AllDone(progress));
        Assert.Null(GettingStartedChecklist.Next(progress));
        Assert.Equal(GettingStartedChecklist.Tasks.Count, GettingStartedChecklist.DoneCount(progress));
    }
}
