using System.Net;
using System.Text.Json;

namespace IntegratedModManager.Core.Online;

public sealed class OnlineRateLimitException : HttpRequestException
{
    public OnlineRateLimitException(TimeSpan? retryAfter)
        : base("GameBanana rate limit reached.", null, HttpStatusCode.TooManyRequests)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}

public sealed class OnlineCategoryPageResult
{
    public OnlineCategoryPageResult(List<OnlineModCard> mods, int totalCount, int page, int pageSize)
    {
        Mods = mods;
        TotalCount = totalCount;
        Page = page;
        PageSize = Math.Max(1, pageSize);
    }

    public List<OnlineModCard> Mods { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public DateTimeOffset? CachedAtUtc { get; init; }

    public bool IsFromCache { get; init; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <summary>
/// One page of a GameBanana category listing. Cards come back as GameBanana names them; the window
/// then maps category names to characters and fills display text.
/// </summary>
public static class GameBananaCategoryClient
{
    public static async Task<OnlineCategoryPageResult> GetPageAsync(HttpClient client, string categoryId, int page, int pageSize,
        int authorId, DateTimeOffset now, CancellationToken cancellationToken = default, string? serverSort = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        using HttpResponseMessage response = await client.GetAsync(OnlineBrowsePolicy.CategoryPageUrl(categoryId, page, pageSize, authorId, serverSort), cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new OnlineRateLimitException(response.Headers.RetryAfter?.Delta);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        List<OnlineModCard> mods = [];
        if (root.ValueKind != JsonValueKind.Object) return new OnlineCategoryPageResult(mods, 0, page, pageSize);

        JsonElement metadata = root.TryGetProperty("_aMetadata", out JsonElement metadataElement) ? metadataElement : default;
        int totalCount = GameBananaJson.Int32(metadata, "_nRecordCount");
        int responsePageSize = Math.Max(1, GameBananaJson.Int32(metadata, "_nPerpage"));
        if (!root.TryGetProperty("_aRecords", out JsonElement records) || records.ValueKind != JsonValueKind.Array)
            return new OnlineCategoryPageResult(mods, totalCount, page, responsePageSize);

        foreach (JsonElement record in records.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GameBananaListingParser.ParseRecord(record, now) is { } card) mods.Add(card);
        }
        return new OnlineCategoryPageResult(mods, totalCount, page, responsePageSize);
    }
}
