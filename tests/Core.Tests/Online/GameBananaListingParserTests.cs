using System.Text.Json;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class GameBananaListingParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseRecord_ReadsEveryListedField()
    {
        OnlineModCard card = Parse("""
            {
              "_idRow": 123, "_sName": "Skin", "_sProfileUrl": "https://gamebanana.com/mods/123",
              "_aSubmitter": { "_sName": "Author", "_idRow": "45" },
              "_aRootCategory": { "_sName": "Skins" },
              "_aSubCategory": { "_sName": "Hu Tao", "_sProfileUrl": "https://gamebanana.com/mods/cats/9876/" },
              "_aPreviewMedia": { "_aImages": [
                { "_sFile": "missing-base-is-skipped" },
                { "_sBaseUrl": "https://images", "_sFile530": "big.jpg", "_sFile": "orig.jpg" } ] },
              "_tsDateModified": 0, "_tsDateUpdated": "1767225600", "_tsDateAdded": 1,
              "_nLikeCount": 9, "_nViewCount": "1,000", "_nDownloadCount": 99.6
            }
            """)!;

        Assert.Equal((123, "Skin", "https://gamebanana.com/mods/123"), (card.ItemId, card.Title, card.ProfileUrl));
        Assert.Equal(("Author", 45), (card.Author, card.AuthorId));
        Assert.Equal(("Skins", "Hu Tao", "9876"), (card.RootCategoryName, card.CharacterName, card.CategoryId));
        Assert.Equal("https://images/big.jpg", card.PreviewUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1767225600), card.UpdatedAt);
        Assert.Equal((9, 1000, 100), (card.Likes, card.Views, card.Downloads));
        Assert.Equal(GameBananaListingParser.Hotness(9, 1000, 100), card.HotnessScore);
        Assert.Equal("https://gamebanana.com/mods/download/123", card.DownloadUrl);
    }

    [Fact]
    public void ParseRecord_FillsDefaultsForMissingFields()
    {
        OnlineModCard card = Parse("""{ "_idRow": 7 }""")!;

        Assert.Equal(("Mod 7", "https://gamebanana.com/mods/7", "Skins"), (card.Title, card.ProfileUrl, card.RootCategoryName));
        Assert.Equal((string.Empty, string.Empty, string.Empty), (card.Author, card.CharacterName, card.CategoryId));
        Assert.Null(card.PreviewUrl);
        Assert.Equal(Now, card.UpdatedAt);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{ "_idRow": 0 }""")]
    [InlineData("""{ "_sName": "no id" }""")]
    public void ParseRecord_RejectsRecordsWithoutAnId(string json)
    {
        Assert.Null(Parse(json));
    }

    [Fact]
    public void Hotness_IsALogScaleCappedAtTen()
    {
        Assert.Equal(0, GameBananaListingParser.Hotness(0, 0, 0));
        Assert.Equal(Math.Round((Math.Log10(1001) * 0.55 + Math.Log10(11) * 0.3 + Math.Log10(101) * 0.15) * 2, 2), GameBananaListingParser.Hotness(10, 100, 1000));
        Assert.Equal(10, GameBananaListingParser.Hotness(int.MaxValue, int.MaxValue, int.MaxValue));
    }

    [Theory]
    [InlineData("""{ "v": 42 }""", 42)]
    [InlineData("""{ "v": "1,234" }""", 1234)]
    [InlineData("""{ "v": 2.5 }""", 3)]
    [InlineData("""{ "v": "-2.5" }""", -3)]
    [InlineData("""{ "v": 1e30 }""", long.MaxValue)]
    [InlineData("""{ "v": "abc" }""", 0)]
    [InlineData("""{ "v": true }""", 0)]
    [InlineData("""{ "v": null }""", 0)]
    [InlineData("""{ }""", 0)]
    [InlineData("""[1]""", 0)]
    public void Int64_ReadsNumbersAndNumericStrings(string json, long expected)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(expected, GameBananaJson.Int64(document.RootElement, "v"));
    }

    [Fact]
    public void Int32_ClampsAndTextAcceptsScalarsOnly()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "big": 99999999999, "small": -99999999999, "s": "x", "n": 5, "b": true, "o": {}, "flag": "True" }""");
        JsonElement root = document.RootElement;

        Assert.Equal((int.MaxValue, int.MinValue), (GameBananaJson.Int32(root, "big"), GameBananaJson.Int32(root, "small")));
        Assert.Equal(("x", "5", "True"), (GameBananaJson.Text(root, "s"), GameBananaJson.Text(root, "n"), GameBananaJson.Text(root, "b")));
        Assert.Null(GameBananaJson.Text(root, "o"));
        Assert.True(GameBananaJson.Boolean(root, "flag"));
        Assert.False(GameBananaJson.Boolean(root, "s"));
    }

    [Theory]
    [InlineData("https://gamebanana.com/mods/cats/12345", "12345")]
    [InlineData("https://gamebanana.com/mods/cats/12345/", "12345")]
    [InlineData("https://gamebanana.com/mods/cats/", "")]
    [InlineData(null, "")]
    public void TrailingNumericId_ReadsTheLastPathNumber(string? url, string expected)
    {
        Assert.Equal(expected, GameBananaJson.TrailingNumericId(url));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(long.MaxValue)]
    public void UnixTime_UsesTheFallbackForInvalidSeconds(long seconds)
    {
        Assert.Equal(Now, GameBananaJson.UnixTime(seconds, Now));
    }

    private static OnlineModCard? Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return GameBananaListingParser.ParseRecord(document.RootElement, Now);
    }
}
