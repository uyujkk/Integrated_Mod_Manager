using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class DashboardLayoutPolicyTests
{
    [Theory]
    [InlineData(2400, 2)]
    [InlineData(1460, 2)]
    [InlineData(1459, 1)]
    [InlineData(860, 1)]
    [InlineData(859, 0)]
    [InlineData(400, 0)]
    public void Mode_SwitchesAtThePageWidthBreakpoints(double width, int expected)
    {
        Assert.Equal(expected, DashboardLayoutPolicy.Mode(width));
    }

    [Theory]
    [InlineData(619, 1)]
    [InlineData(620, 3)]
    [InlineData(1800, 3)]
    public void MetricColumns_StackOnlyOnVeryNarrowPages(double width, int expected)
    {
        Assert.Equal(expected, DashboardLayoutPolicy.MetricColumns(width));
    }

    [Theory]
    [InlineData(2000, 1)]
    [InlineData(1200, 3)]
    [InlineData(700, 2)]
    [InlineData(679, 1)]
    public void PresetColumns_FollowTheMode(double width, int expected)
    {
        Assert.Equal(expected, DashboardLayoutPolicy.PresetColumns(width));
    }
}
