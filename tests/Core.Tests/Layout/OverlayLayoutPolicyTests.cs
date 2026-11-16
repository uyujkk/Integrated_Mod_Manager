using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

// The overlay's size and place, remembered per display (overlay redesign, tier 2).
public sealed class OverlayLayoutPolicyTests
{
    private static readonly OverlayDisplay Primary = new(@"\\.\DISPLAY1|3840x2160", new WindowRect(0, 0, 3840, 2100), 1.5);
    private static readonly OverlayDisplay Second = new(@"\\.\DISPLAY2|1920x1080", new WindowRect(3840, 0, 1920, 1040), 1.0);

    [Fact]
    public void FirstUseOpensAtTheTopRightOfThePrimaryDisplay()
    {
        var (bounds, maxHeight, display) = OverlayLayoutPolicy.Restore([], [Primary, Second], Primary);
        Assert.Equal(Primary, display);
        Assert.Equal(860, maxHeight);
        Assert.Equal(new WindowRect(3840 - 630 - 30, 30, 630, 1290), bounds);
    }

    [Fact]
    public void ThePlaceIsKeptPerDisplayInDipAndRestoredOnThatDisplay()
    {
        var window = new WindowRect(3840 + 100, 50, 500, 600);
        List<OverlayPlacement> saved = OverlayLayoutPolicy.Remember([], window, Second, 640);
        Assert.Equal(Second.Key, saved[0].Display);
        Assert.Equal((100.0, 50.0, 500.0, 640.0), (saved[0].Left, saved[0].Top, saved[0].Width, saved[0].MaxHeight));

        var (bounds, maxHeight, display) = OverlayLayoutPolicy.Restore(saved, [Primary, Second], Primary);
        Assert.Equal(Second, display);
        Assert.Equal(640, maxHeight);
        Assert.Equal(new WindowRect(3940, 50, 500, 640), bounds);
    }

    [Fact]
    public void AScaleChangeKeepsTheSizeInDip()
    {
        List<OverlayPlacement> saved = OverlayLayoutPolicy.Remember([], new WindowRect(3940, 50, 500, 600), Second, 600);
        OverlayDisplay scaled = Second with { Scale = 1.25 };
        var (bounds, _, _) = OverlayLayoutPolicy.Restore(saved, [Primary, scaled], Primary);
        Assert.Equal(new WindowRect(3840 + 125, 62, 625, 750), bounds); // 62.5 rounds to even
    }

    [Fact]
    public void AnUnpluggedDisplayFallsBackToTheNextRememberedOneThenThePrimary()
    {
        List<OverlayPlacement> saved = OverlayLayoutPolicy.Remember([], new WindowRect(200, 300, 600, 900), Primary, 700);
        saved = OverlayLayoutPolicy.Remember(saved, new WindowRect(3940, 50, 500, 600), Second, 600);
        Assert.Equal([Second.Key, Primary.Key], saved.Select(placement => placement.Display));

        var (bounds, maxHeight, display) = OverlayLayoutPolicy.Restore(saved, [Primary], Primary);
        Assert.Equal(Primary, display);
        Assert.Equal(700, maxHeight);
        Assert.Equal(new WindowRect(200, 300, 600, 1050), bounds);

        var (fallback, _, onPrimary) = OverlayLayoutPolicy.Restore([saved[0]], [Primary], Primary);
        Assert.Equal(Primary, onPrimary);
        Assert.Equal(new WindowRect(3840 - 630 - 30, 30, 630, 1290), fallback);
    }

    [Fact]
    public void ARestoredPlaceIsKeptInsideTheWorkArea()
    {
        var off = new OverlayPlacement { Display = Second.Key, Left = 1800, Top = 1000, Width = 3000, MaxHeight = 5000 };
        var (bounds, _, _) = OverlayLayoutPolicy.Restore([off], [Second], Primary);
        Assert.Equal(new WindowRect(3840, 0, 1920, 1040), bounds);
        var tiny = new OverlayPlacement { Display = Second.Key, Left = 10, Top = 10, Width = 50, MaxHeight = 10 };
        var (small, maxHeight, _) = OverlayLayoutPolicy.Restore([tiny], [Second], Primary);
        Assert.Equal((300, 120), (small.Width, small.Height));
        Assert.Equal(120, maxHeight);
    }

    [Fact]
    public void RememberingKeepsOnePlacePerDisplayAndAtMostEight()
    {
        List<OverlayPlacement> saved = [];
        for (int i = 0; i < 10; i++)
            saved = OverlayLayoutPolicy.Remember(saved, new WindowRect(0, 0, 400, 400), new OverlayDisplay("D" + i, new WindowRect(0, 0, 1000, 1000), 1), 400);
        saved = OverlayLayoutPolicy.Remember(saved, new WindowRect(10, 10, 400, 400), new OverlayDisplay("D9", new WindowRect(0, 0, 1000, 1000), 1), 400);
        Assert.Equal(8, saved.Count);
        Assert.Equal("D9", saved[0].Display);
        Assert.Single(saved, placement => placement.Display == "D9");
    }

    [Theory]
    [InlineData(10, 10, 0, 0)]          // near the top left: both edges stick
    [InlineData(30, 30, 30, 30)]        // further than 16 DIP at 150%: stays
    [InlineData(3840 - 630 - 20, 500, 3840 - 630, 500)] // near the right edge
    [InlineData(500, 2100 - 900 - 23, 500, 2100 - 900)] // near the bottom edge
    public void DraggingNearAnEdgeSnapsToIt(int x, int y, int snappedX, int snappedY)
        => Assert.Equal(new WindowRect(snappedX, snappedY, 630, 900), OverlayLayoutPolicy.Snap(new WindowRect(x, y, 630, 900), Primary));

    [Fact]
    public void TheDisplayIsTheOneMostOfTheWindowIsOn()
    {
        Assert.Equal(Second, OverlayLayoutPolicy.DisplayOf([Primary, Second], new WindowRect(3700, 100, 600, 400)));
        Assert.Equal(Primary, OverlayLayoutPolicy.DisplayOf([Primary, Second], new WindowRect(3500, 100, 600, 400)));
        Assert.Equal(Second, OverlayLayoutPolicy.DisplayOf([Primary, Second], new WindowRect(6000, 100, 300, 300)));
        Assert.Null(OverlayLayoutPolicy.DisplayOf([], new WindowRect(0, 0, 10, 10)));
    }

    [Theory]
    [InlineData(null, 14)]
    [InlineData(12, 12)]
    [InlineData(20, 20)]
    [InlineData(15, 14)]
    [InlineData(0, 14)]
    [InlineData(99, 14)]
    public void TheCompactFontSizeIsOneOfTheOfferedSizes(int? saved, int expected)
        => Assert.Equal(expected, OverlayLayoutPolicy.CompactFontSize(saved));

    [Theory]
    [InlineData(400, 860, 1.5, 2100, 0, 600)]   // follows the content
    [InlineData(1200, 860, 1.5, 2100, 0, 1290)] // up to what the user allowed
    [InlineData(50, 860, 1.5, 2100, 0, 180)]    // at least the minimum
    [InlineData(400, 860, 1.5, 2100, 12, 612)]  // plus the window frame
    [InlineData(1200, 2000, 1.0, 900, 0, 860)]  // never past the work area (less the margins)
    public void TheHeightFollowsTheContent(double content, double max, double scale, int workArea, int frame, int expected)
        => Assert.Equal(expected, OverlayLayoutPolicy.FitHeight(content, max, scale, workArea, frame));
}
