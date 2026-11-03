namespace IntegratedModManager.Core.Configuration;

public static class UiLanguagePolicy
{
    /// <summary>The language of a first install: Chinese for any Chinese system language, otherwise English, the only other one.</summary>
    public static bool UsesChinese(string? systemLanguage) =>
        string.Equals(systemLanguage, "zh", StringComparison.OrdinalIgnoreCase)
        || systemLanguage?.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) == true;
}
