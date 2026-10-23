using IntegratedModManager.Core.Endfield;

namespace IntegratedModManager.Core.Tests;

public sealed class EndfieldCharacterCatalogTests
{
    [Fact]
    public void BuiltIn_LoadsTheEmbeddedListInWikiOrder()
    {
        IReadOnlyList<EndfieldCharacterInfo> characters = EndfieldCharacterCatalog.BuiltIn;

        Assert.Equal(35, characters.Count);
        Assert.Equal(("Si", "祀", "50297"), (characters[0].EnglishName, characters[0].ChineseName, characters[0].GameBananaCategoryId));
        Assert.Equal("Antal", characters[^1].EnglishName);
        EndfieldCharacterInfo fangyi = Assert.Single(characters, character => character.EnglishName == "Zhuang Fangyi");
        Assert.Equal(["Zhuang Fangyi", "庄方宜", "Fangyi", "ZFY", "庄方怡"], fangyi.AllNames);
        Assert.Equal("44984", fangyi.GameBananaCategoryId);
        Assert.All(characters, character => Assert.StartsWith("https://", character.AvatarUrl));
    }

    [Fact]
    public void Parse_TrimsValuesAndTreatsMissingOptionalFieldsAsEmpty()
    {
        EndfieldCharacterInfo character = Assert.Single(EndfieldCharacterCatalog.Parse(
            """[{ "englishName": " Test ", "chineseName": "测试", "gameBananaCategoryId": null, "aliases": [" T "] }]"""));

        Assert.Equal(("Test", "测试", "", ""), (character.EnglishName, character.ChineseName, character.GameBananaCategoryId, character.AvatarUrl));
        Assert.Equal(["Test", "测试", "T"], character.AllNames);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""[1]""")]
    [InlineData("""[{ "chineseName": "甲" }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": " " }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": "甲", "gameBananaCategoryId": "12a" }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": "甲", "gameBananaCategoryId": 12 }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": "甲", "avatarUrl": "http://example.com/a.png" }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": "甲", "avatarUrl": "a.png" }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": "甲", "aliases": "B" }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": "甲", "aliases": [""] }]""")]
    [InlineData("""[{ "englishName": "A", "chineseName": "甲" }, { "englishName": "a", "chineseName": "乙" }]""")]
    public void Parse_RejectsMalformedEntries(string json)
    {
        Assert.Throws<InvalidDataException>(() => EndfieldCharacterCatalog.Parse(json));
    }
}
