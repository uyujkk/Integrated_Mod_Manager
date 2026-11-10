using System.Net.Http.Headers;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

// IMM_ONLINE_FIXTURES (screenshots and UI tests only): with it every request is answered from local samples and
// nothing reaches the network; without it the app's HTTP client is the plain one it always was.
public sealed class OnlineFixtureHandlerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "imm-fixtures-" + Guid.NewGuid().ToString("N"));

    public OnlineFixtureHandlerTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "img"));
        File.WriteAllText(Path.Combine(_folder, "fixtures.json"), """
            { "responses": [
              { "prefix": "https://example.invalid/api/List?", "file": "list.json" },
              { "prefix": "https://example.invalid/api/List?page=2", "file": "list2.json" },
              { "prefix": "https://example.invalid/img/", "file": "img/placeholder.png" },
              { "prefix": "https://example.invalid/escape", "file": "../outside.json" }
            ] }
            """);
        File.WriteAllText(Path.Combine(_folder, "list.json"), """{"page":1}""");
        File.WriteAllText(Path.Combine(_folder, "list2.json"), """{"page":2}""");
        File.WriteAllBytes(Path.Combine(_folder, "img", "placeholder.png"), [0x89, 0x50, 0x4E, 0x47]);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task ARequestWithASampleIsAnsweredFromTheFolder()
    {
        using HttpClient client = OnlineHttpClientFactory.Create("Test/1", _folder);

        Assert.Equal("""{"page":1}""", await client.GetStringAsync("https://example.invalid/api/List?page=1"));
        Assert.Equal("""{"page":2}""", await client.GetStringAsync("https://example.invalid/api/List?page=2&size=20"));
        using HttpResponseMessage image = await client.GetAsync("https://example.invalid/img/a/b.jpg");
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("https://gamebanana.com/apiv11/Mod/Index?_nPage=1")]
    [InlineData("https://api.github.com/repos/uyujkk/Integrated_Mod_Manager/releases/latest")]
    [InlineData("https://example.invalid/escape")] // a sample outside the folder is not read
    public async Task ARequestWithoutASampleFailsAndIsNotSent(string url)
    {
        var log = new List<string>();
        var handler = new OnlineFixtureHandler(_folder, log.Add);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));

        Assert.Equal([url], handler.Requests);
        Assert.Contains(log, line => line.Contains("测试样例模式", StringComparison.Ordinal) && line.Contains(url, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheFolderIsOnlyRead()
    {
        DateTime[] before = Snapshot();
        using HttpClient client = OnlineHttpClientFactory.Create("Test/1", _folder);
        await client.GetStringAsync("https://example.invalid/api/List?page=1");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.invalid/missing"));

        Assert.Equal(before, Snapshot());
        Assert.Equal(4, Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).Length);
    }

    private DateTime[] Snapshot() => Directory.GetFiles(_folder, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(File.GetLastWriteTimeUtc).ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutTheVariableTheClientIsThePlainOne(string? folder)
    {
        Assert.Null(OnlineHttpClientFactory.FixtureHandler(folder));
        using HttpClient client = OnlineHttpClientFactory.Create("IntegratedModManager/4.7.3", folder);
        Assert.Equal([new ProductInfoHeaderValue("IntegratedModManager", "4.7.3")], client.DefaultRequestHeaders.UserAgent);
        Assert.Equal(TimeSpan.FromSeconds(100), client.Timeout);
    }

    [Fact]
    public void TheVariableIsCalledImmOnlineFixtures()
        => Assert.Equal("IMM_ONLINE_FIXTURES", OnlineHttpClientFactory.FixturesVariable);
}
