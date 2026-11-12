using System.Text.Json;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mod categories of each repository's game (issue #129): read from GameBanana when first needed, kept for a day
/// in cache\online-categories and in memory. Without them (no network, a rate limit, a site change) the Online page
/// simply has no category bar and works as before.
/// </summary>
internal sealed class OnlineCategoryService
{
    private readonly string _cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-categories");
    private readonly Dictionary<string, OnlineCategoryIndex> _indexes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private HttpClient _http = null!;
    private Action<string, Exception> _log = (_, _) => { };

    /// <summary>Called once the window has made its HTTP client.</summary>
    public void Attach(HttpClient http, Action<string, Exception> log)
    {
        _http = http;
        _log = log;
    }

    /// <summary>The categories for a repository category or a subcategory list changed.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>The categories read so far for this repository category, fresh or not; null when none.</summary>
    public OnlineCategoryIndex? Current(string? repositoryCategoryId)
        => !string.IsNullOrWhiteSpace(repositoryCategoryId) && _indexes.TryGetValue(repositoryCategoryId, out OnlineCategoryIndex? index) ? index : null;

    /// <summary>
    /// Makes sure the categories are known: from memory or the cache while fresh, otherwise from the site. A failure
    /// keeps whatever was known (an expired cache is better than nothing) and is not an error the user has to see.
    /// </summary>
    public async Task<OnlineCategoryIndex?> EnsureAsync(string? repositoryCategoryId, bool forceReload = false)
    {
        if (string.IsNullOrWhiteSpace(repositoryCategoryId) || !repositoryCategoryId.All(char.IsAsciiDigit)) return null;
        OnlineCategoryIndex? known = Current(repositoryCategoryId);
        if (!forceReload && known is not null && known.IsFresh(DateTimeOffset.UtcNow)) return known;
        if (known is null && await ReadCacheAsync(repositoryCategoryId) is { } cached)
        {
            _indexes[repositoryCategoryId] = known = cached;
            Changed?.Invoke(this, repositoryCategoryId);
            if (!forceReload && cached.IsFresh(DateTimeOffset.UtcNow)) return cached;
        }
        if (!_loading.Add(repositoryCategoryId)) return known;
        try
        {
            OnlineCategoryIndex? read = await GameBananaCategoryTreeClient.ReadAsync(_http, repositoryCategoryId, DateTimeOffset.UtcNow);
            if (read is null) return known;
            // Subcategories of other top-level categories read earlier stay until the next read needs them.
            if (known is not null)
            {
                foreach (var (topId, children) in known.Subcategories)
                    if (!read.Subcategories.ContainsKey(topId) && read.TopCategories.Any(top => top.Id == topId)) read.Subcategories[topId] = children;
            }
            _indexes[repositoryCategoryId] = read;
            await WriteCacheAsync(read);
            Changed?.Invoke(this, repositoryCategoryId);
            return read;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            // Offline, throttled or an unexpected answer: the page keeps working without the category bar.
            if (ex is JsonException) _log("Read online categories", ex);
            return known;
        }
        finally { _loading.Remove(repositoryCategoryId); }
    }

    /// <summary>The direct subcategories of a top-level category, read the first time it is chosen.</summary>
    public async Task<List<OnlineCategory>> EnsureSubcategoriesAsync(string repositoryCategoryId, string topId)
    {
        if (Current(repositoryCategoryId) is not { } index || index.TopCategories.FirstOrDefault(top => top.Id == topId) is not { } top) return [];
        if (index.Subcategories.TryGetValue(topId, out List<OnlineCategory>? known)) return known;
        if (top.SubcategoryCount <= 0) return [];
        string key = repositoryCategoryId + "/" + topId;
        if (!_loading.Add(key)) return [];
        try
        {
            List<OnlineCategory> children = await GameBananaCategoryTreeClient.ReadSubcategoriesAsync(_http, topId);
            index.Subcategories[topId] = children;
            await WriteCacheAsync(index);
            Changed?.Invoke(this, repositoryCategoryId);
            return children;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            if (ex is JsonException) _log("Read online subcategories", ex);
            return [];
        }
        finally { _loading.Remove(key); }
    }

    private string CacheFile(string repositoryCategoryId) => Path.Combine(_cachePath, repositoryCategoryId + ".json");

    private async Task<OnlineCategoryIndex?> ReadCacheAsync(string repositoryCategoryId)
    {
        try
        {
            string file = CacheFile(repositoryCategoryId);
            if (!File.Exists(file)) return null;
            OnlineCategoryIndex? index = JsonSerializer.Deserialize<OnlineCategoryIndex>(await File.ReadAllTextAsync(file));
            return index is { TopCategories.Count: > 0 } && index.RepositoryCategoryId == repositoryCategoryId ? index : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _log("Read online category cache", ex);
            return null;
        }
    }

    private async Task WriteCacheAsync(OnlineCategoryIndex index)
    {
        try
        {
            Directory.CreateDirectory(_cachePath);
            string file = CacheFile(index.RepositoryCategoryId);
            string temporary = file + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(index));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log("Write online category cache", ex);
        }
    }
}
