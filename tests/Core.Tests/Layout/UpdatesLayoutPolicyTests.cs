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
    [InlineData(1099, 1)]
    [InlineData(700, 1)]
    public void Columns_SwitchAtThePageWidthBreakpoints(double width, int expected)
        => Assert.Equal(expected, UpdatesLayoutPolicy.Columns(width));

    [Theory]
    [InlineData(3700, true)]   // three columns, tracked column ~1450
    [InlineData(1800, false)]  // three columns, tracked column ~710
    [InlineData(1650, true)]   // two columns, tracked column ~1010
    [InlineData(1230, false)]  // two columns, tracked column ~750
    [InlineData(1000, true)]   // one column, full width
    [InlineData(800, false)]
    public void TrackedModsInTwoColumns_FollowsTheTrackedColumnWidth(double width, bool expected)
        => Assert.Equal(expected, UpdatesLayoutPolicy.TrackedModsInTwoColumns(width));
}
