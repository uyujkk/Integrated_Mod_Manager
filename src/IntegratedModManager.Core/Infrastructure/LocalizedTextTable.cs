using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.Infrastructure;

// One language file of UI text: a flat JSON object of key -> text. Texts that take
// values use composite format placeholders ({0}, {1:F1}); literal braces are doubled.
public static partial class LocalizedTextTable
{
    public static IReadOnlyDictionary<string, string> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A language file must be a JSON object of key and text.");
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"The text for {property.Name} is not a string.");
            }
            if (!texts.TryAdd(property.Name, property.Value.GetString()!))
            {
                throw new InvalidDataException($"The key {property.Name} appears more than once.");
            }
        }

        return texts;
    }

    // Indexes of the placeholders a text uses, ignoring doubled literal braces.
    public static IReadOnlySet<int> PlaceholderIndexes(string text)
    {
        var indexes = new SortedSet<int>();
        foreach (Match match in PlaceholderRegex().Matches(text))
        {
            if (match.Groups["index"].Success)
            {
                indexes.Add(int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return indexes;
    }

    [GeneratedRegex(@"\{\{|\}\}|\{(?<index>\d+)(?:,[^:}]*)?(?::[^}]*)?\}")]
    private static partial Regex PlaceholderRegex();
}
