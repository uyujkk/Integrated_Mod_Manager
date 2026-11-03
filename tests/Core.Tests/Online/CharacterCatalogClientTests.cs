using System.Net;
using System.Text;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class CharacterCatalogClientTests
{
    private static readonly WikiCatalogDescriptor Genshin = CharacterCatalogClient.Describe("https://baike.mihoyo.com/ys/obc/channel/map/189/25")!;

    // Answers each request with the body of the first route whose key the URL contains.
    private static (HttpClient Client, List<HttpRequestMessage> Requests) Client(params (string UrlPart, string Body)[] routes)
    {
        var requests = new List<HttpRequestMessage>();
        var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            string url = request.RequestUri!.ToString();
            (string _, string body) = routes.FirstOrDefault(route => url.Contains(route.UrlPart, StringComparison.Ordinal));
            return body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        return (client, requests);
    }

    [Theory]
    [InlineData("https://baike.mihoyo.com/zzz/wiki/channel/map/2/43", "zzz_wiki", 43, "zzz")]
    [InlineData("https://baike.mihoyo.com/ys/obc/channel/map/189/25?visit_device=pc", "ys_obc", 25, "genshin")]
    [InlineData("https://bbs.mihoyo.com/sr/wiki/channel/map/17/18", "sr_wiki", 18, "hsr")]
    public void Describe_RecognisesTheMiHoYoWikis(string url, string appSn, int channel, string hoYoWikiApp)
    {
        WikiCatalogDescriptor descriptor = CharacterCatalogClient.Describe(url)!;

        Assert.Equal((appSn, channel, hoYoWikiApp), (descriptor.AppSn, descriptor.ChannelId, descriptor.HoYoWikiApp));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://wiki.skland.com/endfield")]
    public void Describe_ReturnsNullForOtherLinks(string? url) => Assert.Null(CharacterCatalogClient.Describe(url));

    [Fact]
    public async Task FetchWikiCharacters_ReadsTheCharacterChannelInOrder()
    {
        var (client, requests) = Client(("content/list", """
            { "data": { "list": [
                { "id": 5, "list": [ { "title": "Not a character" } ] },
                { "id": 25, "list": [
                    { "title": "胡桃", "alias_name": "Hu Tao", "icon": "https://img/hutao.png", "content_id": 77 },
                    { "title": " 胡桃 " },
                    { "title": "" },
                    { "title": "钟离", "content_id": 0 } ] } ] } }
            """));

        List<OnlineCharacterOption> characters = await CharacterCatalogClient.FetchWikiCharactersAsync(client, Genshin, "https://fallback");

        Assert.Equal(["胡桃", "钟离"], characters.Select(character => character.Key));
        Assert.Equal([0, 1], characters.Select(character => character.WikiOrder));
        Assert.All(characters, character => Assert.True(character.IsWikiCharacter));
        Assert.Equal("https://img/hutao.png", characters[0].AvatarUrl);
        Assert.Equal("https://baike.mihoyo.com/ys/obc/content/77/detail?bbs_presentation_style=no_header&visit_device=pc", characters[0].WikiUrl);
        Assert.Equal("https://fallback", characters[1].WikiUrl);
        Assert.Contains("Hu Tao", characters[0].Aliases);
        Assert.Contains("app_sn=ys_obc&channel_id=25", Assert.Single(requests).RequestUri!.Query);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "data": { "list": [ { "id": 3, "list": [] } ] } }""")]
    public async Task FetchWikiCharacters_ToleratesMissingChannels(string body)
    {
        var (client, _) = Client(("content/list", body));

        Assert.Empty(await CharacterCatalogClient.FetchWikiCharactersAsync(client, Genshin, null));
    }

    [Fact]
    public async Task FetchGameBananaCharacters_ReadsFlatCategories()
    {
        var (client, requests) = Client(("Mod/Categories", """
            [ { "_idRow": 101, "_sName": "Hu Tao", "_sIconUrl": "https://img/a.png" },
              { "_sName": "From URL", "_sUrl": "https://gamebanana.com/mods/cats/202" },
              { "_idRow": 101, "_sName": "Duplicate" },
              { "_sName": "No id" } ]
            """));

        List<OnlineCharacterOption> characters = await CharacterCatalogClient.FetchGameBananaCharactersAsync(client, "18140");

        Assert.Equal([("Hu Tao", "101"), ("From URL", "202")], characters.Select(character => (character.Key, character.CategoryId)));
        Assert.Equal("https://img/a.png", characters[0].AvatarUrl);
        Assert.Contains("_idCategoryRow=18140", Assert.Single(requests).RequestUri!.Query);
    }

    [Fact]
    public async Task FetchGameBananaCharacters_ReadsEachGroupOfNestedCategories()
    {
        var (client, requests) = Client(
            ("ModCategory/7/SubCategories", """[ { "_idRow": 71, "_sName": "Pyro one" } ]"""),
            ("ModCategory/8/SubCategories", """[ { "_idRow": 81, "_sName": "Hydro one" }, { "_idRow": 71, "_sName": "Again" } ]"""),
            ("Mod/Categories", """[ { "_idRow": 7, "_sName": "Pyro", "_nCategoryCount": 1 }, { "_idRow": 8, "_sName": "Hydro", "_nCategoryCount": 2 }, { "_idRow": 9, "_sName": "Empty" } ]"""));

        List<OnlineCharacterOption> characters = await CharacterCatalogClient.FetchGameBananaCharactersAsync(client, "1");

        Assert.Equal(["71", "81"], characters.Select(character => character.CategoryId));
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task FetchGameBananaCharacters_FailsOnAnErrorStatus()
    {
        var (client, _) = Client();

        await Assert.ThrowsAsync<HttpRequestException>(() => CharacterCatalogClient.FetchGameBananaCharactersAsync(client, "1"));
    }

    [Fact]
    public async Task FetchHoYoWikiNames_PagesUntilTheTotalAndSendsTheLanguage()
    {
        int page = 0;
        var requests = new List<HttpRequestMessage>();
        var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            page++;
            string items = string.Join(",", Enumerable.Range(0, page == 1 ? 50 : 2).Select(index => $$"""{ "entry_page_id": "{{page}}-{{index}}", "name": "Name {{page}}-{{index}}" }"""));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{ "data": { "total": "52", "list": [ {{items}} ] } }""") };
        }));

        List<HoYoWikiLocalizedCharacter> names = await CharacterCatalogClient.FetchHoYoWikiNamesAsync(client, Genshin, "en-us");

        Assert.Equal(52, names.Count);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("en-us", Assert.Single(request.Headers.GetValues("x-rpc-language")));
            Assert.Equal("genshin", Assert.Single(request.Headers.GetValues("x-rpc-wiki_app")));
        });
    }

    [Fact]
    public async Task FetchHoYoWikiNames_StopsOnAShortPageWithoutATotal()
    {
        var (client, requests) = Client(("get_entry_page_list", """{ "data": { "list": [ { "entry_page_id": "1", "name": "A" }, { "entry_page_id": "1", "name": "A again" } ] } }"""));

        List<HoYoWikiLocalizedCharacter> names = await CharacterCatalogClient.FetchHoYoWikiNamesAsync(client, Genshin, "zh-cn");

        Assert.Equal("A", Assert.Single(names).Name);
        Assert.Single(requests);
    }

    [Fact]
    public void ApplyEnglishNames_PairsLanguagesByEntryAndMatchesAnyName()
    {
        var hutao = new OnlineCharacterOption { Key = "胡桃", DisplayNameZh = "胡桃", DisplayNameEn = "胡桃", Aliases = ["胡桃"] };
        var zhongli = new OnlineCharacterOption { Key = "zl", DisplayNameZh = "zl", DisplayNameEn = "zl", Aliases = ["钟离"] };
        var unknown = new OnlineCharacterOption { Key = "无名", DisplayNameZh = "无名", DisplayNameEn = "无名", Aliases = ["无名"] };

        CharacterCatalogClient.ApplyEnglishNames([hutao, zhongli, unknown],
            [new("1", "胡桃"), new("2", "钟离"), new("3", "没有英文")],
            [new("1", " Hu Tao "), new("2", "Zhongli")]);

        Assert.Equal(("Hu Tao", "Zhongli", "无名"), (hutao.DisplayNameEn, zhongli.DisplayNameEn, unknown.DisplayNameEn));
        Assert.Contains("Hu Tao", hutao.Aliases);
        Assert.Equal(["无名"], unknown.Aliases);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
}
