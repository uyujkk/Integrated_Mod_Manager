using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IntegratedModManager.Core;

/// <summary>
/// The apiv11 list omits download counts. The legacy Core/Item/Data endpoint returns them for many
/// items in one request (verified: 40 items in ~270 ms, results in request order, a bad ID only
/// yields an error entry), replacing one request per card.
/// </summary>
public static class GameBananaDownloadCounts
{
    // 40 items keep the URL near 2 KB; one display page fits in a single request.
    public const int MaxBatch = 40;

    public static string BatchUrl(IReadOnlyList<int> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count is 0 or > MaxBatch) throw new ArgumentOutOfRangeException(nameof(itemIds));
        var url = new StringBuilder("https://api.gamebanana.com/Core/Item/Data?");
        foreach (int id in itemIds)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(itemIds));
            url.Append("itemtype%5B%5D=Mod&itemid%5B%5D=").Append(id.ToString(CultureInfo.InvariantCulture)).Append("&fields%5B%5D=downloads&");
        }
        return url.Append("return_keys=true&format=json_min").ToString();
    }

    /// <summary>Maps results back by position. A response of the wrong length is rejected rather than misassigned.</summary>
    public static IReadOnlyDictionary<int, int> Parse(string json, IReadOnlyList<int> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != itemIds.Count)
            throw new InvalidDataException("Download count response does not match the requested items.");
        var counts = new Dictionary<int, int>();
        int index = 0;
        foreach (JsonElement entry in root.EnumerateArray())
        {
            int id = itemIds[index++];
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("downloads", out JsonElement value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long downloads))
                counts[id] = (int)Math.Clamp(downloads, 0, int.MaxValue);
        }
        return counts;
    }
}
