using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Tests.Infrastructure;

public sealed class DiagnosticTextTests
{
    [Theory]
    [InlineData(@"Could not read C:\Users\someone\Mods\a.ini", "Could not read <private-path>")]
    [InlineData(@"Share \\server\share\file failed", "Share <private-path>")]
    [InlineData("token=abc123 rest", "token=<redacted> rest")]
    [InlineData("Authorization: Bearer xyz", "Authorization=<redacted> xyz")]
    [InlineData("API_KEY = 42", "API_KEY=<redacted>")]
    [InlineData("GET https://example.invalid/a?id=1&token=secret&x=2", "GET https://example.invalid/a?id=1&token=<redacted>")]
    [InlineData("nothing private here", "nothing private here")]
    public void Sanitize_ReplacesPathsAndSecrets(string text, string expected) => Assert.Equal(expected, DiagnosticText.Sanitize(text));

    [Theory]
    [InlineData(-5, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024 * 1024 * 2, "2 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024 * 1024 * 3, "3072 TB")]
    public void StorageSize_IsReadable(long bytes, string expected) => Assert.Equal(expected, StorageSizeText.Format(bytes));
}
