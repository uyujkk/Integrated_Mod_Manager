using IntegratedModManager.Core.Configuration;

namespace IntegratedModManager.Core.Tests;

public sealed class WhatsNewPolicyTests
{
    [Fact]
    public void FirstInstall_DoesNotShowWhatsNew()
        => Assert.False(WhatsNewPolicy.ShouldShow(WhatsNewPolicy.SeenAfterLoad(null, "4.7.0", firstInstall: true), "4.7.0"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4.6.0")]
    public void Upgrade_ShowsWhatsNewOnce(string? saved)
    {
        string? seen = WhatsNewPolicy.SeenAfterLoad(saved, "4.7.0", firstInstall: false);
        Assert.True(WhatsNewPolicy.ShouldShow(seen, "4.7.0"));
        Assert.False(WhatsNewPolicy.ShouldShow("4.7.0", "4.7.0"));
    }

    [Fact]
    public void UpgradeFrom47_ShowsThe48SetOnce()
    {
        string? seen = WhatsNewPolicy.SeenAfterLoad("4.7.0", "4.8.0", firstInstall: false);
        Assert.True(WhatsNewPolicy.ShouldShow(seen, "4.8.0"));
        Assert.False(WhatsNewPolicy.ShouldShow("4.8.0", "4.8.0"));
    }

    [Fact]
    public void Upgrade_KeepsWhatWasSeen()
        => Assert.Equal("4.7.0", WhatsNewPolicy.SeenAfterLoad("4.7.0", "4.7.0", firstInstall: false));
}
