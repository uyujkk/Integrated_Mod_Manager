namespace IntegratedModManager.Core.Configuration;

/// <summary>
/// Which UI language to use. Languages are culture codes such as <c>zh-CN</c>, one per language
/// file; English is the fallback for any text a language file does not have.
/// </summary>
public static class UiLanguagePolicy
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    /// <summary>
    /// The language of a first install: the available language with the system's exact code, else
    /// one with the same base language (zh-TW finds zh-CN), else English.
    /// </summary>
    public static string ForSystem(string? systemLanguage, IEnumerable<string> available)
    {
        string[] languages = available.ToArray();
        if (string.IsNullOrWhiteSpace(systemLanguage)) return English;
        string? exact = languages.FirstOrDefault(language => string.Equals(language, systemLanguage, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        string baseLanguage = BaseLanguage(systemLanguage);
        return languages.FirstOrDefault(language => string.Equals(BaseLanguage(language), baseLanguage, StringComparison.OrdinalIgnoreCase))
            ?? English;
    }

    /// <summary>
    /// The language saved in the configuration. Versions before 4.8 saved "zh" or "en" and read
    /// anything else as Chinese, so those two keep their old values and other languages use their code.
    /// </summary>
    public static string ToConfig(string language) => language switch
    {
        Chinese => "zh",
        English => "en",
        _ => language
    };

    /// <summary>
    /// The saved language, or null when nothing was saved. A language whose file is no longer
    /// available falls back to English.
    /// </summary>
    public static string? FromConfig(string? stored, IEnumerable<string> available)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (string.Equals(stored, "zh", StringComparison.OrdinalIgnoreCase)) return Chinese;
        if (string.Equals(stored, "en", StringComparison.OrdinalIgnoreCase)) return English;
        return available.FirstOrDefault(language => string.Equals(language, stored, StringComparison.OrdinalIgnoreCase)) ?? English;
    }

    private static string BaseLanguage(string language)
    {
        int separator = language.IndexOfAny(['-', '_']);
        return separator < 0 ? language : language[..separator];
    }
}
