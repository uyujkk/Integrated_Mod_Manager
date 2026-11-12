using System.Net;
using System.Text;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

// Issue #129: top-level categories, site-wide sorting and where Mods outside the characters are installed.
public sealed class OnlineCategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- sorting

    [Theory]
    [InlineData(OnlineSortMode.Likes, "Generic_MostLiked")]
    [InlineData(OnlineSortMode.Downloads, "Generic_MostDownloaded")]
    [InlineData(OnlineSortMode.Updated, "Generic_LatestUpdated")]
    [InlineData(OnlineSortMode.Hotness, null)]
    [InlineData(OnlineSortMode.Views, null)]
    public void LikesDownloadsAndUpdateTimeAreSortedByTheSite(OnlineSortMode mode, string? expected)
        => Assert.Equal(expected, OnlineBrowseOrder.ServerSort(mode));

    [Fact]
    public void SortModeNamesAreUnchanged()
        => Assert.Equal(["Hotness", "Downloads", "Likes", "Views", "Updated"], Enum.GetNames<OnlineSortMode>());

    [Fact]
    public void AServerSortedPageKeepsTheSiteOrder()
    {
        // Download counts arrive later; sorting the page by them again would undo the site's order.
        OnlineModCard[] page = [new() { ItemId = 1, Downloads = 0 }, new() { ItemId = 2, Downloads = 50 }, new() { ItemId = 3, Downloads = 10 }];
        Assert.Equal([1, 2, 3], OnlineBrowseOrder.Arrange(page, OnlineSortMode.Downloads).Select(mod => mod.ItemId));
        Assert.Equal([1, 2, 3], OnlineBrowseOrder.Arrange(page, OnlineSortMode.Likes).Select(mod => mod.ItemId));
    }

    [Fact]
    public void HotnessIsStillASortOfThePage()
    {
        OnlineModCard[] page = [new() { ItemId = 1, HotnessScore = 1 }, new() { ItemId = 2, HotnessScore = 3 }, new() { ItemId = 3, HotnessScore = 2 }];
        Assert.Equal([2, 3, 1], OnlineBrowseOrder.Arrange(page, OnlineSortMode.Hotness).Select(mod => mod.ItemId));
    }

    [Fact]
    public void ThePageUrlCarriesTheSiteSortBeforePaging()
    {
        Assert.Equal("https://gamebanana.com/apiv11/Mod/Index?_aFilters%5BGeneric_Category%5D=18140&_sSort=Generic_MostLiked&_nPerpage=20&_nPage=3",
            OnlineBrowsePolicy.CategoryPageUrl("18140", 3, 20, 0, "Generic_MostLiked"));
        Assert.Equal("https://gamebanana.com/apiv11/Mod/Index?_aFilters%5BGeneric_Category%5D=18140&_nPerpage=20&_nPage=1",
            OnlineBrowsePolicy.CategoryPageUrl("18140", 1, 20));
        Assert.Contains("Generic_Submitter%5D=7&_sSort=Generic_LatestUpdated&", OnlineBrowsePolicy.CategoryPageUrl("1", 1, 20, 7, "Generic_LatestUpdated"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a&b")]
    [InlineData("x=y")]
    public void ThePageUrlRefusesOddSortNames(string sort)
        => Assert.Throws<ArgumentException>(() => OnlineBrowsePolicy.CategoryPageUrl("1", 1, 20, 0, sort));

    [Fact]
    public async Task GetPageSendsTheSort()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return Json("""{ "_aMetadata": { "_nRecordCount": 0, "_nPerpage": 20 }, "_aRecords": [] }""");
        }));
        await GameBananaCategoryClient.GetPageAsync(client, "5", 2, 20, 0, Now, serverSort: "Generic_MostDownloaded");
        Assert.Equal([OnlineBrowsePolicy.CategoryPageUrl("5", 2, 20, 0, "Generic_MostDownloaded")], requests);
    }

    // ---------------------------------------------------------------- the category tree

    [Fact]
    public void CategoryUrls()
    {
        Assert.Equal("https://gamebanana.com/apiv11/ModCategory/18140/ProfilePage", OnlineBrowsePolicy.CategoryProfileUrl("18140"));
        Assert.Equal("https://gamebanana.com/apiv11/Mod/Categories?_idGameRow=8552&_sSort=a_to_z&_bShowEmpty=true", OnlineBrowsePolicy.GameCategoriesUrl("8552"));
        Assert.Equal("https://gamebanana.com/apiv11/Mod/Categories?_idCategoryRow=17510&_sSort=a_to_z&_bShowEmpty=true", OnlineBrowsePolicy.SubcategoriesUrl("17510"));
        Assert.Throws<ArgumentException>(() => OnlineBrowsePolicy.GameCategoriesUrl("0"));
        Assert.Throws<ArgumentException>(() => OnlineBrowsePolicy.CategoryProfileUrl("x"));
    }

    [Fact]
    public void ParsesTheGameAndTheCategories()
    {
        Assert.Equal("8552", GameBananaCategoryTreeClient.ParseGameId("""{ "_idRow": 1, "_aGame": { "_idRow": 8552, "_sName": "G" } }"""));
        Assert.Null(GameBananaCategoryTreeClient.ParseGameId("""{ "_idRow": 1 }"""));
        Assert.Null(GameBananaCategoryTreeClient.ParseGameId("[]"));

        List<OnlineCategory> categories = GameBananaCategoryTreeClient.ParseCategories("""
            [ { "_idRow": 17510, "_sName": "Skins", "_nItemCount": 12613, "_nCategoryCount": 3 },
              { "_idRow": 0, "_sName": "no id" }, { "_idRow": 5 }, "text",
              { "_idRow": 22474, "_sName": " UI ", "_nItemCount": "211", "_nCategoryCount": 4 } ]
            """);
        Assert.Equal([new OnlineCategory("17510", "Skins", 12613, 3), new OnlineCategory("22474", "UI", 211, 4)], categories);
        Assert.Empty(GameBananaCategoryTreeClient.ParseCategories("{}"));
    }

    [Fact]
    public async Task ReadFindsTheTopThatHoldsTheCharactersAndItsOtherBranches()
    {
        var replies = new Dictionary<string, string>
        {
            [OnlineBrowsePolicy.CategoryProfileUrl("101")] = """{ "_aGame": { "_idRow": 9 } }""",
            [OnlineBrowsePolicy.GameCategoriesUrl("9")] = """
                [ { "_idRow": 10, "_sName": "Skins", "_nItemCount": 900, "_nCategoryCount": 3 },
                  { "_idRow": 20, "_sName": "UI", "_nItemCount": 50, "_nCategoryCount": 2 },
                  { "_idRow": 30, "_sName": "Other/Misc", "_nItemCount": 40, "_nCategoryCount": 0 } ]
                """,
            [OnlineBrowsePolicy.SubcategoriesUrl("10")] = """
                [ { "_idRow": 101, "_sName": "Characters", "_nItemCount": 800, "_nCategoryCount": 60 },
                  { "_idRow": 102, "_sName": "Weapons", "_nItemCount": 80, "_nCategoryCount": 2 },
                  { "_idRow": 103, "_sName": "Entities/NPCs", "_nItemCount": 9, "_nCategoryCount": 0 } ]
                """,
            [OnlineBrowsePolicy.SubcategoriesUrl("102")] = """
                [ { "_idRow": 1021, "_sName": "Swords", "_nItemCount": 40, "_nCategoryCount": 0 },
                  { "_idRow": 1022, "_sName": "Bows", "_nItemCount": 40, "_nCategoryCount": 0 } ]
                """
        };
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            return replies.TryGetValue(url, out string? body) ? Json(body) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        OnlineCategoryIndex index = (await GameBananaCategoryTreeClient.ReadAsync(client, "101", Now))!;

        Assert.Equal("9", index.GameId);
        Assert.Equal(["Skins", "UI", "Other/Misc"], index.TopCategories.Select(top => top.Name));
        Assert.Equal("10", index.CharacterTopId);
        Assert.Equal(["Characters", "Weapons", "Entities/NPCs"], index.SubcategoriesOf("10").Select(child => child.Name));
        Assert.Equal(new Dictionary<string, string> { ["1021"] = "102", ["1022"] = "102" }, index.CharacterTopBranches);
        // Skins is read first (the largest); UI is never read, and the character category's own children are not read.
        Assert.Equal(4, requests.Count);
        Assert.DoesNotContain(OnlineBrowsePolicy.SubcategoriesUrl("20"), requests);
        Assert.DoesNotContain(OnlineBrowsePolicy.SubcategoriesUrl("101"), requests);
        Assert.True(index.IsFresh(Now.AddHours(23)));
        Assert.False(index.IsFresh(Now.AddDays(1)));
        Assert.Equal("10", index.TopOf("103")!.Id);
        Assert.Equal("20", index.TopOf("20")!.Id);
        Assert.Null(index.TopOf("999"));
    }

    [Fact]
    public async Task ReadStopsWithoutAGameAndReportsRateLimits()
    {
        using var noGame = new HttpClient(new Handler(_ => Json("{}")));
        Assert.Null(await GameBananaCategoryTreeClient.ReadAsync(noGame, "1", Now));

        using var limited = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        }));
        OnlineRateLimitException limit = await Assert.ThrowsAsync<OnlineRateLimitException>(() => GameBananaCategoryTreeClient.ReadAsync(limited, "1", Now));
        Assert.Equal(TimeSpan.FromSeconds(30), limit.RetryAfter);
    }

    // ---------------------------------------------------------------- where a Mod goes

    private static OnlineCategoryIndex Index(string repositoryCategory = "101") => new()
    {
        RepositoryCategoryId = repositoryCategory, GameId = "9", CharacterTopId = "10",
        TopCategories = [new("10", "Skins", 900, 3), new("20", "UI", 50, 2), new("30", "Other/Misc", 40, 0)],
        Subcategories = new()
        {
            ["10"] = [new("101", "Characters", 800, 60), new("102", "Weapons", 80, 2), new("103", "Entities/NPCs", 9, 0)],
            ["20"] = [new("201", "HUD", 20, 0)]
        },
        CharacterTopBranches = new() { ["1021"] = "102" }
    };

    [Theory]
    // A character (a child of the repository category) stays a character.
    [InlineData("Skins", "5001", "Some Hero", null)]
    [InlineData("Skins", "101", "Characters", null)]
    // Other branches under the character top, directly or one level down.
    [InlineData("Skins", "102", "Weapons", "Weapons")]
    [InlineData("Skins", "1021", "Swords", "Weapons")]
    [InlineData("Skins", "103", "Entities/NPCs", "Entities/NPCs")]
    // Directly in the character top.
    [InlineData("Skins", "10", "Skins", "Skins")]
    [InlineData("Skins", "", "", "Skins")]
    // Other top-level categories, whatever their subcategory; the name as the site spells it.
    [InlineData("UI", "201", "HUD", "UI")]
    [InlineData("ui", "201", "HUD", "UI")]
    [InlineData("Other/Misc", "", "", "Other/Misc")]
    [InlineData("Maps", "", "", "Maps")]
    // No root name: the subcategory's own top decides.
    [InlineData("", "201", "HUD", "UI")]
    public void ModsOutsideTheCharactersGoToTheirCategory(string root, string subId, string subName, string? expected)
        => Assert.Equal(expected, OnlineCategoryPlacement.CategoryName(Index(), root, subId, subName));

    [Fact]
    public void WithoutAnIndexOrWhenTheCharactersAreATopEverythingStaysACharacter()
    {
        Assert.Null(OnlineCategoryPlacement.CategoryName(null, "UI", "201", "HUD"));
        Assert.Null(OnlineCategoryPlacement.CategoryName(new OnlineCategoryIndex { TopCategories = Index().TopCategories }, "UI", "201", "HUD"));
        OnlineCategoryIndex topIsCharacters = new()
        {
            RepositoryCategoryId = "10", CharacterTopId = "10", TopCategories = Index().TopCategories,
            Subcategories = new() { ["10"] = [new("5001", "Some Hero", 30, 0)] }
        };
        Assert.Null(OnlineCategoryPlacement.CategoryName(topIsCharacters, "Skins", "5001", "Some Hero"));
        Assert.Equal("UI", OnlineCategoryPlacement.CategoryName(topIsCharacters, "UI", "201", "HUD"));
    }

    [Theory]
    [InlineData("UI", "UI")]
    [InlineData("Other/Misc", "Other")]
    [InlineData("Entities/NPCs", "Entities")]
    [InlineData("  Weapons  ", "Weapons")]
    [InlineData("A:B*C?", "A_B_C_")]
    [InlineData("Two   spaces", "Two spaces")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("CON", "CON_")]
    [InlineData("/Leading", "Leading")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("///", null)]
    [InlineData("???", null)]
    [InlineData("..", null)]
    public void CategoryFolderNamesAreMadeSafe(string category, string? expected)
        => Assert.Equal(expected, CategoryFolderPlanner.FolderName(category));

    [Fact]
    public void LongCategoryNamesAreShortened()
    {
        string name = CategoryFolderPlanner.FolderName(new string('x', 100))!;
        Assert.Equal(64, name.Length);
        Assert.True(CharacterFolderPlanner.IsSafeFolderName(name));
    }

    [Fact]
    public void ThePlanReusesCreatesOrAsks()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-category-plan");
        CharacterFolderPlan reuse = CategoryFolderPlanner.Plan(root, [Path.Combine(root, "ui")], [], "UI");
        Assert.Equal((CharacterFolderAction.UseExisting, Path.Combine(root, "ui")), (reuse.Action, reuse.Path));

        CharacterFolderPlan create = CategoryFolderPlanner.Plan(root, [Path.Combine(root, "Hero")], [], "Other/Misc");
        Assert.Equal((CharacterFolderAction.Create, CharacterFolderReason.NoMatchCreate, Path.Combine(root, "Other")), (create.Action, create.Reason, create.Path));

        Assert.Equal(CharacterFolderReason.NameConflict, CategoryFolderPlanner.Plan(root, [], ["weapons"], "Weapons").Reason);
        Assert.Equal(CharacterFolderReason.InvalidName, CategoryFolderPlanner.Plan(root, [], [], "???").Reason);
        Assert.Equal(CharacterFolderAction.AskUser, CategoryFolderPlanner.Plan(root, [], [], null).Action);
    }

    [Fact]
    public void ThePlannedFolderIsCreatedAsADirectChildOfTheRepository()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-category-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CharacterFolderPlan plan = CategoryFolderPlanner.Plan(root, [], [], "Other/Misc");
            string created = CharacterFolderPlanner.CreatePlannedFolder(root, plan.Path!);
            Assert.Equal(Path.Combine(root, "Other"), created);
            Assert.True(Directory.Exists(created));
            Assert.Equal(CharacterFolderAction.UseExisting, CategoryFolderPlanner.Plan(root, Directory.GetDirectories(root), [], "Other/Misc").Action);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---------------------------------------------------------------- the offline samples (IMM_ONLINE_FIXTURES)

    [Fact]
    public async Task TheSamplesHaveTheCategoriesAndTheSortedLists()
    {
        string samples = Path.Combine(FindRepositoryRoot(), "tests", "TestData", "OnlineFixtures");
        using HttpClient client = OnlineHttpClientFactory.Create("IntegratedModManager/test", samples);

        OnlineCategoryIndex index = (await GameBananaCategoryTreeClient.ReadAsync(client, "90001", Now))!;
        Assert.Equal(["Other/Misc", "Skins", "UI"], index.TopCategories.Select(top => top.Name));
        Assert.Equal("90100", index.CharacterTopId);
        Assert.Equal("90101", index.CharacterTopBranches["90111"]);
        Assert.Equal(["HUD", "Menus"], (await GameBananaCategoryTreeClient.ReadSubcategoriesAsync(client, "90200")).Select(child => child.Name));

        OnlineCategoryPageResult liked = await GameBananaCategoryClient.GetPageAsync(client, "90100", 1, 40, 0, Now, serverSort: "Generic_MostLiked");
        Assert.Equal(liked.Mods.Select(mod => mod.Likes).OrderByDescending(likes => likes), liked.Mods.Select(mod => mod.Likes));
        Assert.Equal(16, liked.TotalCount);
        OnlineCategoryPageResult ui = await GameBananaCategoryClient.GetPageAsync(client, "90200", 1, 40, 0, Now);
        Assert.Equal(["UI", "UI"], ui.Mods.Select(mod => mod.RootCategoryName));
        Assert.Equal(["UI", "UI"], ui.Mods.Select(mod => OnlineCategoryPlacement.CategoryName(index, mod.RootCategoryName, mod.CategoryId, mod.CharacterName)));
        OnlineCategoryPageResult weapons = await GameBananaCategoryClient.GetPageAsync(client, "90101", 1, 40, 0, Now);
        Assert.Equal(["Weapons", "Weapons"], weapons.Mods.Select(mod => OnlineCategoryPlacement.CategoryName(index, mod.RootCategoryName, mod.CategoryId, mod.CharacterName)));

        // A request the samples do not have still fails.
        await Assert.ThrowsAsync<HttpRequestException>(() => GameBananaCategoryClient.GetPageAsync(client, "90999", 1, 40, 0, Now, serverSort: "Generic_MostLiked"));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "IntegratedModManager.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("IntegratedModManager.sln was not found above the test output.");
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(reply(request));
    }
}
