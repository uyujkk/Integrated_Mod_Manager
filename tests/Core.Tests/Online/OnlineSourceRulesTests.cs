using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

// The rules the Online page used before stage J finish, step 3, unchanged.
public sealed class OnlineSourceRulesTests
{
    [Theory]
    [InlineData(null, "GameBanana")]
    [InlineData("", "GameBanana")]
    [InlineData("   ", "GameBanana")]
    [InlineData(" gamebanana.com ", "gamebanana.com")]
    [InlineData("Other", "Other")]
    public void TheSourceDefaultsToGameBanana(string? site, string expected)
        => Assert.Equal(expected, OnlineSourceRules.Source(site));

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData(" 42770 ", "42770")]
    [InlineData("Genshin Impact", "Genshin Impact")]
    public void GameNamesAndCategoriesAreTrimmed(string? value, string expected)
        => Assert.Equal(expected, OnlineSourceRules.Trimmed(value));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("GameBanana", true)]
    [InlineData("https://GAMEBANANA.com/mods", true)]
    [InlineData("Nexus", false)]
    public void OnlyGameBananaIsSupported(string? source, bool supported)
        => Assert.Equal(supported, OnlineSourceRules.IsGameBanana(source));

    [Theory]
    [InlineData("42770", "", true)]
    [InlineData("18140", "Arknights: Endfield", true)]
    [InlineData("18140", "明日方舟：终末地", true)]
    [InlineData("18140", "Genshin Impact", false)]
    [InlineData("", "", false)]
    public void EndfieldIsKnownByCategoryOrName(string categoryId, string gameName, bool endfield)
        => Assert.Equal(endfield, OnlineSourceRules.IsEndfield(categoryId, gameName));

    [Theory]
    [InlineData("42770", true)]
    [InlineData("90001", true)]
    [InlineData("90002", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EndfieldCategoriesIncludeEachOperators(string? categoryId, bool endfield)
        => Assert.Equal(endfield, OnlineSourceRules.IsEndfieldCategory(categoryId, ["90001", null]));

    [Fact]
    public void TheDefaultsAreTheBuiltInRepositorys()
    {
        Assert.Equal("GameBanana", OnlineSourceRules.DefaultSourceSite);
        Assert.Equal("42770", OnlineSourceRules.DefaultCategoryId);
        Assert.Equal("https://wiki.skland.com/endfield", OnlineSourceRules.EndfieldOfficialWikiUrl);
    }
}
