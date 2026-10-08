using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

public sealed class LocalizationBoundaryTests
{
    [Theory]
    [InlineData("\n参数：16", "Values: 16", "\n参数：16")]
    [InlineData("已完成\n", "Complete\n", "已完成\n")]
    [InlineData("\r\n备份路径\r\n", "Backup path", "\r\n备份路径\r\n")]
    [InlineData(" 参数备份： ", "State backup: ", " 参数备份： ")]
    [InlineData("\n设置\n", "Settings", "\n设置\n")]
    [InlineData("参数\n说明", "Values\nHelp", "参数\n说明")]
    public void ChineseFragmentsKeepSeparators(string zh, string en, string expected)
        => Assert.Equal(expected, BetaLocalization.Resolve(false, zh, en));

    [Fact]
    public void EnglishAndEmptyFallbackRemainUnchanged()
    {
        Assert.Equal("\nValues\n", BetaLocalization.Resolve(true, "参数", "\nValues\n"));
        Assert.Equal("Fallback", BetaLocalization.Resolve(false, "  ", "Fallback"));
        Assert.Equal("参数", BetaLocalization.NormalizeChineseText("  参数  "));
    }
}
