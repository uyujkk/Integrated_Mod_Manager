using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineInstallationDetectionPolicyTests
{
    [Fact]
    public void Matches_PrefersStableItemId()
    {
        Assert.True(OnlineInstallationDetectionPolicy.Matches(
            675282,
            "https://gamebanana.com/mods/old-address",
            675282,
            "https://gamebanana.com/mods/new-address"));
    }

    [Fact]
    public void Matches_NormalizesProfileUrlWhenItemIdIsUnavailable()
    {
        Assert.True(OnlineInstallationDetectionPolicy.Matches(
            0,
            " HTTPS://GAMEBANANA.COM/MODS/675282/ ",
            0,
            "https://gamebanana.com/mods/675282"));
    }

    [Fact]
    public void Matches_RejectsDifferentMods()
    {
        Assert.False(OnlineInstallationDetectionPolicy.Matches(
            675282,
            "https://gamebanana.com/mods/675282",
            675283,
            "https://gamebanana.com/mods/675283"));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(0, "  ")]
    public void BuildIdentity_ReturnsEmptyWhenNoStableIdentityExists(int itemId, string? profileUrl)
    {
        Assert.Equal(string.Empty, OnlineInstallationDetectionPolicy.BuildIdentity(itemId, profileUrl));
    }
}
