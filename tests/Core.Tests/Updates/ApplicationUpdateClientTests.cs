using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Updates;

namespace IntegratedModManager.Core.Tests;

public sealed class ApplicationUpdateClientTests : IDisposable
{
    private const string PackageUrl = "https://github.com/o/r/releases/download/v4.7.0/Integrated_Mod_Manager-v4.7.0.zip";
    private const string ChecksumUrl = "https://github.com/o/r/releases/download/v4.7.0/SHA256SUMS.txt";
    private readonly string _folder = Directory.CreateTempSubdirectory("imm-update-client-").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> reply) => new(new Handler(reply));

    private static HttpResponseMessage Ok(byte[] body, bool withLength = true)
    {
        HttpContent content = withLength ? new ByteArrayContent(body) : new StreamContent(new NonSeekableStream(body));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static LatestReleaseInfo Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ApplicationUpdateClient.ParseRelease(document.RootElement, "https://github.com/o/r");
    }

    [Fact]
    public void ParseRelease_FindsThePackageAndItsChecksum()
    {
        LatestReleaseInfo release = Parse($$"""
            { "tag_name": "v4.7.0", "name": "4.7.0", "body": "  notes  ", "published_at": "2026-11-03T00:00:00Z",
              "html_url": "https://github.com/o/r/releases/tag/v4.7.0",
              "assets": [
                { "name": "Source.zip", "browser_download_url": "https://x/source.zip" },
                { "name": "Integrated_Mod_Manager-v4.7.0.zip", "browser_download_url": "{{PackageUrl}}" },
                { "name": "SHA256SUMS.txt", "browser_download_url": "{{ChecksumUrl}}" },
                { "name": "Integrated_Mod_Manager-v4.7.0.zip.sha256", "browser_download_url": "" } ] }
            """);

        Assert.Equal(new LatestReleaseInfo("v4.7.0", "4.7.0", "notes", "2026-11-03T00:00:00Z",
            "https://github.com/o/r/releases/tag/v4.7.0", PackageUrl, ChecksumUrl), release);
    }

    [Fact]
    public void ParseRelease_FallsBackForMissingFields()
    {
        LatestReleaseInfo release = Parse("""{ "tag_name": "v4.7.0", "name": null, "assets": [ { "name": "SHA256SUMS.txt", "browser_download_url": "https://x/sums" } ] }""");

        Assert.Equal(("v4.7.0", "", "https://github.com/o/r"), (release.Title, release.Body, release.PageUrl));
        Assert.Null(release.PackageUrl);
        Assert.Null(release.Sha256Url);
    }

    [Theory]
    [InlineData("Integrated_Mod_Manager-v4.7.0.zip", true)]
    [InlineData("integrated_mod_manager-V10.20.30.ZIP", true)]
    [InlineData("Integrated_Mod_Manager-v4.7.0-preview.1.zip", false)]
    [InlineData("Integrated_Mod_Manager-v4.7.zip", false)]
    public void PackageNamePattern_AcceptsOnlyStablePackages(string name, bool expected)
        => Assert.Equal(expected, ApplicationUpdateClient.PackageNamePattern.IsMatch(name));

    [Fact]
    public async Task FetchLatestRelease_AsksForTheGitHubFormat()
    {
        HttpRequestMessage? sent = null;
        HttpClient client = Client(request =>
        {
            sent = request;
            return Ok(Encoding.UTF8.GetBytes("""{ "tag_name": "v1.2.3" }"""));
        });

        LatestReleaseInfo release = await ApplicationUpdateClient.FetchLatestReleaseAsync(client, "https://api.github.com/latest", "https://page");

        Assert.Equal("v1.2.3", release.Tag);
        Assert.Contains("application/vnd.github+json", sent!.Headers.Accept.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Download_WritesTheFileAndReportsProgress(bool withLength)
    {
        byte[] package = Enumerable.Range(0, 300_000).Select(index => (byte)index).ToArray();
        string path = Path.Combine(_folder, "package.download");
        var reports = new List<(long Downloaded, long? Total)>();

        await ApplicationUpdateClient.DownloadAsync(Client(_ => Ok(package, withLength)), PackageUrl, path, (downloaded, total) => reports.Add((downloaded, total)));

        Assert.Equal(package, await File.ReadAllBytesAsync(path));
        Assert.Equal((0L, withLength ? package.Length : (long?)null), reports[0]);
        Assert.Equal(package.Length, reports[^1].Downloaded);
        Assert.All(reports, report => Assert.Equal(withLength ? package.Length : null, report.Total));
    }

    [Fact]
    public async Task Download_FailsOnAnErrorStatus()
    {
        HttpClient client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => ApplicationUpdateClient.DownloadAsync(client, PackageUrl, Path.Combine(_folder, "x"), null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_ComparesWithTheListedHash(bool matching)
    {
        string path = Path.Combine(_folder, "package.zip");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        string hash = Convert.ToHexString(SHA256.HashData(matching ? [1, 2, 3] : [9]));
        HttpClient client = Client(request => Ok(Encoding.UTF8.GetBytes($"{hash}  Integrated_Mod_Manager-v4.7.0.zip\n")));

        Assert.Equal(matching, await ApplicationUpdateClient.VerifyAsync(client, PackageUrl, ChecksumUrl, path));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }

    // A body without a known length, like a chunked response.
    private sealed class NonSeekableStream(byte[] body) : MemoryStream(body)
    {
        public override bool CanSeek => false;
    }
}
