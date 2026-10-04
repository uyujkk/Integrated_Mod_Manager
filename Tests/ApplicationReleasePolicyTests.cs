using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BetaCannotEnableAppUpdatesFromSavedConfiguration(bool requested)
    {
        Assert.True(ApplicationReleasePolicy.IsPrerelease);
        Assert.False(ApplicationReleasePolicy.ApplicationSelfUpdateEnabled);
        Assert.False(ApplicationReleasePolicy.CanCheckApplicationUpdates(requested));
    }

    [Fact]
    public void BetaUsesExplicitManualReleasePageAndSeparateVersion()
    {
        Assert.Equal("v4.0-beta", ApplicationReleasePolicy.VersionLabel);
        Assert.Equal(new Version(4, 0, 0, 0), Version.Parse(ApplicationReleasePolicy.FileVersion));
        Assert.Equal("https://github.com/uyujkk/Integrated_Mod_Manager/releases/tag/v4.0-beta", ApplicationReleasePolicy.ReleasePageUrl);
    }
}
