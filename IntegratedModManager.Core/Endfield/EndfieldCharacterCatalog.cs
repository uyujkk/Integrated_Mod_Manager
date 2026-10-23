using System.Text.Json;

namespace IntegratedModManager.Core.Endfield;

public sealed class EndfieldCharacterInfo
{
    public EndfieldCharacterInfo(
        string englishName,
        string chineseName,
        string gameBananaCategoryId,
        string avatarUrl,
        params string[] aliases)
    {
        EnglishName = englishName;
        ChineseName = chineseName;
        GameBananaCategoryId = gameBananaCategoryId;
        AvatarUrl = avatarUrl;
        AllNames = [englishName, chineseName, .. aliases];
    }

    public string EnglishName { get; }

    public string ChineseName { get; }

    public string GameBananaCategoryId { get; }

    public string AvatarUrl { get; }

    public string[] AllNames { get; }
}

// The built-in operator list lives in endfield-characters.json, embedded in this
// assembly. Adding an operator means editing that file; the order is the wiki order.
public static class EndfieldCharacterCatalog
{
    private const string ResourceName = "IntegratedModManager.Core.Endfield.endfield-characters.json";
    private static readonly Lazy<IReadOnlyList<EndfieldCharacterInfo>> BuiltInCharacters = new(LoadBuiltIn);

    public static IReadOnlyList<EndfieldCharacterInfo> BuiltIn => BuiltInCharacters.Value;

    public static IReadOnlyList<EndfieldCharacterInfo> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The character list must be a JSON array.");
        }

        var characters = new List<EndfieldCharacterInfo>();
        var englishNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            int index = characters.Count;
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Character {index} is not a JSON object.");
            }

            string englishName = RequiredString(item, "englishName", index);
            string chineseName = RequiredString(item, "chineseName", index);
            string categoryId = OptionalString(item, "gameBananaCategoryId", index);
            string avatarUrl = OptionalString(item, "avatarUrl", index);
            if (categoryId.Any(character => !char.IsAsciiDigit(character)))
            {
                throw new InvalidDataException($"Character {englishName} has a non-numeric GameBanana category id.");
            }
            if (avatarUrl.Length > 0
                && (!Uri.TryCreate(avatarUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidDataException($"Character {englishName} has an avatar URL that is not an absolute https URL.");
            }
            if (!englishNames.Add(englishName))
            {
                throw new InvalidDataException($"Character {englishName} is listed more than once.");
            }

            string[] aliases = [];
            if (item.TryGetProperty("aliases", out JsonElement aliasElement))
            {
                if (aliasElement.ValueKind != JsonValueKind.Array
                    || aliasElement.EnumerateArray().Any(alias => alias.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(alias.GetString())))
                {
                    throw new InvalidDataException($"Character {englishName} has aliases that are not a list of names.");
                }
                aliases = aliasElement.EnumerateArray().Select(alias => alias.GetString()!.Trim()).ToArray();
            }

            characters.Add(new EndfieldCharacterInfo(englishName, chineseName, categoryId, avatarUrl, aliases));
        }

        return characters;
    }

    private static IReadOnlyList<EndfieldCharacterInfo> LoadBuiltIn()
    {
        using Stream stream = typeof(EndfieldCharacterCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The built-in character list is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static string RequiredString(JsonElement item, string name, int index)
    {
        string value = OptionalString(item, name, index);
        return value.Length > 0 ? value : throw new InvalidDataException($"Character {index} is missing {name}.");
    }

    private static string OptionalString(JsonElement item, string name, int index)
    {
        if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : throw new InvalidDataException($"Character {index} has a non-text {name}.");
    }
}
