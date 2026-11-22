using IntegratedModManager.Core.Configuration;

namespace IntegratedModManager.Core.Tests.Configuration;

public sealed class ModLinkTextTests
{
    [Theory]
    [InlineData("https://gamebanana.com/mods/1", "https://gamebanana.com/mods/1")]
    [InlineData("  http://example.invalid/a  ", "http://example.invalid/a")]
    [InlineData("HTTPS://Example.invalid", "HTTPS://Example.invalid")]
    [InlineData("gamebanana.com/mods/1", "https://gamebanana.com/mods/1")]
    [InlineData(" example.invalid ", "https://example.invalid")]
    [InlineData("ftp://example.invalid", "https://ftp://example.invalid")]
    public void Normalize_KeepsHttpLinksAndAddsHttpsOtherwise(string link, string expected)
        => Assert.Equal(expected, ModLinkText.Normalize(link));
}
