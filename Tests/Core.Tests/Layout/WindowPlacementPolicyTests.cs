using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class WindowPlacementPolicyTests
{
    private static readonly WindowRect FullHd = new(0, 0, 1920, 1040);
    private static readonly WindowRect FourK = new(0, 0, 3840, 2100);

    [Fact]
    public void Proportional_UsesEightyPercentOfTheWorkAreaCentered()
    {
        Assert.Equal(new WindowRect(192, 104, 1536, 832), WindowPlacementPolicy.Proportional(FullHd));
        Assert.Equal(new WindowRect(384, 210, 3072, 1680), WindowPlacementPolicy.Proportional(FourK));
    }

    [Fact]
    public void Proportional_KeepsTheMinimumSizeButNeverExceedsTheScreen()
    {
        Assert.Equal(new WindowRect(90, 34, 1100, 700), WindowPlacementPolicy.Proportional(new WindowRect(0, 0, 1280, 768)));
        Assert.Equal(new WindowRect(1920, 0, 1024, 700), WindowPlacementPolicy.Proportional(new WindowRect(1920, 0, 1024, 700)));
    }

    [Fact]
    public void Normal_ReplacesMissingOrScreenFillingBounds()
    {
        WindowRect proportional = WindowPlacementPolicy.Proportional(FullHd);

        Assert.Equal(proportional, WindowPlacementPolicy.Normal(null, FullHd));
        Assert.Equal(proportional, WindowPlacementPolicy.Normal(new WindowRect(-8, -8, 1936, 1056), FullHd));
        Assert.Equal(proportional, WindowPlacementPolicy.Normal(new WindowRect(0, 0, 1900, 1000), FullHd));
    }

    [Fact]
    public void Normal_KeepsSmallerBoundsInsideTheWorkArea()
    {
        Assert.Equal(new WindowRect(100, 50, 1300, 800), WindowPlacementPolicy.Normal(new WindowRect(100, 50, 1300, 800), FullHd));
        Assert.Equal(new WindowRect(620, 240, 1300, 800), WindowPlacementPolicy.Normal(new WindowRect(3000, 900, 1300, 800), FullHd));
        Assert.Equal(new WindowRect(0, 0, 1100, 700), WindowPlacementPolicy.Normal(new WindowRect(0, 0, 400, 300), FullHd));
    }

    [Fact]
    public void FillsWorkArea_NeedsBothDimensions()
    {
        Assert.True(WindowPlacementPolicy.FillsWorkArea(new WindowRect(0, 0, 1824, 988), FullHd));
        Assert.False(WindowPlacementPolicy.FillsWorkArea(new WindowRect(0, 0, 1920, 900), FullHd));
    }
}
