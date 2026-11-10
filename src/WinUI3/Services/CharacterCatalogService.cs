using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Data;

namespace ModFolderCopier.WinUI;

/// <summary>A catalog was read or fetched for this key (the page shows it when the key is still its own).</summary>
internal sealed class CharacterCatalogLoadedEventArgs(string key, List<OnlineCharacterOption> characters) : EventArgs
{
    public string Key { get; } = key;

    public List<OnlineCharacterOption> Characters { get; } = characters;
}

/// <summary>
/// The character catalog (stage J finish, step 3, moved unchanged from the Online page): which characters a repository's
/// game has, with Chinese and English names, wiki order and GameBanana categories, cached for a day in the local index
/// and in cache\online-characters. The page shows a loaded catalog through <see cref="Loaded"/>; the dashboard,
/// workspace avatars and favourites read the same catalog.
/// </summary>
internal sealed class CharacterCatalogService(OnlineSourceService source)
{
    private readonly string _cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-characters");
    private readonly HashSet<string> _loadingKeys = new(StringComparer.Ordinal);
    private HttpClient _http = null!;
    private AppDataStore _store = null!;
    private Action<string, Exception> _log = (_, _) => { };
    private Func<WorkspaceRepository, string?> _presetWikiUrl = _ => null;

    /// <summary>Called once the window has made its HTTP client and local index.</summary>
    public void Attach(HttpClient http, AppDataStore store, Action<string, Exception> log, Func<WorkspaceRepository, string?> presetWikiUrl)
    {
        _http = http;
        _store = store;
        _log = log;
        _presetWikiUrl = presetWikiUrl;
    }

    public event EventHandler<CharacterCatalogLoadedEventArgs>? Loaded;

    private Task<string> TranslateChunkAsync(string text, string targetLanguage)
        => TextTranslationClient.TranslateAsync(_http, text, targetLanguage);

    public string CatalogKey(WorkspaceRepository? repository)
    {
        return repository is null
            ? string.Empty
            : $"wiki-monthly-v4|{repository.Id}|wiki={repository.WikiUrl.Trim()}";
    }

    private string CacheFile(string catalogKey)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalogKey))).ToLowerInvariant()[..16];
        return Path.Combine(_cachePath, $"{key}.json");
    }

    // A cached catalog older than this is shown at once and refreshed in the background, so newly
    // released characters (and their avatars) appear within a day. Refreshing the online page reloads now.
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(1);

    public async Task<OnlineCharacterCatalogCacheSnapshot?> TryReadCacheAsync(string catalogKey)
    {
        CachedValue<OnlineCharacterCatalogCacheEntry>? sqliteEntry = _store.TryReadCache<OnlineCharacterCatalogCacheEntry>(
            "character-catalog",
            catalogKey,
            FreshFor,
            allowExpired: true);
        if (sqliteEntry?.Value.Characters is { Count: > 0 } storedCharacters)
        {
            return new OnlineCharacterCatalogCacheSnapshot(
                storedCharacters,
                sqliteEntry.CachedAtUtc,
                !sqliteEntry.IsExpired);
        }

        try
        {
            string cacheFile = CacheFile(catalogKey);
            if (!File.Exists(cacheFile))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(cacheFile);
            OnlineCharacterCatalogCacheEntry? entry = JsonSerializer.Deserialize<OnlineCharacterCatalogCacheEntry>(json);
            DateTimeOffset checkedAt = entry is not null && entry.CachedAtUtc > DateTimeOffset.MinValue
                ? entry.CachedAtUtc
                : new DateTimeOffset(File.GetLastWriteTimeUtc(cacheFile));
            return entry?.Characters is { Count: > 0 } characters
                ? new OnlineCharacterCatalogCacheSnapshot(
                    characters,
                    checkedAt,
                    DateTimeOffset.UtcNow - checkedAt <= FreshFor)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteCacheAsync(
        string catalogKey,
        List<OnlineCharacterOption> characters,
        bool preserveWikiCheckedAt = false)
    {
        try
        {
            Directory.CreateDirectory(_cachePath);
            string cacheFile = CacheFile(catalogKey);
            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            if (preserveWikiCheckedAt && File.Exists(cacheFile))
            {
                try
                {
                    string existingJson = await File.ReadAllTextAsync(cacheFile);
                    OnlineCharacterCatalogCacheEntry? existingEntry = JsonSerializer.Deserialize<OnlineCharacterCatalogCacheEntry>(existingJson);
                    if (existingEntry?.CachedAtUtc > DateTimeOffset.MinValue)
                    {
                        checkedAt = existingEntry.CachedAtUtc;
                    }
                }
                catch (Exception ex)
                {
                    _log("Read existing character catalog cache", ex);
                }
            }

            var entry = new OnlineCharacterCatalogCacheEntry
            {
                CachedAtUtc = checkedAt,
                Characters = characters
            };
            _store.WriteCache("character-catalog", catalogKey, entry, checkedAt);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            _log("Write character catalog cache", ex);
        }
    }

    // Each catalog request gets this long, then one more try; see CharacterCatalogClient.WithRetryAsync.
    private static readonly TimeSpan CharacterCatalogRequestTimeout = TimeSpan.FromSeconds(20);

    // The repository's own wiki link, else its game's built-in one (found by category or game name).
    private WikiCatalogDescriptor? GetWikiCatalogDescriptor(WorkspaceRepository repository)
        => CharacterCatalogClient.Describe(repository.WikiUrl,
            _presetWikiUrl(repository));

    private Task<T> RequestCharacterCatalogAsync<T>(string operation, Func<CancellationToken, Task<T>> request)
        => CharacterCatalogClient.WithRetryAsync(request, CharacterCatalogRequestTimeout,
            ex => _log(operation + ", trying again", ex));

    private async Task<List<OnlineCharacterOption>> FetchWikiCharacterCatalogAsync(WorkspaceRepository repository)
    {
        if (source.IsEndfield(repository))
        {
            return await FetchEndfieldCharacterCatalogAsync(repository);
        }

        return GetWikiCatalogDescriptor(repository) is { } descriptor
            ? await RequestCharacterCatalogAsync("Load wiki character catalog",
                token => CharacterCatalogClient.FetchWikiCharactersAsync(_http, descriptor, repository.WikiUrl, token))
            : [];
    }

    private Task<List<OnlineCharacterOption>> FetchEndfieldCharacterCatalogAsync(WorkspaceRepository repository)
        => RequestCharacterCatalogAsync("Load Endfield operator catalog",
            token => CharacterCatalogClient.FetchEndfieldOperatorsAsync(_http, EndfieldCharacterCatalog.BuiltIn,
                string.IsNullOrWhiteSpace(repository.WikiUrl) ? OnlineSourceRules.EndfieldOfficialWikiUrl : repository.WikiUrl, token));

    private Task<List<OnlineCharacterOption>> FetchGameBananaCharacterCatalogAsync(string categoryId)
        => RequestCharacterCatalogAsync("Load GameBanana character categories",
            token => CharacterCatalogClient.FetchGameBananaCharactersAsync(_http, categoryId, token));

    private async Task ApplyOfficialHoYoWikiEnglishNamesAsync(
        WorkspaceRepository repository,
        List<OnlineCharacterOption> characters)
    {
        WikiCatalogDescriptor? descriptor = GetWikiCatalogDescriptor(repository);
        if (descriptor is null
            || string.IsNullOrWhiteSpace(descriptor.HoYoWikiApp)
            || string.IsNullOrWhiteSpace(descriptor.HoYoWikiCharacterMenuId))
        {
            return;
        }

        try
        {
            // Fetch sequentially to keep the monthly Wiki refresh gentle and separate from mod loading.
            List<HoYoWikiLocalizedCharacter> chineseCharacters = await RequestCharacterCatalogAsync("Load HoYoWiki names",
                token => CharacterCatalogClient.FetchHoYoWikiNamesAsync(_http, descriptor, "zh-cn", token));
            List<HoYoWikiLocalizedCharacter> englishCharacters = await RequestCharacterCatalogAsync("Load HoYoWiki names",
                token => CharacterCatalogClient.FetchHoYoWikiNamesAsync(_http, descriptor, "en-us", token));
            CharacterCatalogClient.ApplyEnglishNames(characters, chineseCharacters, englishCharacters);
        }
        catch (Exception ex)
        {
            // The existing Chinese catalog and translation fallback remain usable when HoYoWiki is unavailable.
            _log("Load HoYoWiki names", ex);
        }
    }

    private async Task TranslateWikiCharacterOptionsAsync(List<OnlineCharacterOption> characters)
    {
        List<OnlineCharacterOption> unresolvedCharacters = characters
            .Where(character => string.IsNullOrWhiteSpace(character.DisplayNameEn)
                || string.Equals(
                    OnlineCharacterCatalogMatcher.NormalizeLookup(character.DisplayNameEn),
                    OnlineCharacterCatalogMatcher.NormalizeLookup(character.DisplayNameZh),
                    StringComparison.Ordinal))
            .ToList();
        if (unresolvedCharacters.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> englishNames = await TranslateWikiCharacterNamesAsync(
            unresolvedCharacters.Select(character => character.DisplayNameZh).ToList());
        for (int index = 0; index < unresolvedCharacters.Count && index < englishNames.Count; index++)
        {
            string englishName = englishNames[index].Trim();
            if (string.IsNullOrWhiteSpace(englishName))
            {
                continue;
            }

            unresolvedCharacters[index].DisplayNameEn = englishName;
            if (!unresolvedCharacters[index].Aliases.Contains(englishName, StringComparer.OrdinalIgnoreCase))
            {
                unresolvedCharacters[index].Aliases.Add(englishName);
            }
        }
    }

    private async Task<IReadOnlyList<string>> TranslateWikiCharacterNamesAsync(IReadOnlyList<string> names)
    {
        string[] translatedNames = names.ToArray();
        const int batchSize = 16;
        const string separator = " [[[IMM_NAME]]] ";

        for (int start = 0; start < names.Count; start += batchSize)
        {
            int count = Math.Min(batchSize, names.Count - start);
            string[] batch = names.Skip(start).Take(count).ToArray();
            try
            {
                string translatedBatch = await TranslateChunkAsync(string.Join(separator, batch), "en");
                string[] parts = translatedBatch.Split("[[[IMM_NAME]]]", StringSplitOptions.TrimEntries);
                if (parts.Length == count)
                {
                    for (int index = 0; index < count; index++)
                    {
                        translatedNames[start + index] = parts[index];
                    }
                    continue;
                }

                Task<string>[] translations = batch.Select(name => TranslateChunkAsync(name, "en")).ToArray();
                string[] fallbackResults = await Task.WhenAll(translations);
                for (int index = 0; index < count; index++)
                {
                    translatedNames[start + index] = fallbackResults[index];
                }
            }
            catch
            {
                // Chinese names remain available when the optional translation service is unavailable.
            }
        }

        return translatedNames;
    }

    public async Task EnsureAsync(WorkspaceRepository repository, bool forceReload)
    {
        string catalogKey = CatalogKey(repository);
        if (string.IsNullOrWhiteSpace(catalogKey) || !_loadingKeys.Add(catalogKey))
        {
            return;
        }

        try
        {
            if (!forceReload)
            {
                OnlineCharacterCatalogCacheSnapshot? cachedCatalog = await TryReadCacheAsync(catalogKey);
                if (cachedCatalog is not null)
                {
                    bool requiresWikiCatalog = GetWikiCatalogDescriptor(repository) is not null
                        || source.IsEndfield(repository);
                    if (!requiresWikiCatalog || cachedCatalog.Characters.Any(character => character.IsWikiCharacter))
                    {
                        OnlineCharacterCatalogMatcher.ApplyManualMappings(repository.CharacterCategoryMappings, cachedCatalog.Characters);
                        Loaded?.Invoke(this, new CharacterCatalogLoadedEventArgs(catalogKey, cachedCatalog.Characters));
                        if (cachedCatalog.IsFresh)
                        {
                            return;
                        }
                    }
                }
            }

            List<OnlineCharacterOption> wikiCharacters = await FetchWikiCharacterCatalogAsync(repository);
            if ((GetWikiCatalogDescriptor(repository) is not null || source.IsEndfield(repository))
                && wikiCharacters.Count == 0)
            {
                return;
            }

            if (wikiCharacters.Count > 0)
            {
                OnlineCharacterCatalogMatcher.ApplyManualMappings(repository.CharacterCategoryMappings, wikiCharacters);
                Loaded?.Invoke(this, new CharacterCatalogLoadedEventArgs(catalogKey, wikiCharacters));
                await ApplyOfficialHoYoWikiEnglishNamesAsync(repository, wikiCharacters);
                await TranslateWikiCharacterOptionsAsync(wikiCharacters);

                List<OnlineCharacterOption> gameBananaCharacters = [];
                try
                {
                    gameBananaCharacters = await FetchGameBananaCharacterCatalogAsync(
                        source.CategoryId(repository));
                }
                catch (Exception ex)
                {
                    // Wiki names remain usable if the optional category mapping request fails.
                    _log("Load GameBanana character categories", ex);
                }

                List<OnlineCharacterOption> mergedCharacters = OnlineCharacterCatalogMatcher.Merge(
                    wikiCharacters,
                    gameBananaCharacters,
                    repository.CharacterCategoryMappings);
                OnlineCharacterCatalogMatcher.ApplyManualMappings(repository.CharacterCategoryMappings, mergedCharacters);
                await WriteCacheAsync(catalogKey, mergedCharacters);
                Loaded?.Invoke(this, new CharacterCatalogLoadedEventArgs(catalogKey, mergedCharacters));
            }
        }
        catch (Exception ex)
        {
            // The current page still supplies a usable partial character list; the log says why the catalog did not load.
            _log("Load online character catalog", ex);
        }
        finally
        {
            _loadingKeys.Remove(catalogKey);
        }
    }
}
