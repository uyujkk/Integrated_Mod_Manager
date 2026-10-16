using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class EndfieldBridgeIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "bridge-tests", Guid.NewGuid().ToString("N"));
    private string Tools => Path.Combine(_root, "tools");
    public EndfieldBridgeIntegrationTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("42770", "", true)]
    [InlineData(null, "Arknights: Endfield", true)]
    [InlineData(null, "明日方舟：终末地", true)]
    [InlineData(null, "终末地", true)]
    [InlineData("30305", "Zenless Zone Zero", false)]
    [InlineData(null, null, false)]
    public void OfferOnlyForEndfield(string? category, string? name, bool expected) =>
        Assert.Equal(expected, EndfieldBridgeIntegration.IsEndfield(category, name));

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 0, false)]
    public void UpgradeOfferIsOneTimeAndOptional(bool eligible, int revision, bool expected) =>
        Assert.Equal(expected, EndfieldBridgeIntegration.ShouldOffer(eligible, revision));

    [Fact]
    public void LoaderChoicePreservesExplicitPathsAndUsesOnlyExistingDefaults()
    {
        string known = Path.Combine(_root, "XXMI Launcher.exe");
        File.WriteAllText(known, "test fixture - never executed");
        Assert.Equal("custom.exe", EndfieldBridgeIntegration.ResolveLoader(" custom.exe ", [known]));
        Assert.Equal(known, EndfieldBridgeIntegration.ResolveLoader(null, ["missing.exe", known]));
        Assert.Equal("", EndfieldBridgeIntegration.ResolveLoader("", ["missing.exe"]));
    }

    [Fact]
    public void ReleaseIsPinnedAndDoesNotDownloadLatestOrInstaller()
    {
        Assert.EndsWith("/v0.3.2/EndfieldBridge-0.3.2-win-x64-portable.zip", EndfieldBridgeIntegration.DownloadUrl);
        Assert.Equal(64, EndfieldBridgeIntegration.PackageSha256.Length);
        Assert.All(EndfieldBridgeIntegration.PackageSha256, character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public async Task InstallsVerifiedPackageWithLicenseAndSourceWithoutRunningIt()
    {
        var package = CreatePackage();
        string exe = await EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash);
        Assert.Equal("fixture - never execute", File.ReadAllText(exe));
        Assert.True(EndfieldBridgeIntegration.IsInstalled(Tools));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "data")));
        Assert.Empty(Directory.GetDirectories(Tools, ".bridge-install-*"));
    }

    [Fact]
    public async Task HashFailureDoesNotInstallOrExecute()
    {
        var package = CreatePackage();
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, new string('0', 64)));
        Assert.False(EndfieldBridgeIntegration.IsInstalled(Tools));
        Assert.Empty(Directory.GetFileSystemEntries(Tools));
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task RejectsMalformedChecksum(string checksum)
    {
        var package = CreatePackage();
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, checksum));
    }

    [Fact]
    public async Task ExistingBridgeAndRecoveryDataAreNeverOverwritten()
    {
        var package = CreatePackage();
        string root = EndfieldBridgeIntegration.InstallDirectory(Tools);
        Directory.CreateDirectory(Path.Combine(root, "data"));
        string profile = Path.Combine(root, "data", "profile.json");
        File.WriteAllText(profile, "keep private data");
        await Assert.ThrowsAsync<IOException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash));
        Assert.Equal("keep private data", File.ReadAllText(profile));
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("/absolute.exe")]
    [InlineData("x/../../escape.exe")]
    [InlineData("x\\escape.exe")]
    [InlineData("C:/escape.exe")]
    [InlineData("file:ads")]
    [InlineData("CON.txt")]
    [InlineData("NUL")]
    [InlineData("COM1.exe")]
    [InlineData("LPT9")]
    [InlineData("AUX")]
    [InlineData("PRN")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("bad|name")]
    [InlineData("empty//part")]
    [InlineData("data/profile.json")]
    public async Task RejectsUnsafeAndUserDataPaths(string extra)
    {
        var package = CreatePackage(extra);
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash));
        Assert.False(Directory.Exists(EndfieldBridgeIntegration.InstallDirectory(Tools)));
        Assert.False(File.Exists(Path.Combine(_root, "escape.exe")));
    }

    [Theory]
    [InlineData(0xA000)]
    [InlineData(0x6000)]
    [InlineData(0x1000)]
    public async Task RejectsArchiveLinksAndSpecialFiles(int unixType)
    {
        var package = CreatePackage("link", unixType << 16);
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash));
    }

    [Fact]
    public async Task RejectsWindowsReparseEntry()
    {
        var package = CreatePackage("link", (int)FileAttributes.ReparsePoint);
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash));
    }

    [Theory]
    [InlineData("EndfieldBridge.exe")]
    [InlineData("licenses/LICENSE")]
    [InlineData("docs/NOTICE.md")]
    [InlineData("docs/SOURCE.txt")]
    [InlineData("docs/RISK.zh-CN.md")]
    [InlineData("docs/RISK.md")]
    public async Task RejectsIncompleteDistribution(string omitted)
    {
        var package = CreatePackage(omit: omitted);
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash));
    }

    [Fact]
    public async Task RejectsDuplicateEntries()
    {
        var package = CreatePackage("EndfieldBridge.exe");
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash));
    }

    [Fact]
    public async Task RejectsWrongRootAndCleansStagingOnFileDirectoryCollision()
    {
        var wrong = CreatePackage("file", wrongRoot: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(wrong.Path, Tools, wrong.Hash));
        var collision = CreatePackage("EndfieldBridge.exe/nested.txt");
        await Assert.ThrowsAnyAsync<IOException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(collision.Path, Tools, collision.Hash));
        Assert.Empty(Directory.GetDirectories(Tools));
    }

    [Fact]
    public async Task CancellationNeverPromotesInstallation()
    {
        var package = CreatePackage();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EndfieldBridgeIntegration.InstallArchiveAsync(package.Path, Tools, package.Hash, cancellation.Token));
        Assert.False(EndfieldBridgeIntegration.IsInstalled(Tools));
    }

    [Fact]
    public async Task DownloadUsesPinnedHttpsAndHashVerification()
    {
        using var client = new HttpClient(new ReplyHandler(request =>
        {
            Assert.Equal(EndfieldBridgeIntegration.DownloadUrl, request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.DownloadAsync(client, Tools));
        Assert.Empty(Directory.GetFileSystemEntries(Tools));
    }

    [Fact]
    public async Task HttpFailuresAndOversizedDownloadsAreCleaned()
    {
        using var failure = new HttpClient(new ReplyHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Assert.ThrowsAsync<HttpRequestException>(() => EndfieldBridgeIntegration.DownloadAsync(failure, Tools));
        using var oversized = new HttpClient(new ReplyHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = EndfieldBridgeIntegration.MaxPackageBytes + 1;
            return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => EndfieldBridgeIntegration.DownloadAsync(oversized, Tools));
        Assert.Empty(Directory.GetFileSystemEntries(Tools));
    }

    private (string Path, string Hash) CreatePackage(string? extra = null, int attributes = 0, string? omit = null, bool wrongRoot = false)
    {
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (string file in new[] { "EndfieldBridge.exe", "licenses/LICENSE", "docs/NOTICE.md", "docs/SOURCE.txt", "docs/RISK.zh-CN.md", "docs/RISK.md" })
            {
                if (file == omit) continue;
                using var writer = new StreamWriter(zip.CreateEntry(EndfieldBridgeIntegration.FolderName + "/" + file).Open(), new UTF8Encoding(false));
                writer.Write("fixture - never execute");
            }
            if (extra is not null)
            {
                var entry = zip.CreateEntry((wrongRoot ? "wrong" : EndfieldBridgeIntegration.FolderName) + "/" + extra);
                entry.ExternalAttributes = attributes;
                using var writer = new StreamWriter(entry.Open());
                writer.Write("fixture");
            }
        }
        return (path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    private sealed class ReplyHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
}
