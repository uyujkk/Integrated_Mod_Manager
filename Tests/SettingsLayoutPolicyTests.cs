using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class SettingsLayoutPolicyTests
{
    [Theory]
    [InlineData(1500, 620, 2)]
    [InlineData(1500, 900, 2)]
    [InlineData(1500, 1039, 2)]
    [InlineData(1080, 1040, 3)]
    [InlineData(1079, 1200, 2)]
    [InlineData(845, 621, 2)]
    [InlineData(760, 621, 2)]
    [InlineData(759, 621, 1)]
    [InlineData(600, 466, 1)]
    public void OverviewNeedsBothWidthAndHeight(double width, double height, int expected)
        => Assert.Equal(expected, SettingsLayoutPolicy.Mode(width, height));

    [Theory]
    [InlineData(621, 88, 130, 375)]
    [InlineData(900, 88, 0, 784)]
    [InlineData(100, 88, 130, 1)]
    public void FocusContentStaysInsideViewport(double viewport, double header, double navigation, double expected)
        => Assert.Equal(expected, SettingsLayoutPolicy.FocusHeight(viewport, header, navigation));
}
