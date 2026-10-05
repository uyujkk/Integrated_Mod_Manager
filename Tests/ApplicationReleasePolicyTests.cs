using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StableAppChecksRespectSavedConfiguration(bool requested)
    {
        Assert.False(ApplicationReleasePolicy.IsPrerelease);
        Assert.True(ApplicationReleasePolicy.ApplicationSelfUpdateEnabled);
        Assert.Equal(requested, ApplicationReleasePolicy.CanCheckApplicationUpdates(requested));
    }

    [Fact]
    public void StableUsesReleaseChannelAndConsistentVersion()
    {
        Assert.Equal("v4.0.0", ApplicationReleasePolicy.VersionLabel);
        Assert.Equal(new Version(4, 0, 0, 0), Version.Parse(ApplicationReleasePolicy.FileVersion));
        Assert.Equal("https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest", ApplicationReleasePolicy.ReleasePageUrl);
    }
}
