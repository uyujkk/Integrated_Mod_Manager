using IntegratedModManager.Core.Shortcuts;

namespace IntegratedModManager.Core.Tests.Shortcuts;

public sealed class LegacyShortcutDescriptionTests
{
    [Theory]
    [InlineData("循环切换 $hair", true)]
    [InlineData("按住触发 $glow", true)]
    [InlineData("切换 $coat", true)]
    [InlineData("  触发 $wave", true)]
    [InlineData("Cycle $hair", true)]
    [InlineData("hold $glow", true)]
    [InlineData("TOGGLE $coat", true)]
    [InlineData("Trigger $wave", true)]
    [InlineData("Cycles the hair", false)]
    [InlineData("切换发型", false)]
    [InlineData("Change hair", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsPossibleLegacyDescription_MatchesOnlyTheOldAutomaticPrefixes(string? action, bool expected)
        => Assert.Equal(expected, ModShortcutDescriptionFormatter.IsPossibleLegacyDescription(action));
}
