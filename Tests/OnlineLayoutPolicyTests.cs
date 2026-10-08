using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineLayoutPolicyTests
{
    [Theory]
    [InlineData(845, 621, false, 152)]
    [InlineData(600, 466, false, 152)]
    [InlineData(845, 739, false, 152)]
    [InlineData(845, 740, true, 152)]
    [InlineData(1039, 900, true, 152)]
    [InlineData(1040, 900, false, 188)]
    [InlineData(1500, 620, false, 188)]
    public void ShortWindowsKeepVerticalSpaceForResults(double width, double height, bool horizontal, double columnWidth)
        => Assert.Equal(new OnlineCharacterRailLayout(horizontal, columnWidth), OnlineLayoutPolicy.CharacterRail(width, height));
}
