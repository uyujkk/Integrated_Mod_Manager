using System.Net;
using System.Text.Json;

namespace IntegratedModManager.Core.Online;

/// <summary>One GameBanana Mod category: its ID, name, how many Mods it holds and how many subcategories it has.</summary>
public sealed record OnlineCategory(string Id, string Name, int ItemCount, int SubcategoryCount);

/// <summary>
/// The Mod categories of a repository's game (issue #129), read from GameBanana and cached: the top-level categories
/// (Skins, UI, Other/Misc…), the one that holds the repository's character category, and the branches under it that
/// are not characters (Weapons, Entities/NPCs…). Nothing here names a game; every list comes from the site.
/// </summary>
public sealed class OnlineCategoryIndex
{
    /// <summary>The repository category this index was read for.</summary>
    public string RepositoryCategoryId { get; init; } = string.Empty;

    public string GameId { get; init; } = string.Empty;

    public List<OnlineCategory> TopCategories { get; init; } = [];

    /// <summary>The top-level category that holds the repository's character category; empty when none was found.</summary>
    public string CharacterTopId { get; init; } = string.Empty;

    /// <summary>The direct subcategories of each top-level category read so far, by the top category's ID.</summary>
    public Dictionary<string, List<OnlineCategory>> Subcategories { get; init; } = [];

    /// <summary>Categories one level further down under the character top: category ID → the ID of its parent there.</summary>
    public Dictionary<string, string> CharacterTopBranches { get; init; } = [];

    public DateTimeOffset FetchedAtUtc { get; init; }

    public OnlineCategory? CharacterTop => TopCategories.FirstOrDefault(category => category.Id == CharacterTopId);

    public OnlineCategory? Find(string? id) => string.IsNullOrEmpty(id) ? null
        : TopCategories.FirstOrDefault(category => category.Id == id)
            ?? Subcategories.Values.SelectMany(list => list).FirstOrDefault(category => category.Id == id);

    /// <summary>The top-level category <paramref name="id"/> is, or is directly under; null when it is unknown.</summary>
    public OnlineCategory? TopOf(string? id)
    {
        if (Find(id) is null) return null;
        if (TopCategories.Any(category => category.Id == id)) return TopCategories.First(category => category.Id == id);
        string? parent = Subcategories.FirstOrDefault(pair => pair.Value.Any(category => category.Id == id)).Key;
        return parent is null ? null : TopCategories.FirstOrDefault(category => category.Id == parent);
    }

    public List<OnlineCategory> SubcategoriesOf(string? topId)
        => topId is not null && Subcategories.TryGetValue(topId, out List<OnlineCategory>? list) ? list : [];

    /// <summary>Read again after a day, like the character catalog.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(1);

    public bool IsFresh(DateTimeOffset now) => now - FetchedAtUtc < FreshFor;
}

/// <summary>Reads <see cref="OnlineCategoryIndex"/> from GameBanana's public API (issue #129).</summary>
public static class GameBananaCategoryTreeClient
{
    /// <summary>
    /// The category tree for a repository category: its profile names the game; the game's top-level categories follow;
    /// then the top-level categories with subcategories are read until the one holding the repository category is found
    /// (largest first, so usually one request), and the non-character branches under it one level further down.
    /// </summary>
    public static async Task<OnlineCategoryIndex?> ReadAsync(HttpClient client, string repositoryCategoryId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        string? gameId = ParseGameId(await GetStringAsync(client, OnlineBrowsePolicy.CategoryProfileUrl(repositoryCategoryId), cancellationToken));
        if (gameId is null) return null;
        List<OnlineCategory> tops = ParseCategories(await GetStringAsync(client, OnlineBrowsePolicy.GameCategoriesUrl(gameId), cancellationToken));
        if (tops.Count == 0) return null;

        var subcategories = new Dictionary<string, List<OnlineCategory>>();
        string characterTopId = tops.Any(top => top.Id == repositoryCategoryId) ? repositoryCategoryId : string.Empty;
        foreach (OnlineCategory top in tops.Where(top => top.SubcategoryCount > 0).OrderByDescending(top => top.ItemCount))
        {
            if (characterTopId.Length > 0) break;
            List<OnlineCategory> children = ParseCategories(await GetStringAsync(client, OnlineBrowsePolicy.SubcategoriesUrl(top.Id), cancellationToken));
            subcategories[top.Id] = children;
            if (children.Any(child => child.Id == repositoryCategoryId)) characterTopId = top.Id;
        }

        var branches = new Dictionary<string, string>();
        if (characterTopId.Length > 0 && characterTopId != repositoryCategoryId)
        {
            foreach (OnlineCategory child in subcategories[characterTopId].Where(child => child.SubcategoryCount > 0 && child.Id != repositoryCategoryId))
            {
                foreach (OnlineCategory grandchild in ParseCategories(await GetStringAsync(client, OnlineBrowsePolicy.SubcategoriesUrl(child.Id), cancellationToken)))
                    branches[grandchild.Id] = child.Id;
            }
        }

        return new OnlineCategoryIndex
        {
            RepositoryCategoryId = repositoryCategoryId, GameId = gameId, TopCategories = tops, CharacterTopId = characterTopId,
            Subcategories = subcategories, CharacterTopBranches = branches, FetchedAtUtc = now
        };
    }

    /// <summary>The direct subcategories of one top-level category, read when it is first chosen.</summary>
    public static async Task<List<OnlineCategory>> ReadSubcategoriesAsync(HttpClient client, string categoryId, CancellationToken cancellationToken = default)
        => ParseCategories(await GetStringAsync(client, OnlineBrowsePolicy.SubcategoriesUrl(categoryId), cancellationToken));

    /// <summary>The game ID in a category profile (<c>_aGame._idRow</c>), or null.</summary>
    public static string? ParseGameId(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("_aGame", out JsonElement game)) return null;
        int id = GameBananaJson.Int32(game, "_idRow");
        return id > 0 ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The categories in a <c>Mod/Categories</c> list; entries without an ID or name are skipped.</summary>
    public static List<OnlineCategory> ParseCategories(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        var categories = new List<OnlineCategory>();
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            int id = GameBananaJson.Int32(item, "_idRow");
            string? name = GameBananaJson.Text(item, "_sName")?.Trim();
            if (id <= 0 || string.IsNullOrEmpty(name)) continue;
            categories.Add(new OnlineCategory(id.ToString(System.Globalization.CultureInfo.InvariantCulture), name,
                GameBananaJson.Int32(item, "_nItemCount"), GameBananaJson.Int32(item, "_nCategoryCount")));
        }
        return categories;
    }

    private static async Task<string> GetStringAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new OnlineRateLimitException(response.Headers.RetryAfter?.Delta);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

/// <summary>
/// Where an online Mod belongs in the repository (issue #129). Mods in the repository's character category go to
/// their character's folder as before (null here). Any other Mod goes to a folder named after its category:
/// <list type="bullet">
/// <item>under the top-level category that holds the characters, the branch it is in (Weapons, Entities/NPCs), or that
/// top-level category itself (Skins) for a Mod directly in it;</item>
/// <item>under any other top-level category, that category (UI, Other/Misc).</item>
/// </list>
/// </summary>
public static class OnlineCategoryPlacement
{
    /// <summary>The category name a Mod's folder is named after, or null for a character Mod (and whenever unsure).</summary>
    public static string? CategoryName(OnlineCategoryIndex? index, string? rootCategoryName, string? subCategoryId, string? subCategoryName)
    {
        if (index?.CharacterTop is not { } characterTop) return null;
        string root = rootCategoryName?.Trim() ?? string.Empty;
        string subId = subCategoryId?.Trim() ?? string.Empty;
        string subName = subCategoryName?.Trim() ?? string.Empty;

        if (root.Length > 0 && !string.Equals(root, characterTop.Name, StringComparison.OrdinalIgnoreCase))
            return TopNamed(index, root)?.Name ?? root;
        if (root.Length == 0 && index.TopOf(subId) is { } subTop && subTop.Id != characterTop.Id)
            return subTop.Name;

        // Under the character top. When the repository category is that top itself, everything in it is a character.
        if (characterTop.Id == index.RepositoryCategoryId) return null;
        if (subId == index.RepositoryCategoryId) return null;
        if (index.CharacterTopBranches.TryGetValue(subId, out string? branchId) && index.Find(branchId) is { } branch) return branch.Name;
        if (index.SubcategoriesOf(characterTop.Id).FirstOrDefault(child => child.Id == subId) is { } direct) return direct.Name;
        if (subId == characterTop.Id || (subId.Length == 0 && (subName.Length == 0 || string.Equals(subName, characterTop.Name, StringComparison.OrdinalIgnoreCase))))
            return characterTop.Name;
        return null;
    }

    private static OnlineCategory? TopNamed(OnlineCategoryIndex index, string name)
        => index.TopCategories.FirstOrDefault(top => string.Equals(top.Name, name, StringComparison.OrdinalIgnoreCase));
}
