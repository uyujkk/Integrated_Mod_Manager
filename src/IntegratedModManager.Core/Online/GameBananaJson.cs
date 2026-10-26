using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Online;

// Lenient readers for GameBanana API JSON, where numbers may arrive as numbers or
// strings (sometimes with thousands separators) and fields are often missing.
public static partial class GameBananaJson
{
    public static string? Text(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? value.ToString()
                : null;
    }

    public static long Int64(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out JsonElement value)
            ? ReadInt64(value)
            : 0;
    }

    public static int Int32(JsonElement element, string propertyName)
    {
        return (int)Math.Clamp(Int64(element, propertyName), int.MinValue, int.MaxValue);
    }

    public static bool Boolean(JsonElement element, string propertyName)
    {
        return bool.TryParse(Text(element, propertyName), out bool result) && result;
    }

    // Numbers and numeric strings; fractions round half away from zero, out-of-range values clamp.
    public static long ReadInt64(JsonElement value)
    {
        string? text = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
        text = text?.Replace(",", string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
        {
            return integer;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
        {
            return 0;
        }
        return number >= long.MaxValue ? long.MaxValue
            : number <= long.MinValue ? long.MinValue
            : (long)Math.Round(number, MidpointRounding.AwayFromZero);
    }

    // The numeric id at the end of a profile URL such as https://gamebanana.com/mods/cats/12345.
    public static string TrailingNumericId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        Match match = TrailingIdRegex().Match(url);
        return match.Success ? match.Groups["id"].Value : string.Empty;
    }

    // GameBanana timestamps are Unix seconds; missing or invalid ones use the fallback.
    public static DateTimeOffset UnixTime(long seconds, DateTimeOffset fallback)
    {
        if (seconds <= 0 || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return fallback;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
    }

    [GeneratedRegex(@"(?<id>\d+)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingIdRegex();
}
