using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntegratedModManager.Core.DeveloperTools;

public enum LanguageIssueKind
{
    /// <summary>The reference has the key, this language does not (the app shows the English text).</summary>
    Missing,
    /// <summary>This language has a key the reference does not (unused, or a typo).</summary>
    Extra,
    /// <summary>
    /// The text uses a <c>{n}</c> placeholder the reference does not have, which the app cannot fill (a translation may
    /// leave some out: the code passes every argument, and each language picks the ones it needs).
    /// </summary>
    Placeholders,
    /// <summary>The text is empty.</summary>
    Empty
}

public sealed record LanguageIssue(string Language, LanguageIssueKind Kind, string Key, string Detail);

public sealed record LanguageFileSummary(string Language, int Keys, int Missing, int Extra, int Placeholders, int Empty);

public sealed record LanguageComparison(string Reference, IReadOnlyList<LanguageFileSummary> Files, IReadOnlyList<LanguageIssue> Issues);

/// <summary>
/// Compares the UI language files (<c>Strings/&lt;language&gt;.json</c>, flat key → text) with a reference language,
/// the way the app reads them: a missing key falls back to English, and a text may only use placeholders the
/// reference has. Read-only.
/// </summary>
public static partial class LanguageFileComparer
{
    public const string DefaultReference = "en-US";

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>Reads every <c>*.json</c> in a folder as a language file (file name = language code).</summary>
    public static Dictionary<string, Dictionary<string, string>> ReadFolder(string folder)
    {
        var languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(folder, "*.json").OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            languages[Path.GetFileNameWithoutExtension(file)] = Read(File.ReadAllText(file));
        return languages;
    }

    public static Dictionary<string, string> Read(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
            texts[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
        return texts;
    }

    public static LanguageComparison Compare(IReadOnlyDictionary<string, Dictionary<string, string>> languages, string reference = DefaultReference)
    {
        ArgumentNullException.ThrowIfNull(languages);
        if (!languages.TryGetValue(reference, out Dictionary<string, string>? baseline))
            throw new ArgumentException("The reference language file is missing: " + reference, nameof(reference));

        var issues = new List<LanguageIssue>();
        var summaries = new List<LanguageFileSummary>();
        foreach ((string language, Dictionary<string, string> texts) in languages.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            int missing = 0, extra = 0, placeholders = 0, empty = 0;
            foreach ((string key, string text) in texts)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    empty++;
                    issues.Add(new(language, LanguageIssueKind.Empty, key, string.Empty));
                }
                if (!baseline.TryGetValue(key, out string? baseText))
                {
                    if (language != reference)
                    {
                        extra++;
                        issues.Add(new(language, LanguageIssueKind.Extra, key, text));
                    }
                    continue;
                }
                int highest = Numbers(baseText).DefaultIfEmpty(-1).Max();
                int[] unknown = [.. Numbers(text).Where(number => number > highest)];
                if (language != reference && unknown.Length > 0)
                {
                    placeholders++;
                    issues.Add(new(language, LanguageIssueKind.Placeholders, key, $"{reference}: {Placeholders(baseText)} · {language}: {Placeholders(text)}"));
                }
            }
            if (language != reference)
            {
                foreach (string key in baseline.Keys.Where(key => !texts.ContainsKey(key)))
                {
                    missing++;
                    issues.Add(new(language, LanguageIssueKind.Missing, key, baseline[key]));
                }
            }
            summaries.Add(new(language, texts.Count, missing, extra, placeholders, empty));
        }
        return new LanguageComparison(reference, summaries, issues);
    }

    /// <summary>The distinct placeholder numbers in a text, such as "{0} {1}".</summary>
    public static string Placeholders(string text) => string.Join(' ', Numbers(text).Select(number => "{" + number + "}"));

    private static IEnumerable<int> Numbers(string text)
        => PlaceholderPattern().Matches(text).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().Order();
}
