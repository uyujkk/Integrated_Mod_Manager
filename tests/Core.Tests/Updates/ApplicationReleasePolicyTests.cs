using IntegratedModManager.Core.Updates;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewNeverChecksForAppUpdates(bool requested)
    {
        Assert.True(ApplicationReleasePolicy.IsPrerelease);
        Assert.False(ApplicationReleasePolicy.ApplicationSelfUpdateEnabled);
        Assert.False(ApplicationReleasePolicy.CanCheckApplicationUpdates(requested));
        // A preview keeps what it loaded, so a later stable build still has the user's choice.
        Assert.Equal((bool?)requested, ApplicationReleasePolicy.UpdateSettingToSave<bool?>(!requested, requested));
    }

    [Fact]
    public void PreviewUsesConsistentVersionAndRetainsPublicReferenceLink()
    {
        Assert.Equal("v4.7.0-preview.1", ApplicationReleasePolicy.VersionLabel);
        Assert.Equal(new Version(4, 7, 0, 0), Version.Parse(ApplicationReleasePolicy.FileVersion));
        Assert.Equal("https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest", ApplicationReleasePolicy.ReleasePageUrl);
    }

    [Fact]
    public void BuildsWithoutSelfUpdateKeepTheLoadedUpdateSettings()
    {
        Assert.Equal("Daily", ApplicationReleasePolicy.UpdateSettingToSave("Manual", "Daily", selfUpdateEnabled: false));
        Assert.Equal((bool?)true, ApplicationReleasePolicy.UpdateSettingToSave<bool?>(false, true, selfUpdateEnabled: false));
        Assert.Null(ApplicationReleasePolicy.UpdateSettingToSave<bool?>(false, null, selfUpdateEnabled: false));
    }

    [Fact]
    public void StableSavesTheCurrentUpdateSettings()
    {
        Assert.Equal("Manual", ApplicationReleasePolicy.UpdateSettingToSave("Manual", "Daily", selfUpdateEnabled: true));
        Assert.Equal((bool?)false, ApplicationReleasePolicy.UpdateSettingToSave<bool?>(false, true, selfUpdateEnabled: true));
    }
}
