using System.Text.Json;

namespace IntegratedModManager.Core.Online;

// One record of a GameBanana category listing (apiv11 Mod/Index) as a card. The window
// still resolves the character name for generic categories and localizes a missing author.
public static class GameBananaListingParser
{
    public static OnlineModCard? ParseRecord(JsonElement record, DateTimeOffset now)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        int itemId = GameBananaJson.Int32(record, "_idRow");
        if (itemId <= 0)
        {
            return null;
        }

        string author = string.Empty;
        int authorId = 0;
        if (record.TryGetProperty("_aSubmitter", out JsonElement submitter))
        {
            author = GameBananaJson.Text(submitter, "_sName") ?? string.Empty;
            authorId = GameBananaJson.Int32(submitter, "_idRow");
        }

        string rootCategoryName = record.TryGetProperty("_aRootCategory", out JsonElement rootCategory)
            ? GameBananaJson.Text(rootCategory, "_sName") ?? string.Empty
            : string.Empty;

        string characterName = string.Empty;
        string categoryId = string.Empty;
        if (record.TryGetProperty("_aSubCategory", out JsonElement subCategory))
        {
            characterName = GameBananaJson.Text(subCategory, "_sName") ?? string.Empty;
            categoryId = GameBananaJson.TrailingNumericId(GameBananaJson.Text(subCategory, "_sProfileUrl"));
        }

        long updatedEpoch = GameBananaJson.Int64(record, "_tsDateModified");
        if (updatedEpoch <= 0)
        {
            updatedEpoch = GameBananaJson.Int64(record, "_tsDateUpdated");
        }
        if (updatedEpoch <= 0)
        {
            updatedEpoch = GameBananaJson.Int64(record, "_tsDateAdded");
        }

        int likes = GameBananaJson.Int32(record, "_nLikeCount");
        int views = GameBananaJson.Int32(record, "_nViewCount");
        int downloads = GameBananaJson.Int32(record, "_nDownloadCount");
        return new OnlineModCard
        {
            ItemId = itemId,
            Title = GameBananaJson.Text(record, "_sName") ?? $"Mod {itemId}",
            CharacterName = characterName,
            CategoryId = categoryId,
            RootCategoryName = string.IsNullOrWhiteSpace(rootCategoryName) ? "Skins" : rootCategoryName,
            Author = author,
            AuthorId = authorId,
            Likes = likes,
            Views = views,
            Downloads = downloads,
            HotnessScore = Hotness(likes, views, downloads),
            PreviewUrl = PreviewUrl(record),
            ProfileUrl = GameBananaJson.Text(record, "_sProfileUrl") ?? $"https://gamebanana.com/mods/{itemId}",
            DownloadUrl = $"https://gamebanana.com/mods/download/{itemId}",
            UpdatedAt = GameBananaJson.UnixTime(updatedEpoch, now)
        };
    }

    // 0-10 score weighted towards downloads, on a log scale so a few huge mods do not flatten the rest.
    public static double Hotness(int likes, int views, int downloads)
    {
        double downloadPart = Math.Log10(downloads + 1d) * 0.55d;
        double likePart = Math.Log10(likes + 1d) * 0.30d;
        double viewPart = Math.Log10(views + 1d) * 0.15d;
        double weighted = downloadPart + likePart + viewPart;
        double normalized = Math.Min(10d, weighted * 2d);
        return Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
    }

    private static string? PreviewUrl(JsonElement record)
    {
        if (!record.TryGetProperty("_aPreviewMedia", out JsonElement previewMedia)
            || previewMedia.ValueKind != JsonValueKind.Object
            || !previewMedia.TryGetProperty("_aImages", out JsonElement images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement image in images.EnumerateArray())
        {
            string? baseUrl = GameBananaJson.Text(image, "_sBaseUrl");
            string? fileName = GameBananaJson.Text(image, "_sFile220")
                ?? GameBananaJson.Text(image, "_sFile530")
                ?? GameBananaJson.Text(image, "_sFile100")
                ?? GameBananaJson.Text(image, "_sFile");
            if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(fileName))
            {
                return $"{baseUrl}/{fileName}";
            }
        }

        return null;
    }
}
