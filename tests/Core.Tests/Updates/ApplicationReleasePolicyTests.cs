using IntegratedModManager.Core.Updates;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StableAppUpdatesHonorSavedConfiguration(bool requested)
    {
        Assert.False(ApplicationReleasePolicy.IsPrerelease);
        Assert.True(ApplicationReleasePolicy.ApplicationSelfUpdateEnabled);
        Assert.Equal(requested, ApplicationReleasePolicy.CanCheckApplicationUpdates(requested));
    }

    [Fact]
    public void StableUsesConsistentVersionAndRetainsPublicReferenceLink()
    {
        Assert.Equal("v4.6.1", ApplicationReleasePolicy.VersionLabel);
        Assert.Equal(new Version(4, 6, 1, 0), Version.Parse(ApplicationReleasePolicy.FileVersion));
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
        Assert.Equal((bool?)false, ApplicationReleasePolicy.UpdateSettingToSave<bool?>(false, true));
    }
}
