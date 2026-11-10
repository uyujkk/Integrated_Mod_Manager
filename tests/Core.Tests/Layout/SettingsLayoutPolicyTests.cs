using IntegratedModManager.Core.Layout;

namespace IntegratedModManager.Core.Tests;

public sealed class SettingsLayoutPolicyTests
{
    [Fact]
    public void AMaximizedWindowMovesTheGroupThatEvensTheColumnsOut()
    {
        // Measured on a maximized window: main 600, updates 160; online 190, diagnostics 300, project 80.
        bool[] moves = SettingsLayoutPolicy.BalanceColumns(600, 160, [190, 300, 80], spacing: 4);
        Assert.Equal([false, false, true], moves);
    }

    [Fact]
    public void BalancedColumnsStayAsTheyAre()
        => Assert.Equal([false, false], SettingsLayoutPolicy.BalanceColumns(500, 300, [100, 100]));

    [Fact]
    public void ATallerSideColumnCanGiveSeveralGroups()
        => Assert.Equal([true, true, false, false], SettingsLayoutPolicy.BalanceColumns(0, 0, [100, 100, 100, 100]));

    [Fact]
    public void OnATieTheFewerMovesWin()
        => Assert.Equal([true, false, false], SettingsLayoutPolicy.BalanceColumns(0, 0, [100, 50, 50]));

    [Fact]
    public void NoMovableGroupsMeansNoMoves()
        => Assert.Empty(SettingsLayoutPolicy.BalanceColumns(100, 400, []));
}
