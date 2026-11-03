using System.Globalization;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Endfield;

namespace IntegratedModManager.Core.Online;

/// <summary>Where a miHoYo wiki keeps its character channel, and the HoYoWiki menu with the same characters.</summary>
public sealed record WikiCatalogDescriptor(
    string AppSn,
    int ChannelId,
    string ApiUrl,
    string ContentUrlTemplate,
    string HoYoWikiApp,
    string HoYoWikiCharacterMenuId);

public sealed record HoYoWikiLocalizedCharacter(string EntryPageId, string Name);

/// <summary>
/// Character lists from the game wikis and GameBanana. Requests and parsing only; the window decides
/// which sources a repository uses, caches the result and merges the lists.
/// </summary>
public static class CharacterCatalogClient
{
    public const string HoYoWikiApiBaseUrl = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/wapi";
    public const string EndfieldOperatorCatalogUrl = "https://endfield.hypergryph.com/operator";

    /// <summary>
    /// The catalog behind a repository's wiki link, else behind its game's built-in wiki: a repository
    /// set up by hand may have the game's category but no wiki link, and would otherwise get no catalog.
    /// </summary>
    public static WikiCatalogDescriptor? Describe(string? wikiUrl, string? gameWikiUrl) => Describe(wikiUrl) ?? Describe(gameWikiUrl);

    /// <summary>
    /// Runs one catalog request with its own time limit, and once more after a timeout or a network error.
    /// A stalled request would otherwise hold the catalog for HttpClient's 100 seconds and then give up.
    /// </summary>
    public static async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> request, TimeSpan timeout, Action<Exception>? retrying = null,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            try
            {
                return await request(limit.Token);
            }
            catch (Exception ex) when (attempt < 2 && !cancellationToken.IsCancellationRequested
                && ex is HttpRequestException or OperationCanceledException)
            {
                retrying?.Invoke(ex);
            }
        }
    }

    /// <summary>The catalog behind a repository's wiki link, or null for a wiki without one.</summary>
    public static WikiCatalogDescriptor? Describe(string? wikiUrl)
    {
        if (!Uri.TryCreate(wikiUrl?.Trim() ?? string.Empty, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string path = uri.AbsolutePath;
        if (path.Contains("/zzz/wiki/", StringComparison.OrdinalIgnoreCase))
        {
            return new WikiCatalogDescriptor(
                "zzz_wiki",
                43,
                "https://act-api-takumi-static.mihoyo.com/common/blackboard/zzz_wiki/v1/home/content/list",
                "https://baike.mihoyo.com/zzz/wiki/content/{0}/detail",
                "zzz",
                "8");
        }

        if (path.Contains("/ys/obc/", StringComparison.OrdinalIgnoreCase))
        {
            return new WikiCatalogDescriptor(
                "ys_obc",
                25,
                "https://act-api-takumi-static.mihoyo.com/common/blackboard/ys_obc/v1/home/content/list",
                "https://baike.mihoyo.com/ys/obc/content/{0}/detail?bbs_presentation_style=no_header&visit_device=pc",
                "genshin",
                "2");
        }

        if (path.Contains("/sr/wiki/", StringComparison.OrdinalIgnoreCase))
        {
            return new WikiCatalogDescriptor(
                "sr_wiki",
                18,
                "https://act-api-takumi-static.mihoyo.com/common/blackboard/sr_wiki/v1/home/content/list",
                "https://bbs.mihoyo.com/sr/wiki/content/{0}/detail?bbs_presentation_style=no_header",
                "hsr",
                "104");
        }

        return null;
    }

    /// <summary>The characters of the wiki's character channel, in wiki order; titles repeat only once.</summary>
    public static async Task<List<OnlineCharacterOption>> FetchWikiCharactersAsync(HttpClient client, WikiCatalogDescriptor descriptor,
        string? fallbackWikiUrl, CancellationToken cancellationToken = default)
    {
        string requestUrl = $"{descriptor.ApiUrl}?app_sn={Uri.EscapeDataString(descriptor.AppSn)}&channel_id={descriptor.ChannelId}&lang=zh-cn";
        using HttpResponseMessage response = await client.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        if (!document.RootElement.TryGetProperty("data", out JsonElement data)
            || !data.TryGetProperty("list", out JsonElement channels)
            || channels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        JsonElement characterItems = channels.EnumerateArray()
            .FirstOrDefault(channel => GameBananaJson.Int32(channel, "id") == descriptor.ChannelId
                && channel.TryGetProperty("list", out JsonElement items) && items.ValueKind == JsonValueKind.Array);
        if (characterItems.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var characters = new List<OnlineCharacterOption>();
        var knownTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in characterItems.GetProperty("list").EnumerateArray())
        {
            string title = GameBananaJson.Text(item, "title")?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title) || !knownTitles.Add(OnlineCharacterCatalogMatcher.NormalizeLookup(title)))
            {
                continue;
            }

            int contentId = GameBananaJson.Int32(item, "content_id");
            characters.Add(new OnlineCharacterOption
            {
                Key = title,
                DisplayNameZh = title,
                DisplayNameEn = title,
                AvatarUrl = GameBananaJson.Text(item, "icon")?.Trim() ?? string.Empty,
                WikiUrl = contentId > 0 ? string.Format(CultureInfo.InvariantCulture, descriptor.ContentUrlTemplate, contentId) : fallbackWikiUrl ?? string.Empty,
                Aliases = OnlineCharacterCatalogMatcher.ParseWikiAliases(title, GameBananaJson.Text(item, "alias_name")?.Trim() ?? string.Empty),
                IsWikiCharacter = true,
                WikiOrder = characters.Count
            });
        }

        return characters;
    }

    /// <summary>
    /// The character categories under a GameBanana category. Games that group characters (by element,
    /// say) have nested categories; their subcategories are read one group at a time.
    /// </summary>
    public static async Task<List<OnlineCharacterOption>> FetchGameBananaCharactersAsync(HttpClient client, string categoryId,
        CancellationToken cancellationToken = default)
    {
        string requestUrl = $"https://gamebanana.com/apiv11/Mod/Categories?_idCategoryRow={Uri.EscapeDataString(categoryId)}&_sSort=a_to_z&_bShowEmpty=true";
        using JsonDocument document = await GetJsonAsync(client, requestUrl, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var characters = new List<OnlineCharacterOption>();
        bool hasNestedCategories = document.RootElement.EnumerateArray().Any(category => GameBananaJson.Int32(category, "_nCategoryCount") > 0);
        foreach (JsonElement category in document.RootElement.EnumerateArray())
        {
            if (!hasNestedCategories)
            {
                AddGameBananaCategory(characters, category);
                continue;
            }

            int nestedCategoryId = GameBananaJson.Int32(category, "_idRow");
            if (GameBananaJson.Int32(category, "_nCategoryCount") <= 0 || nestedCategoryId <= 0)
            {
                continue;
            }

            using JsonDocument nested = await GetJsonAsync(client, $"https://gamebanana.com/apiv11/ModCategory/{nestedCategoryId}/SubCategories", cancellationToken);
            if (nested.RootElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (JsonElement nestedCategory in nested.RootElement.EnumerateArray())
            {
                AddGameBananaCategory(characters, nestedCategory);
            }
        }

        return characters
            .GroupBy(character => character.CategoryId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>The operators on the official Endfield site, completed from the built-in list.</summary>
    public static async Task<List<OnlineCharacterOption>> FetchEndfieldOperatorsAsync(HttpClient client,
        IReadOnlyList<EndfieldCharacterInfo> builtIn, string wikiUrl, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await client.GetAsync(EndfieldOperatorCatalogUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        return EndfieldOperators(EndfieldOperatorCatalogParser.Parse(html), builtIn, wikiUrl);
    }

    /// <summary>
    /// One option per official operator. The built-in entry with a matching name adds the GameBanana
    /// category, aliases and a fallback avatar. The site lists the Endministrator twice: first the male,
    /// then the female variant, which the built-in list names apart.
    /// </summary>
    public static List<OnlineCharacterOption> EndfieldOperators(IReadOnlyList<EndfieldOperatorCatalogEntry> officialOperators,
        IReadOnlyList<EndfieldCharacterInfo> builtIn, string wikiUrl)
    {
        var characters = new List<OnlineCharacterOption>(officialOperators.Count);
        int endministratorVariant = 0;
        foreach (EndfieldOperatorCatalogEntry officialOperator in officialOperators)
        {
            string englishName = officialOperator.EnglishName;
            string chineseName = officialOperator.ChineseName;
            EndfieldCharacterInfo? fallbackCharacter;
            if (string.Equals(englishName, "Endministrator", StringComparison.OrdinalIgnoreCase))
            {
                bool isMaleVariant = endministratorVariant++ == 0;
                englishName = isMaleVariant ? "Endministrator (M)" : "Endministrator (F)";
                chineseName = isMaleVariant ? "管理员 (男)" : "管理员 (女)";
                fallbackCharacter = builtIn.FirstOrDefault(character =>
                    string.Equals(character.EnglishName, englishName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                fallbackCharacter = builtIn.FirstOrDefault(character =>
                    character.AllNames.Any(name =>
                        string.Equals(name, englishName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, chineseName, StringComparison.OrdinalIgnoreCase)));
            }

            List<string> aliases = new[] { englishName, chineseName, officialOperator.EnglishName, officialOperator.ChineseName }
                .Concat(fallbackCharacter?.AllNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            characters.Add(new OnlineCharacterOption
            {
                Key = englishName,
                DisplayNameZh = chineseName,
                DisplayNameEn = englishName,
                CategoryId = fallbackCharacter?.GameBananaCategoryId ?? string.Empty,
                AvatarUrl = string.IsNullOrWhiteSpace(officialOperator.AvatarUrl) ? fallbackCharacter?.AvatarUrl : officialOperator.AvatarUrl,
                WikiUrl = wikiUrl,
                Aliases = aliases,
                IsWikiCharacter = true,
                WikiOrder = Math.Max(0, officialOperator.Order - 1)
            });
        }

        return characters;
    }

    /// <summary>Character names from one HoYoWiki language, paged until the wiki's total is reached.</summary>
    public static async Task<List<HoYoWikiLocalizedCharacter>> FetchHoYoWikiNamesAsync(HttpClient client, WikiCatalogDescriptor descriptor,
        string language, CancellationToken cancellationToken = default)
    {
        const int pageSize = 50;
        const int maxPages = 10;
        var characters = new List<HoYoWikiLocalizedCharacter>();
        var knownEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int page = 1; page <= maxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{HoYoWikiApiBaseUrl}/get_entry_page_list");
            request.Headers.TryAddWithoutValidation("x-rpc-language", language);
            request.Headers.TryAddWithoutValidation("x-rpc-wiki_app", descriptor.HoYoWikiApp);
            request.Headers.Referrer = new Uri("https://wiki.hoyolab.com/");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    filters = Array.Empty<object>(),
                    menu_id = descriptor.HoYoWikiCharacterMenuId,
                    page_num = page,
                    page_size = pageSize,
                    use_es = true
                }),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("data", out JsonElement data)
                || !data.TryGetProperty("list", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            int returnedCount = 0;
            foreach (JsonElement item in list.EnumerateArray())
            {
                returnedCount++;
                string entryPageId = GameBananaJson.Text(item, "entry_page_id")?.Trim() ?? string.Empty;
                string name = GameBananaJson.Text(item, "name")?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(entryPageId) && !string.IsNullOrWhiteSpace(name) && knownEntryIds.Add(entryPageId))
                {
                    characters.Add(new HoYoWikiLocalizedCharacter(entryPageId, name));
                }
            }

            int totalCount = 0;
            if (data.TryGetProperty("total", out JsonElement total))
            {
                if (total.ValueKind == JsonValueKind.Number)
                {
                    total.TryGetInt32(out totalCount);
                }
                else if (total.ValueKind == JsonValueKind.String)
                {
                    int.TryParse(total.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out totalCount);
                }
            }

            if (returnedCount == 0
                || (totalCount > 0 && characters.Count >= totalCount)
                || (totalCount == 0 && returnedCount < pageSize))
            {
                break;
            }
        }

        return characters;
    }

    /// <summary>
    /// Gives each character the official English name of the HoYoWiki entry whose Chinese name matches
    /// one of its names; the two language lists are paired by entry id.
    /// </summary>
    public static void ApplyEnglishNames(IEnumerable<OnlineCharacterOption> characters,
        IEnumerable<HoYoWikiLocalizedCharacter> chineseCharacters, IEnumerable<HoYoWikiLocalizedCharacter> englishCharacters)
    {
        Dictionary<string, string> englishByEntryId = englishCharacters
            .Where(item => !string.IsNullOrWhiteSpace(item.EntryPageId) && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.EntryPageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
        var englishByChineseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (HoYoWikiLocalizedCharacter chineseCharacter in chineseCharacters)
        {
            string normalizedChineseName = OnlineCharacterCatalogMatcher.NormalizeLookup(chineseCharacter.Name);
            if (englishByEntryId.TryGetValue(chineseCharacter.EntryPageId, out string? englishName) && !string.IsNullOrWhiteSpace(normalizedChineseName))
            {
                englishByChineseName[normalizedChineseName] = englishName;
            }
        }

        foreach (OnlineCharacterOption character in characters)
        {
            string? officialEnglishName = new[] { character.DisplayNameZh, character.Key }
                .Concat(character.Aliases)
                .Select(OnlineCharacterCatalogMatcher.NormalizeLookup)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => englishByChineseName.TryGetValue(name, out string? englishName) ? englishName : null)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            if (string.IsNullOrWhiteSpace(officialEnglishName))
            {
                continue;
            }

            character.DisplayNameEn = officialEnglishName.Trim();
            if (!character.Aliases.Contains(character.DisplayNameEn, StringComparer.OrdinalIgnoreCase))
            {
                character.Aliases.Add(character.DisplayNameEn);
            }
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static void AddGameBananaCategory(List<OnlineCharacterOption> characters, JsonElement category)
    {
        string name = GameBananaJson.Text(category, "_sName")?.Trim() ?? string.Empty;
        int numericCategoryId = GameBananaJson.Int32(category, "_idRow");
        string categoryId = numericCategoryId > 0
            ? numericCategoryId.ToString(CultureInfo.InvariantCulture)
            : GameBananaJson.TrailingNumericId(GameBananaJson.Text(category, "_sUrl")?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(categoryId))
        {
            return;
        }

        characters.Add(new OnlineCharacterOption
        {
            Key = name,
            DisplayNameZh = name,
            DisplayNameEn = name,
            CategoryId = categoryId,
            AvatarUrl = GameBananaJson.Text(category, "_sIconUrl")?.Trim() ?? string.Empty,
            Aliases = [name]
        });
    }
}
