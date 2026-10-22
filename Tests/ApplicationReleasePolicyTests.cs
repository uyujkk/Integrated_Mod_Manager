using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsolatedHotkeyTestBuildDisablesAppUpdates(bool requested)
    {
        Assert.True(ApplicationReleasePolicy.IsPrerelease);
        Assert.False(ApplicationReleasePolicy.ApplicationSelfUpdateEnabled);
        Assert.False(ApplicationReleasePolicy.CanCheckApplicationUpdates(requested));
    }

    [Fact]
    public void TestBuildUsesDistinctVersionAndRetainsPublicReferenceLink()
    {
        Assert.Equal("v4.3.1-hotkeys-test.3", ApplicationReleasePolicy.VersionLabel);
        Assert.Equal(new Version(4, 3, 1, 0), Version.Parse(ApplicationReleasePolicy.FileVersion));
        Assert.Equal("https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest", ApplicationReleasePolicy.ReleasePageUrl);
    }
}
