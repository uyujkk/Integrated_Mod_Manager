using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineCharacterCatalogMatcherTests
{
    [Theory]
    [InlineData("Hu Tao", "hutao")]
    [InlineData("胡桃·Hu-Tao!", "胡桃hutao")]
    [InlineData("", "")]
    public void NormalizeLookup_KeepsOnlyLowercaseLettersAndDigits(string value, string expected)
    {
        Assert.Equal(expected, OnlineCharacterCatalogMatcher.NormalizeLookup(value));
    }

    [Theory]
    [InlineData("Raiden Shogun outfit", "Raiden", true)]
    [InlineData("Raidenshogun outfit", "Raiden", false)]
    [InlineData("Hu   Tao skin", "Hu Tao", true)]
    [InlineData("新皮肤 胡桃 v2", "胡桃", true)]
    [InlineData("Outfit", "", false)]
    [InlineData(null, "Ayaka", false)]
    public void ContainsAlias_MatchesWholeLatinWordsAndAnyCjkSubstring(string? text, string alias, bool expected)
    {
        Assert.Equal(expected, OnlineCharacterCatalogMatcher.ContainsAlias(text, alias));
    }

    [Fact]
    public void ParseWikiAliases_SplitsOnSeparatorsAndRemovesDuplicates()
    {
        Assert.Equal(["胡桃", "Hu Tao", "往生堂"], OnlineCharacterCatalogMatcher.ParseWikiAliases("胡桃（Hu Tao）", "往生堂, hu tao；"));
    }

    [Fact]
    public void MappingKey_PrefersTheWikiUrlThenTheNormalizedChineseName()
    {
        Assert.Equal("https://wiki/a", OnlineCharacterCatalogMatcher.MappingKey(new OnlineCharacterOption { WikiUrl = " HTTPS://Wiki/A ", DisplayNameZh = "甲" }));
        Assert.Equal("胡桃", OnlineCharacterCatalogMatcher.MappingKey(new OnlineCharacterOption { DisplayNameZh = "胡 桃" }));
    }

    [Fact]
    public void NameSimilarity_ScoresEqualContainedAndEditedNames()
    {
        Assert.Equal(1, OnlineCharacterCatalogMatcher.NameSimilarity("Hu Tao", "hutao"));
        Assert.Equal(0, OnlineCharacterCatalogMatcher.NameSimilarity("!!", "Hu Tao"));
        Assert.Equal(0.78 + 0.2 * 6 / 12d, OnlineCharacterCatalogMatcher.NameSimilarity("Raiden", "Raiden Shogun"), 6);
        Assert.Equal(0.8, OnlineCharacterCatalogMatcher.NameSimilarity("Ayaka", "Ayeka"), 6);
    }

    [Fact]
    public void MatchScore_UsesTheBestPairOfAnyNames()
    {
        var wiki = new OnlineCharacterOption { Key = "w", DisplayNameZh = "雷电将军", DisplayNameEn = "Raiden Shogun", Aliases = ["Ei"] };
        var category = new OnlineCharacterOption { Key = "Raiden", DisplayNameZh = "Raiden", DisplayNameEn = "Raiden", Aliases = ["Raiden Shogun"] };

        Assert.Equal(1, OnlineCharacterCatalogMatcher.MatchScore(wiki, category));
    }

    [Fact]
    public void Merge_AppliesManualMappingsConfidentMatchesAndAppendsTheRest()
    {
        var hutao = Wiki("胡桃", "Hu Tao", "https://wiki/hutao");
        var ayaka = Wiki("神里绫华", "Kamisato Ayaka", "https://wiki/ayaka");
        var raiden = Wiki("雷电将军", "Raiden Shogun", "https://wiki/raiden");
        var gbHutao = Category("Hu Tao", "101", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var gbAyaka = Category("Ayaka", "102");
        var gbManual = Category("Shogun", "103");
        var gbOther = Category("Nahida", "104");

        List<OnlineCharacterOption> merged = OnlineCharacterCatalogMatcher.Merge(
            [hutao, ayaka, raiden],
            [gbHutao, gbAyaka, gbManual, gbOther],
            new Dictionary<string, string> { ["https://wiki/raiden"] = "103" });

        Assert.Equal(("101", "102", "103"), (hutao.CategoryId, ayaka.CategoryId, raiden.CategoryId));
        Assert.Contains("Shogun", raiden.Aliases);
        Assert.Equal(gbHutao.LatestModUpdatedAt, hutao.LatestModUpdatedAt);
        Assert.Equal(["胡桃", "神里绫华", "雷电将军", "Nahida"], merged.Select(character => character.DisplayNameZh));
        Assert.Equal(3, merged[3].WikiOrder);
    }

    [Fact]
    public void Merge_LeavesAmbiguousOrWeakMatchesUnassigned()
    {
        // Both candidates are one edit away (0.83): close enough, but neither stands out.
        var wiki = Wiki("枫原万叶", "Kazuha", "https://wiki/kazuha");
        var aether = Category("Kazuka", "201");
        var lumine = Category("Kazuna", "202");

        List<OnlineCharacterOption> merged = OnlineCharacterCatalogMatcher.Merge([wiki], [aether, lumine], new Dictionary<string, string>());

        Assert.Equal(string.Empty, wiki.CategoryId);
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Merge_ManualMappingToAMissingCategoryStillSetsTheId()
    {
        var wiki = Wiki("胡桃", "Hu Tao", "https://wiki/hutao");

        OnlineCharacterCatalogMatcher.Merge([wiki], [Category("Hu Tao", "101")], new Dictionary<string, string> { ["https://wiki/hutao"] = " 999 " });

        Assert.Equal("999", wiki.CategoryId);
    }

    [Fact]
    public void Merge_WithoutWikiCharactersReturnsTheCategories()
    {
        List<OnlineCharacterOption> categories = [Category("A", "1")];

        Assert.Same(categories, OnlineCharacterCatalogMatcher.Merge([], categories, new Dictionary<string, string>()));
    }

    [Fact]
    public void ApplyManualMappings_ChangesOnlyWikiCharacters()
    {
        var wiki = Wiki("胡桃", "Hu Tao", "https://wiki/hutao");
        var category = Category("Hu Tao", "101");
        category.WikiUrl = "https://wiki/hutao";

        OnlineCharacterCatalogMatcher.ApplyManualMappings(new Dictionary<string, string> { ["https://wiki/hutao"] = "555" }, [wiki, category]);

        Assert.Equal(("555", "101"), (wiki.CategoryId, category.CategoryId));
    }

    [Fact]
    public void SortByWikiOrderThenNewest_OrdersByWikiOrderDateAndName()
    {
        var old = new OnlineCharacterOption { DisplayNameEn = "B", LatestModUpdatedAt = DateTimeOffset.UnixEpoch };
        var recent = new OnlineCharacterOption { DisplayNameEn = "C", LatestModUpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1) };
        var sameDate = new OnlineCharacterOption { DisplayNameEn = "A", LatestModUpdatedAt = DateTimeOffset.UnixEpoch };
        var first = new OnlineCharacterOption { DisplayNameEn = "Z", WikiOrder = 0 };
        List<OnlineCharacterOption> characters = [old, recent, sameDate, first];

        OnlineCharacterCatalogMatcher.SortByWikiOrderThenNewest(characters);

        Assert.Equal(["Z", "C", "A", "B"], characters.Select(character => character.DisplayNameEn));
    }

    [Fact]
    public void WithFallback_AddsOnlyCharactersTheCatalogLacks()
    {
        var official = new OnlineCharacterOption { Key = "Laevatain", DisplayNameZh = "莱万汀", DisplayNameEn = "Laevatain", AvatarUrl = "https://new/laev.png" };
        var newcomer = new OnlineCharacterOption { Key = "Newcomer", DisplayNameZh = "新干员", DisplayNameEn = "Newcomer" };
        var builtInLaev = new OnlineCharacterOption { Key = "Laev", DisplayNameZh = "莱万汀", DisplayNameEn = "Laevatain", AvatarUrl = "https://old/laev.png" };
        var builtInOnly = new OnlineCharacterOption { Key = "Ember", DisplayNameZh = "余烬", DisplayNameEn = "Ember", Aliases = ["Ember"] };

        List<OnlineCharacterOption> merged = OnlineCharacterCatalogMatcher.WithFallback([official, newcomer], [builtInLaev, builtInOnly]);

        Assert.Equal([official, newcomer, builtInOnly], merged);
        Assert.Equal([builtInOnly], OnlineCharacterCatalogMatcher.WithFallback([], [builtInOnly]));
    }

    private static OnlineCharacterOption Wiki(string zh, string en, string url) => new()
    {
        Key = en, DisplayNameZh = zh, DisplayNameEn = en, WikiUrl = url, IsWikiCharacter = true, Aliases = [zh, en]
    };

    private static OnlineCharacterOption Category(string name, string id, DateTimeOffset? updated = null) => new()
    {
        Key = name, DisplayNameZh = name, DisplayNameEn = name, CategoryId = id, Aliases = [name],
        LatestModUpdatedAt = updated ?? default
    };
}
