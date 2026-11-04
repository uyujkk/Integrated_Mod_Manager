using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Online.Tests;

// Each test asks a live site for what the app asks for and checks that the answer still has the shape
// the app's parsers expect. A failure means a site changed or is down, not that the app's code broke.
public sealed class OnlineSourceTests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Client = CreateClient();

    // The built-in games' wiki links and GameBanana categories, as in MainWindow.BuiltInData.cs.
    public static TheoryData<string, string> MiHoYoWikis => new()
    {
        { "Zenless Zone Zero", "https://baike.mihoyo.com/zzz/wiki/channel/map/2/43" },
        { "Genshin Impact", "https://baike.mihoyo.com/ys/obc/channel/map/189/25?bbs_presentation_style=no_header&visit_device=pc" },
        { "Honkai: Star Rail", "https://bbs.mihoyo.com/sr/wiki/channel/map/17/18?bbs_presentation_style=no_header" }
    };

    public static TheoryData<string, string> GameBananaCategories => new()
    {
        { "Zenless Zone Zero", "30305" },
        { "Genshin Impact", "18140" },
        { "Honkai: Star Rail", "22633" },
        { "Arknights: Endfield", "42770" }
    };

    [Theory]
    [MemberData(nameof(MiHoYoWikis))]
    public async Task MiHoYoWiki_ListsTheCharacters(string game, string wikiUrl)
    {
        WikiCatalogDescriptor descriptor = CharacterCatalogClient.Describe(wikiUrl)
            ?? throw new InvalidOperationException($"{game}: the wiki link is no longer recognised.");
        List<OnlineCharacterOption> characters = await Request(token => CharacterCatalogClient.FetchWikiCharactersAsync(Client, descriptor, wikiUrl, token));

        Assert.True(characters.Count >= 20, $"{game}: {characters.Count} characters");
        Assert.All(characters, character => Assert.False(string.IsNullOrWhiteSpace(character.DisplayNameZh), game));
    }

    [Theory]
    [MemberData(nameof(MiHoYoWikis))]
    public async Task HoYoWiki_HasEnglishNames(string game, string wikiUrl)
    {
        WikiCatalogDescriptor descriptor = CharacterCatalogClient.Describe(wikiUrl)!;
        List<HoYoWikiLocalizedCharacter> names = await Request(token => CharacterCatalogClient.FetchHoYoWikiNamesAsync(Client, descriptor, "en-us", token));

        Assert.True(names.Count >= 20, $"{game}: {names.Count} names");
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name.Name), game));
    }

    [Fact]
    public async Task EndfieldSite_ListsTheOperators()
    {
        List<OnlineCharacterOption> operators = await Request(token => CharacterCatalogClient.FetchEndfieldOperatorsAsync(
            Client, EndfieldCharacterCatalog.BuiltIn, "https://wiki.skland.com/endfield", token));

        Assert.True(operators.Count >= 10, $"{operators.Count} operators");
        Assert.All(operators, character => Assert.False(string.IsNullOrWhiteSpace(character.DisplayNameEn)));
    }

    [Theory]
    [MemberData(nameof(GameBananaCategories))]
    public async Task GameBanana_HasCharacterCategories(string game, string categoryId)
    {
        List<OnlineCharacterOption> categories = await Request(token => CharacterCatalogClient.FetchGameBananaCharactersAsync(Client, categoryId, token));

        Assert.True(categories.Count >= 5, $"{game}: {categories.Count} categories");
    }

    [Theory]
    [MemberData(nameof(GameBananaCategories))]
    public async Task GameBanana_ListsModsInTheCategory(string game, string categoryId)
    {
        OnlineCategoryPageResult page = await Request(token => GameBananaCategoryClient.GetPageAsync(Client, categoryId, 1, 20, 0, DateTimeOffset.Now, token));

        Assert.True(page.Mods.Count > 0 && page.TotalCount > 0, $"{game}: {page.Mods.Count} of {page.TotalCount}");
        Assert.All(page.Mods, mod => Assert.False(string.IsNullOrWhiteSpace(mod.Title), game));
    }

    // Retries once, like the app: a single stalled connection to a CDN node is not a site change.
    private static Task<T> Request<T>(Func<CancellationToken, Task<T>> request)
        => CharacterCatalogClient.WithRetryAsync(request, RequestTimeout);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IntegratedModManager-OnlineCheck/1.0");
        return client;
    }
}
