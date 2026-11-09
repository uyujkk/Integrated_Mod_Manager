using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

// The getting-started tips open where they fit in the window (J8 follow-up: step 2 used to open beside the path
// settings, outside the window).
public sealed class TipPlacementPolicyTests
{
    private const double Tip = 360, TipHigh = 300;

    [Fact]
    public void AWideTargetWithRoomBelowGetsTheTipBelow()
        => Assert.Equal(TipPlacement.Bottom, TipPlacementPolicy.Choose(40, 300, 900, 80, 1000, 800, Tip, TipHigh));

    [Fact]
    public void AWideTargetLowInTheWindowGetsTheTipAbove()
        => Assert.Equal(TipPlacement.Top, TipPlacementPolicy.Choose(40, 560, 900, 80, 1000, 800, Tip, TipHigh));

    [Fact]
    public void ATargetTallerThanTheRoomGetsTheTipInTheMiddle()
        => Assert.Equal(TipPlacement.Center, TipPlacementPolicy.Choose(40, 200, 900, 500, 1000, 800, Tip, TipHigh));

    [Fact]
    public void ATallTargetReachingPastTheWindowIsPointedAtFromAbove()
        => Assert.Equal(TipPlacement.Top, TipPlacementPolicy.Choose(40, 330, 900, 700, 1000, 800, Tip, TipHigh));

    [Theory]
    [InlineData(40, -60)]   // scrolled above the page
    [InlineData(40, 790)]   // below the window
    [InlineData(-30, 300)]  // off the left edge
    public void ATargetNotFullyOnScreenGetsTheTipInTheMiddle(double left, double top)
        => Assert.Equal(TipPlacement.Center, TipPlacementPolicy.Choose(left, top, 200, 80, 1000, 800, Tip, TipHigh));

    [Fact]
    public void TheNewRepositoryButtonKeepsItsTipBelowAndToTheLeft()
        => Assert.Equal(TipPlacement.BottomLeft, TipPlacementPolicy.Choose(820, 90, 140, 36, 1000, 800, Tip, TipHigh, preferBelowLeft: true));

    [Fact]
    public void BelowLeftNeedsRoomToTheLeft()
        => Assert.Equal(TipPlacement.Bottom, TipPlacementPolicy.Choose(10, 90, 140, 36, 1000, 800, Tip, TipHigh, preferBelowLeft: true));

    [Fact]
    public void AWindowNarrowerThanTheTipGetsItInTheMiddle()
        => Assert.Equal(TipPlacement.Center, TipPlacementPolicy.Choose(10, 90, 140, 36, 320, 800, Tip, TipHigh));

    [Fact]
    public void AnEmptyTargetGetsTheTipInTheMiddle()
        => Assert.Equal(TipPlacement.Center, TipPlacementPolicy.Choose(10, 90, 0, 0, 1000, 800, Tip, TipHigh));
}
