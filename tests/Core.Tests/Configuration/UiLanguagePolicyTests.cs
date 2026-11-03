using IntegratedModManager.Core.Configuration;

namespace IntegratedModManager.Core.Tests;

public sealed class UiLanguagePolicyTests
{
    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", true)]
    [InlineData("zh-Hant-HK", true)]
    [InlineData("ZH", true)]
    [InlineData("en-US", false)]
    [InlineData("ja-JP", false)]
    [InlineData("zhx", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UsesChinese_OnlyForChineseSystemLanguages(string? language, bool expected)
        => Assert.Equal(expected, UiLanguagePolicy.UsesChinese(language));
}
