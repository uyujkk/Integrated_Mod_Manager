using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class DashboardLayoutPolicyTests
{
    // A ~1400 DIP window leaves a ~1230 DIP page, which should already use two columns.
    [Theory]
    [InlineData(2400, true)]
    [InlineData(1230, true)]
    [InlineData(1100, true)]
    [InlineData(1099, false)]
    [InlineData(600, false)]
    public void UsesTwoColumns_FromAboutAWindowedWidth(double width, bool expected)
    {
        Assert.Equal(expected, DashboardLayoutPolicy.UsesTwoColumns(width));
    }

    [Theory]
    [InlineData(1230, true)]
    [InlineData(860, true)]
    [InlineData(859, false)]
    public void StripFitsOneRow_WrapsOnlyOnNarrowPages(double width, bool expected)
    {
        Assert.Equal(expected, DashboardLayoutPolicy.StripFitsOneRow(width));
    }
}
