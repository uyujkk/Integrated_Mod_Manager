using IntegratedModManager.Core.Configuration;

namespace IntegratedModManager.Core.Tests;

public sealed class GettingStartedTipsTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void Load_StartsOnAFirstInstallOrKeepsWhatWasSaved(bool firstInstall, bool saved, bool pending)
        => Assert.Equal(pending, GettingStartedTips.Load(firstInstall, saved).IsPending);

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void ShouldShow_OnlyOnTheDashboardWithoutADialog(bool dashboard, bool dialog, bool shown)
        => Assert.Equal(shown, GettingStartedTips.Load(true, false).ShouldShow(dashboard, dialog));

    [Fact]
    public void ShouldShow_NeverOnceClosed()
    {
        GettingStartedTips tips = GettingStartedTips.Load(true, false);
        tips.Close();
        Assert.False(tips.IsPending);
        Assert.False(tips.ShouldShow(dashboardShown: true, dialogOpen: false));
    }

    [Fact]
    public void Next_WalksTheStepsAndFinishesAfterTheLast()
    {
        GettingStartedTips tips = GettingStartedTips.Load(true, false);
        Assert.Equal(GettingStartedStep.NewRepository, tips.Current);
        Assert.Equal(1, tips.Number);

        tips.Next();
        Assert.Equal(GettingStartedStep.Paths, tips.Current);
        tips.Next();
        Assert.Equal(GettingStartedStep.OpenMods, tips.Current);
        Assert.True(tips.IsLast);
        Assert.Equal(3, tips.Number);

        tips.Next();
        Assert.False(tips.IsPending);
    }

    [Fact]
    public void Close_FromTheMiddleEndsTheTips()
    {
        GettingStartedTips tips = GettingStartedTips.Load(true, false);
        tips.Next();
        tips.Close();
        Assert.False(tips.IsPending);
        tips.Next();
        Assert.False(tips.IsPending);
    }

    [Fact]
    public void Upgrade_NeverStartsTheTips()
        => Assert.False(GettingStartedTips.Load(firstInstall: false, savedPending: false).ShouldShow(true, false));
}
