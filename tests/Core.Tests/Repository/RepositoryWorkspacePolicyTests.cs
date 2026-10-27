using IntegratedModManager.Core.Repository;

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
        Assert.InRange(layout.CategoryWidth, 176, 220);
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
        Assert.Equal(paths[1], RepositoryWorkspacePolicy.PreserveSelection(paths.AsEnumerable().Reverse(), @"g:\mods\b"));
        Assert.Equal(paths[0], RepositoryWorkspacePolicy.PreserveSelection(paths, @"G:\Mods\missing"));
        Assert.Null(RepositoryWorkspacePolicy.PreserveSelection([], @"G:\Mods\B"));
    }

    [Theory]
    [InlineData(500, 600, 476, 1, 236, 240, 140)]
    [InlineData(1000, 600, 976, 4, 236, 240, 140)]
    [InlineData(3000, 600, 2976, 8, 236, 240, 140)]
    [InlineData(200, 120, 176, 1, 176, 140, 54)]
    [InlineData(100, 200, 140, 1, 140, 192, 106)]
    public void CoverGrid_FillsRowsAndFollowsTheViewerHeight(double width, double height,
        double gallery, int columns, double minWidth, double itemHeight, double preview)
        => Assert.Equal(new RepositoryCoverGrid(gallery, columns, minWidth, itemHeight, preview),
            RepositoryWorkspacePolicy.CoverGrid(width, height));

    [Fact]
    public void CoverGrid_ToleratesAnUnmeasuredViewer()
        => Assert.Equal(new RepositoryCoverGrid(140, 1, 140, 140, 54), RepositoryWorkspacePolicy.CoverGrid(double.NaN, double.NaN));

    [Theory]
    [InlineData(5, 2, true, ModListEmptyKind.None)]
    [InlineData(5, 0, true, ModListEmptyKind.NoMatches)]
    [InlineData(5, 0, false, ModListEmptyKind.NoMatches)]
    [InlineData(0, 0, true, ModListEmptyKind.NoMods)]
    [InlineData(0, 0, false, ModListEmptyKind.NoCharacter)]
    public void EmptyKind_SaysWhyTheListIsEmpty(int total, int visible, bool hasCharacter, ModListEmptyKind expected)
        => Assert.Equal(expected, RepositoryWorkspacePolicy.EmptyKind(total, visible, hasCharacter));

    [Theory]
    [InlineData(null, "Ann|bob|Carol")]
    [InlineData("  ", "Ann|bob|Carol")]
    [InlineData("o", "bob|Carol")]
    [InlineData(" B ", "bob")]
    [InlineData("zz", "")]
    public void FilterByName_KeepsOrderAndIgnoresCaseAndSpaces(string? keyword, string expected)
        => Assert.Equal(expected, string.Join('|', RepositoryWorkspacePolicy.FilterByName(new[] { "Ann", "bob", "Carol" }, name => name, keyword)));

    [Theory]
    [InlineData("已复制", ModStateTone.Deployed)]
    [InlineData("已链接", ModStateTone.Deployed)]
    [InlineData("未部署", ModStateTone.NotDeployed)]
    [InlineData("未选择", ModStateTone.Neutral)]
    [InlineData("", ModStateTone.Neutral)]
    [InlineData(null, ModStateTone.Neutral)]
    public void StateTone_ReadsTheCurrentLanguagesLabels(string? state, ModStateTone expected)
        => Assert.Equal(expected, RepositoryWorkspacePolicy.StateTone(state, "未部署", "已复制", "已链接"));

    [Fact]
    public void StateTone_IgnoresCase()
        => Assert.Equal(ModStateTone.Deployed, RepositoryWorkspacePolicy.StateTone("LINKED", "Not deployed", "Copied", "Linked"));

    [Theory]
    [InlineData(@"C:\Repo", @"C:\Repo\Ann\Outfit", @"…\Ann\Outfit")]
    [InlineData(@"C:\Repo\", @"C:\Repo\Ann\Outfit", @"…\Ann\Outfit")]
    [InlineData(@"C:\Repo", @"D:\Other\Outfit", @"D:\Other\Outfit")]
    [InlineData(@"C:\Repo", @"C:\Elsewhere\Outfit", @"C:\Elsewhere\Outfit")]
    [InlineData("  ", @"C:\Repo\Ann\Outfit", @"C:\Repo\Ann\Outfit")]
    [InlineData(null, @"C:\Repo\Ann\Outfit", @"C:\Repo\Ann\Outfit")]
    public void DisplayPath_ShortensOnlyPathsInsideTheRepository(string? root, string path, string expected)
        => Assert.Equal(expected, RepositoryWorkspacePolicy.DisplayPath(root, path));

    [Theory]
    [InlineData(ModStateTone.Deployed, ModStateFilter.All, true)]
    [InlineData(ModStateTone.NotDeployed, ModStateFilter.All, true)]
    [InlineData(ModStateTone.Deployed, ModStateFilter.Deployed, true)]
    [InlineData(ModStateTone.NotDeployed, ModStateFilter.Deployed, false)]
    [InlineData(ModStateTone.Neutral, ModStateFilter.Deployed, false)]
    [InlineData(ModStateTone.Deployed, ModStateFilter.NotDeployed, false)]
    [InlineData(ModStateTone.NotDeployed, ModStateFilter.NotDeployed, true)]
    [InlineData(ModStateTone.Neutral, ModStateFilter.NotDeployed, true)]
    public void MatchesStateFilter_SplitsDeployedFromTheRest(ModStateTone tone, ModStateFilter filter, bool expected)
        => Assert.Equal(expected, RepositoryWorkspacePolicy.MatchesStateFilter(tone, filter));

    [Fact]
    public void ReplacedByDeploy_NamesTheOtherDeployedMod()
        => Assert.Equal("Outfit-Casual", RepositoryWorkspacePolicy.ReplacedByDeploy(
            [("Outfit-Casual", ModStateTone.Deployed), ("Weapon-Glow", ModStateTone.NotDeployed)], "Weapon-Glow"));

    [Fact]
    public void ReplacedByDeploy_IgnoresTheModItself()
        => Assert.Null(RepositoryWorkspacePolicy.ReplacedByDeploy(
            [("Outfit-Casual", ModStateTone.Deployed), ("Weapon-Glow", ModStateTone.NotDeployed)], "outfit-casual"));

    [Fact]
    public void ReplacedByDeploy_IsNullWhenNothingIsDeployed()
        => Assert.Null(RepositoryWorkspacePolicy.ReplacedByDeploy([("Hair-Pack", ModStateTone.NotDeployed)], "Other"));

    [Theory]
    [InlineData(false, false, false, 0, false)]
    [InlineData(true, true, false, 2, false)]
    [InlineData(true, true, true, 3, true)]
    public void RepositorySetup_CountsTheDoneSteps(bool game, bool folder, bool target, int done, bool complete)
    {
        var setup = new RepositorySetup(game, folder, target);
        Assert.Equal(done, setup.Done);
        Assert.Equal(complete, setup.IsComplete);
    }
}
