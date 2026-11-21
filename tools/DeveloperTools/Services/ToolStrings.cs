using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace IntegratedModManager.DeveloperTools.Services;

/// <summary>
/// The tools' UI text: one embedded file per language (<c>Strings/&lt;code&gt;.json</c>), like the app's. A key missing
/// from a language falls back to English, then to the key itself.
/// </summary>
internal sealed class ToolStrings
{
    public const string Fallback = "en-US";
    private readonly Dictionary<string, Dictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);

    public ToolStrings()
    {
        Assembly assembly = typeof(ToolStrings).Assembly;
        const string prefix = "DeveloperTools.Strings.";
        foreach (string name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            _languages[name[prefix.Length..^".json".Length]] = texts;
        }
    }

    /// <summary>Language codes with their own names, for the language picker.</summary>
    public IReadOnlyList<(string Code, string Name)> Languages
        => [.. _languages.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value.TryGetValue("Language.Name", out string? name) ? name : pair.Key))];

    public string Current { get; private set; } = Fallback;

    public void Use(string? code) => Current = code is not null && _languages.ContainsKey(code) ? code : Fallback;

    /// <summary>The system's language when the tools have a file for it (zh-* uses zh-CN), otherwise English.</summary>
    public string SystemDefault()
    {
        string name = CultureInfo.CurrentUICulture.Name;
        if (_languages.ContainsKey(name)) return name;
        string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return _languages.Keys.FirstOrDefault(code => code.StartsWith(two + "-", StringComparison.OrdinalIgnoreCase)) ?? Fallback;
    }

    public string T(string key, params object?[] values)
    {
        string text = _languages.TryGetValue(Current, out var current) && current.TryGetValue(key, out string? found) ? found
            : _languages.TryGetValue(Fallback, out var english) && english.TryGetValue(key, out string? fallback) ? fallback
            : key;
        return values.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, values);
    }
}
