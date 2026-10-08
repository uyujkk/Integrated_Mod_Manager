using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core;

public sealed record GameBananaDetails
{
    [JsonRequired] public string Summary { get; init; } = string.Empty;
    [JsonRequired] public string Description { get; init; } = string.Empty;
    [JsonRequired] public string[] ImageUrls { get; init; } = [];
    [JsonIgnore] public bool IsValid => Summary is not null && Description is not null
        && ImageUrls is not null && ImageUrls.All(GameBananaMetadataParser.IsWebUrl);
    public GameBananaDetails Copy() => this with { ImageUrls = ImageUrls.ToArray() };
}

public sealed record GameBananaModMetadata
{
    public int ItemId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string CharacterName { get; init; } = string.Empty;
    public int CategoryId { get; init; }
    public string RootCategoryName { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public int Likes { get; init; }
    public int Views { get; init; }
    public int Downloads { get; init; }
    public long UpdatedEpoch { get; init; }
    public string? PreviewUrl { get; init; }
    public string ProfileUrl { get; init; } = string.Empty;
    public string? FallbackDownloadUrl { get; init; }
    public bool HasUpdates { get; init; }
    public IReadOnlyList<OnlineDownloadCandidate> DownloadFiles { get; init; } = [];
}

public static class GameBananaMetadataParser
{
    public static GameBananaDetails ParseDetails(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = GetData(document.RootElement);
        var images = new List<string>();
        string? preview = WebUrl(Text(data, "Preview().sSubFeedImageUrl()"));
        if (preview is not null) images.Add(preview);
        if (data.TryGetProperty("screenshots", out JsonElement screenshots))
        {
            if (screenshots.ValueKind == JsonValueKind.String)
            {
                try
                {
                    using JsonDocument nested = JsonDocument.Parse(screenshots.GetString() ?? "[]");
                    AddScreenshots(nested.RootElement, images);
                }
                catch (JsonException) { } // Optional malformed screenshots do not lose valid text.
            }
            else AddScreenshots(screenshots, images);
        }
        return new GameBananaDetails
        {
            Summary = PlainText(Text(data, "description") ?? string.Empty),
            Description = PlainText(Text(data, "text") ?? string.Empty),
            ImageUrls = images.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public static GameBananaModMetadata ParseMod(string json, int itemId)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = GetData(document.RootElement);
        var files = new List<OnlineDownloadCandidate>();
        long fileDownloads = 0;
        if (data.TryGetProperty("Files().aFiles()", out JsonElement collection))
        {
            if (collection.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty property in collection.EnumerateObject()) AddFile(property.Value, property.Name, files, ref fileDownloads);
            else if (collection.ValueKind == JsonValueKind.Array)
                foreach (JsonElement file in collection.EnumerateArray()) AddFile(file, string.Empty, files, ref fileDownloads);
        }
        int downloads = Count(data, "downloads");
        return new GameBananaModMetadata
        {
            ItemId = itemId, Title = Text(data, "name") ?? $"Mod {itemId}",
            CharacterName = Text(data, "Category().name") ?? string.Empty,
            CategoryId = Count(data, "catid"), RootCategoryName = Text(data, "RootCategory().name") ?? string.Empty,
            Author = Text(data, "Owner().name") ?? string.Empty, Likes = Count(data, "likes"), Views = Count(data, "views"),
            Downloads = downloads > 0 ? downloads : (int)Math.Min(int.MaxValue, fileDownloads),
            UpdatedEpoch = Number(data, "mdate"), PreviewUrl = WebUrl(Text(data, "Preview().sSubFeedImageUrl()")),
            ProfileUrl = WebUrl(Text(data, "Url().sProfileUrl()")) ?? $"https://gamebanana.com/mods/{itemId}",
            FallbackDownloadUrl = WebUrl(Text(data, "Url().sDownloadUrl()")),
            HasUpdates = Boolean(data, "Updates().bSubmissionHasUpdates()"),
            DownloadFiles = OnlineDownloadSelectionPolicy.OrderForManualSelection(files)
        };
    }

    private static JsonElement GetData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out JsonElement wrapped)) root = wrapped;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The metadata response is not an object.");
        // Error objects must not become a successful empty Mod and enter the cache.
        if (!root.EnumerateObject().Any(property => property.Name is "name" or "description" or "text" or "screenshots"
            or "Category().name" or "Files().aFiles()" or "Preview().sSubFeedImageUrl()"))
            throw new InvalidDataException("The metadata response has no recognized fields.");
        return root;
    }

    private static void AddScreenshots(JsonElement screenshots, List<string> images)
    {
        if (screenshots.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement image in screenshots.EnumerateArray())
        {
            string? file = Text(image, "_sFile800") ?? Text(image, "_sFile530") ?? Text(image, "_sFile");
            if (string.IsNullOrWhiteSpace(file)) continue;
            string? url = WebUrl(file);
            if (url is null && !file.Contains('/') && !file.Contains('\\') && !file.Contains(':') && file is not ("." or ".."))
                url = "https://images.gamebanana.com/img/ss/mods/" + Uri.EscapeDataString(file);
            if (url is not null) images.Add(url);
        }
    }

    private static void AddFile(JsonElement file, string fallbackId, List<OnlineDownloadCandidate> files, ref long downloads)
    {
        if (file.ValueKind != JsonValueKind.Object) return;
        downloads = Math.Min(int.MaxValue, downloads + Count(file, "_nDownloadCount"));
        string? url = WebUrl(Text(file, "_sDownloadUrl"));
        if (url is null) return;
        string name = Text(file, "_sFile") ?? string.Empty;
        DateTimeOffset added = DateTimeOffset.MinValue;
        long epoch = Number(file, "_tsDateAdded");
        if (epoch > 0)
            try { added = DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime(); }
            catch (ArgumentOutOfRangeException) { }
        files.Add(new OnlineDownloadCandidate
        {
            FileId = Text(file, "_idRow") ?? fallbackId, FileName = name, DownloadUrl = url,
            FileSizeBytes = Math.Max(0, Number(file, "_nFilesize")), AddedAt = added,
            IsArchived = Boolean(file, "_bIsArchived"), IsSupportedArchive = IsArchive(name),
            Version = Text(file, "_sVersion") ?? string.Empty, Description = Text(file, "_sDescription") ?? string.Empty
        });
    }

    private static bool IsArchive(string name) => Path.GetExtension(name).ToLowerInvariant()
        is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" or ".zst" or ".zipx" or ".cab";
    public static bool IsWebUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https";
    private static string? WebUrl(string? value) => IsWebUrl(value) ? value : null;
    private static string? Text(JsonElement data, string key) => data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(key, out JsonElement field) && field.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
        ? field.ToString() : null;
    private static int Count(JsonElement data, string key) => (int)Math.Clamp(Number(data, key), 0, int.MaxValue);
    private static long Number(JsonElement data, string key)
    {
        string? text = Text(data, key)?.Replace(",", string.Empty).Trim();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) return integer;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number)) return 0;
        return number >= long.MaxValue ? long.MaxValue : number <= long.MinValue ? long.MinValue : (long)Math.Round(number, MidpointRounding.AwayFromZero);
    }
    private static bool Boolean(JsonElement data, string key) => bool.TryParse(Text(data, key), out bool result) && result;

    public static string PlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Bound regex processing as well as HTTP bytes; preserve the existing formatting rules.
        static string Replace(string text, string pattern, string replacement, RegexOptions options = RegexOptions.None)
            => Regex.Replace(text, pattern, replacement, options, TimeSpan.FromSeconds(2));
        string text = Replace(value, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Replace(text, @"<\s*/?(p|div|h\d|li|ul|ol)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Replace(text, "<[^>]+>", " "));
        text = Replace(text, @"[ \t]+\n", "\n");
        text = Replace(text, @"\n{3,}", "\n\n");
        return Replace(text, @"[ \t]{2,}", " ").Trim();
    }
}
