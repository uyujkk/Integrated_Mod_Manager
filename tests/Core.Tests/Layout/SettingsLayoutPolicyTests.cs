using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class SettingsLayoutPolicyTests
{
    [Theory]
    [InlineData(2400, 3)]
    [InlineData(1230, 3)]
    [InlineData(1080, 3)]
    [InlineData(1079, 2)]
    [InlineData(760, 2)]
    [InlineData(759, 1)]
    [InlineData(600, 1)]
    public void OverviewNeedsOnlyWidth(double width, int expected)
        => Assert.Equal(expected, SettingsLayoutPolicy.Mode(width));

    [Theory]
    [InlineData(621, 88, 130, 375)]
    [InlineData(900, 88, 0, 784)]
    [InlineData(100, 88, 130, 1)]
    public void FocusContentStaysInsideViewport(double viewport, double header, double navigation, double expected)
        => Assert.Equal(expected, SettingsLayoutPolicy.FocusHeight(viewport, header, navigation));
}
