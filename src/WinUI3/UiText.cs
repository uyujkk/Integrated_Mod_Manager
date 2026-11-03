using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Infrastructure;

namespace ModFolderCopier.WinUI;

// UI text lives in Strings/<language>.json (zh-CN.json, en-US.json, ...), embedded in this
// assembly. Adding a language is adding a file: it shows up in Settings, and any key it does
// not translate falls back to English. Tests check that zh-CN and en-US have the same keys, that
// other files only use English keys and placeholders, and that every key used in code exists.
internal static class UiText
{
    private const string ResourcePrefix = "ModFolderCopier.WinUI.Strings.";
    private const string ResourceSuffix = ".json";

    // The language's own name, shown in the language list.
    public const string LanguageNameKey = "Language.Name";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Tables = new(LoadAll);

    /// <summary>The languages with a file: Chinese and English first, then the others by code.</summary>
    public static IReadOnlyList<string> Languages { get; } = Tables.Value.Keys
        .OrderBy(language => language switch { UiLanguagePolicy.Chinese => 0, UiLanguagePolicy.English => 1, _ => 2 })
        .ThenBy(language => language, StringComparer.Ordinal)
        .ToArray();

    public static string Get(string language, string key)
    {
        if (Tables.Value.TryGetValue(language, out var table) && table.TryGetValue(key, out string? text)) return text;
        return Tables.Value.TryGetValue(UiLanguagePolicy.English, out var english) && english.TryGetValue(key, out string? fallback)
            ? fallback
            : key;
    }

    public static string Chinese(string key) => Get(UiLanguagePolicy.Chinese, key);

    public static string English(string key) => Get(UiLanguagePolicy.English, key);

    // For code outside the window, which passes the window's language.
    public static string Format(string language, string key, params object?[] values)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(language, key), values);

    public static string LanguageName(string language) => Get(language, LanguageNameKey);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadAll()
    {
        var assembly = typeof(UiText).Assembly;
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(ResourceSuffix, StringComparison.Ordinal)))
        {
            string language = resourceName[ResourcePrefix.Length..^ResourceSuffix.Length];
            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            tables[language] = LocalizedTextTable.Parse(reader.ReadToEnd());
        }

        if (!tables.ContainsKey(UiLanguagePolicy.English) || !tables.ContainsKey(UiLanguagePolicy.Chinese))
        {
            throw new InvalidOperationException("The zh-CN and en-US UI text resources are required.");
        }
        return tables;
    }
}
