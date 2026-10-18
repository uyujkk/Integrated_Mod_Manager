using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewCannotEnableStableAppUpdatesEvenFromSavedConfiguration(bool requested)
    {
        Assert.True(ApplicationReleasePolicy.IsPrerelease);
        Assert.False(ApplicationReleasePolicy.ApplicationSelfUpdateEnabled);
        Assert.False(ApplicationReleasePolicy.CanCheckApplicationUpdates(requested));
    }

    [Fact]
    public void PreviewUsesConsistentVersionAndRetainsPublicReferenceLink()
    {
        Assert.Equal("v4.2.0-preview.3", ApplicationReleasePolicy.VersionLabel);
        Assert.Equal(new Version(4, 2, 0, 0), Version.Parse(ApplicationReleasePolicy.FileVersion));
        Assert.Equal("https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest", ApplicationReleasePolicy.ReleasePageUrl);
    }
}
