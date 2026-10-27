using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class UpdatesLayoutPolicyTests
{
    [Theory]
    [InlineData(3700, 3)]
    [InlineData(1700, 3)]
    [InlineData(1699, 2)]
    [InlineData(1230, 2)]
    [InlineData(1100, 2)]
    [InlineData(900, 2)]
    [InlineData(899, 1)]
    [InlineData(700, 1)]
    public void Columns_SwitchAtThePageWidthBreakpoints(double width, int expected)
        => Assert.Equal(expected, UpdatesLayoutPolicy.Columns(width));

    [Theory]
    [InlineData(1000, 60, 912)]
    [InlineData(508, 60, 420)]
    public void FixedColumnHeight_FillsTheWindowBelowTheHeader(double viewport, double header, double expected)
        => Assert.Equal(expected, UpdatesLayoutPolicy.FixedColumnHeight(viewport, header));

    [Theory]
    [InlineData(500, 60)]
    [InlineData(double.NaN, 60)]
    public void FixedColumnHeight_LetsShortWindowsScroll(double viewport, double header)
        => Assert.Null(UpdatesLayoutPolicy.FixedColumnHeight(viewport, header));
}
