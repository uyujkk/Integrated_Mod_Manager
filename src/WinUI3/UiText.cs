using IntegratedModManager.Core.Infrastructure;

namespace ModFolderCopier.WinUI;

// UI text lives in Strings/zh-CN.json and Strings/en-US.json, embedded in this
// assembly. Tests check that both files have the same keys and placeholders and
// that every key used in code exists.
internal static class UiText
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ChineseTexts = new(() => Load("zh-CN"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> EnglishTexts = new(() => Load("en-US"));

    public static string Chinese(string key) => ChineseTexts.Value.TryGetValue(key, out string? text) ? text : key;

    public static string English(string key) => EnglishTexts.Value.TryGetValue(key, out string? text) ? text : key;

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        string resourceName = $"ModFolderCopier.WinUI.Strings.{language}.json";
        using Stream stream = typeof(UiText).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Missing UI text resource " + resourceName);
        using var reader = new StreamReader(stream);
        return LocalizedTextTable.Parse(reader.ReadToEnd());
    }
}
