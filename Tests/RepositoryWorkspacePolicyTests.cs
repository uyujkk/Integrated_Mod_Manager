using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class RepositoryWorkspacePolicyTests
{
    [Theory]
    [InlineData(773, 290, true)]
    [InlineData(773, 1000, false)]
    [InlineData(719, 290, false)]
    [InlineData(780, 1000, true)]
    [InlineData(double.NaN, 290, false)]
    public void ShortViewportsKeepRecoveryBesideThePresetBrowser(double width, double height, bool expected)
        => Assert.Equal(expected, RepositoryWorkspacePolicy.InlineCombinationRecovery(width, height));

    [Theory]
    [InlineData(314, 120)]
    [InlineData(400, 140)]
    [InlineData(580, 203)]
    [InlineData(1000, 220)]
    [InlineData(double.NaN, 120)]
    [InlineData(double.PositiveInfinity, 120)]
    [InlineData(-1, 120)]
    public void StackedRecoveryReservesRoomForCommandsAndBrowsing(double height, double expected)
        => Assert.Equal(expected, RepositoryWorkspacePolicy.StackedRecoveryHeight(height), 4);

    [Theory]
    [InlineData(320, false)]
    [InlineData(760, false)]
    [InlineData(979, false)]
    [InlineData(980, true)]
    [InlineData(1440, true)]
    [InlineData(3440, true)]
    public void DetailsFitWithoutShrinkingTheCategoryColumn(double width, bool inline)
    {
        var layout = RepositoryWorkspacePolicy.Layout(width);
        Assert.Equal(inline, layout.InlineDetails);
        Assert.InRange(layout.CategoryWidth, 156, 208);
        Assert.InRange(layout.CoverColumns, 1, 8);
        Assert.True(layout.CoverWidth >= 140);
        if (inline) Assert.True(width - layout.CategoryWidth - layout.DetailWidth - 24 >= 240);
        else Assert.Equal(0, layout.DetailWidth);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void InvalidMeasurementsUseSafeSmallWindowLayout(double width)
    {
        var layout = RepositoryWorkspacePolicy.Layout(width);
        Assert.False(layout.InlineDetails);
        Assert.Equal(1, layout.CoverColumns);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("CYBER", true)]
    [InlineData("提弗 Cyber", true)]
    [InlineData("cyber missing", false)]
    public void SearchMatchesAllTermsAcrossNameAndCategory(string? query, bool matches)
        => Assert.Equal(matches, RepositoryWorkspacePolicy.MatchesQuery("Typhoeus Cyber Sarkaz", "提弗洛斯", query));

    [Fact]
    public void PresentationChangesPreserveSelectionByPathRatherThanIndex()
    {
        string[] paths = [@"G:\Mods\A", @"G:\Mods\B"];
        Assert.Equal(paths[1], RepositoryWorkspacePolicy.PreserveSelection(paths.Reverse(), @"g:\mods\b"));
        Assert.Equal(paths[0], RepositoryWorkspacePolicy.PreserveSelection(paths, @"G:\Mods\missing"));
        Assert.Null(RepositoryWorkspacePolicy.PreserveSelection([], @"G:\Mods\B"));
    }
}
