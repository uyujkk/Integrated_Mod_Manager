using IntegratedModManager.Core.Updates;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewDisablesAppUpdates(bool requested)
    {
        Assert.True(ApplicationReleasePolicy.IsPrerelease);
        Assert.False(ApplicationReleasePolicy.ApplicationSelfUpdateEnabled);
        Assert.False(ApplicationReleasePolicy.CanCheckApplicationUpdates(requested));
    }

    [Fact]
    public void PreviewUsesConsistentVersionAndRetainsPublicReferenceLink()
    {
        Assert.Equal("v4.5.2-preview.1", ApplicationReleasePolicy.VersionLabel);
        Assert.Equal(new Version(4, 5, 2, 0), Version.Parse(ApplicationReleasePolicy.FileVersion));
        Assert.Equal("https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest", ApplicationReleasePolicy.ReleasePageUrl);
    }
}
