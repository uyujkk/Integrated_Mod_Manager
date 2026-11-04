using IntegratedModManager.Core.Configuration;

namespace IntegratedModManager.Core.Tests;

public sealed class UiLanguagePolicyTests
{
    private static readonly string[] Available = ["zh-CN", "en-US", "ru-RU"];

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("zh-Hant-HK", "zh-CN")]
    [InlineData("ZH", "zh-CN")]
    [InlineData("en-GB", "en-US")]
    [InlineData("ru-RU", "ru-RU")]
    [InlineData("ru", "ru-RU")]
    [InlineData("RU-ru", "ru-RU")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("zhx", "en-US")]
    [InlineData("", "en-US")]
    [InlineData(null, "en-US")]
    public void ForSystem_PicksTheClosestAvailableLanguage(string? system, string expected)
        => Assert.Equal(expected, UiLanguagePolicy.ForSystem(system, Available));

    [Fact]
    public void ForSystem_FallsBackToEnglishWhenTheBaseLanguageIsMissing()
        => Assert.Equal("en-US", UiLanguagePolicy.ForSystem("ru-RU", ["zh-CN", "en-US"]));

    [Theory]
    [InlineData("zh-CN", "zh")]
    [InlineData("en-US", "en")]
    [InlineData("ru-RU", "ru-RU")]
    public void ToConfig_KeepsTheOldValuesForChineseAndEnglish(string language, string expected)
        => Assert.Equal(expected, UiLanguagePolicy.ToConfig(language));

    [Theory]
    [InlineData("zh", "zh-CN")]
    [InlineData("EN", "en-US")]
    [InlineData("ru-RU", "ru-RU")]
    [InlineData("ru-ru", "ru-RU")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FromConfig_ReadsOldAndNewValues(string? stored, string? expected)
        => Assert.Equal(expected, UiLanguagePolicy.FromConfig(stored, Available));

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    public void ConfigValues_RoundTrip(string language)
        => Assert.Equal(language, UiLanguagePolicy.FromConfig(UiLanguagePolicy.ToConfig(language), Available));
}
