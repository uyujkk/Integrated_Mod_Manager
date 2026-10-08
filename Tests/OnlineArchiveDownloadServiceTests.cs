using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineArchiveDownloadServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-download-tests", Guid.NewGuid().ToString("N"));
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4]; // Signature fixture, not a valid installable ZIP.
    public OnlineArchiveDownloadServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private static OnlineArchiveDownloadRequest Request(long expected = 0) =>
        new("https://example.invalid/dl/42", "https://example.invalid/mods/1", "mod.zip", "Title", 42, expected);
    private static HttpResponseMessage Response(HttpContent? content = null, string? name = "mod.zip")
    {
        var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = content ?? new ByteArrayContent(Zip) };
        if (name is not null) result.Content.Headers.ContentDisposition = new("attachment") { FileName = name };
        return result;
    }

    [Fact]
    public async Task DownloadStreamsBytesReportsProgressAndPreservesBorrowedClient()
    {
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        var service = new OnlineArchiveDownloadService(client);
        var progress = new List<OnlineArchiveDownloadProgress>();
        var result = await service.DownloadAsync(Request(Zip.Length), _root, progress.Add);
        Assert.Equal(Zip, File.ReadAllBytes(result.ArchivePath));
        Assert.Equal(Zip.Length, result.BytesRead);
        Assert.Equal(new(0, Zip.Length), progress.First());
        Assert.Equal(new(Zip.Length, Zip.Length), progress.Last());
        Assert.Equal(100, progress.Last().Percent);
        Assert.Equal("https://example.invalid/mods/1", handler.Referrer);
        Assert.Equal("*/*", handler.Accept);
        Assert.Equal("https://example.invalid/dl/42", handler.Url);
        var second = await service.DownloadAsync(Request(), _root);
        Assert.NotEqual(result.ArchivePath, second.ArchivePath);
        Assert.Equal(2, handler.RequestCount);
        Assert.Empty(Directory.GetFiles(_root, "*.download"));
    }

    [Fact]
    public async Task UnicodeServerFilenameStarWinsAndExistingFileRemainsUntouched()
    {
        string original = Path.Combine(_root, "角色 图片.zip");
        File.WriteAllText(original, "existing user file");
        using var handler = new FakeHandler(_ =>
        {
            var response = Response(name: "fallback.zip");
            response.Content.Headers.ContentDisposition!.FileNameStar = "角色 图片.zip";
            return response;
        });
        using var client = new HttpClient(handler);
        var result = await new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root);
        Assert.Equal("角色 图片-2.zip", Path.GetFileName(result.ArchivePath));
        Assert.Equal("existing user file", File.ReadAllText(original));
        Assert.Equal(Zip, File.ReadAllBytes(result.ArchivePath));
    }

    [Theory]
    [InlineData("../../outside.zip", "outside.zip")]
    [InlineData(@"C:\outside\mod.zip", "mod.zip")]
    [InlineData("mod.zip:stream", "mod.zip_stream")]
    [InlineData("CON.zip", "_CON.zip")]
    [InlineData("LPT1.zip", "_LPT1.zip")]
    [InlineData("NUL", "_NUL")]
    [InlineData("..", "mod.zip")]
    [InlineData("\"good.zip\"", "good.zip")]
    public async Task UntrustedServerNameIsOneSafeLeaf(string name, string expected)
    {
        using var handler = new FakeHandler(_ => Response(name: name));
        using var client = new HttpClient(handler);
        var result = await new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root);
        Assert.Equal(expected, Path.GetFileName(result.ArchivePath));
        Assert.Equal(_root, Path.GetDirectoryName(result.ArchivePath));
    }

    [Theory]
    [InlineData("https://example.invalid/files/server.7z", "", "server.7z")]
    [InlineData("https://example.invalid/dl/42", "", "Title-42.zip")]
    [InlineData("https://example.invalid/files/server.7z", "chosen.zip", "chosen.zip")]
    public async Task FilenameFallbackKeepsPreferredThenResponseThenTitle(string uri, string preferred, string expected)
    {
        using var handler = new FakeHandler(_ => Response(name: null));
        using var client = new HttpClient(handler);
        var result = await new OnlineArchiveDownloadService(client).DownloadAsync(Request() with { DownloadUrl = uri, PreferredFileName = preferred }, _root);
        Assert.Equal(expected, Path.GetFileName(result.ArchivePath));
    }

    [Fact]
    public async Task ExistingDirectoryWithSameNameIsNotReplaced()
    {
        Directory.CreateDirectory(Path.Combine(_root, "mod.zip"));
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        var result = await new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root);
        Assert.Equal("mod-2.zip", Path.GetFileName(result.ArchivePath));
        Assert.True(Directory.Exists(Path.Combine(_root, "mod.zip")));
    }

    [Fact]
    public async Task UnknownContentLengthReportsIndeterminateBytesAndKeepsFinalBytes()
    {
        using var handler = new FakeHandler(_ => Response(new StreamContent(new NonSeekableStream(Zip))));
        using var client = new HttpClient(handler);
        var progress = new List<OnlineArchiveDownloadProgress>();
        await new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root, progress.Add);
        Assert.All(progress, p => { Assert.Equal(0, p.TotalBytes); Assert.Equal(0, p.Percent); });
        Assert.Equal(Zip.Length, progress.Last().BytesRead);
    }

    [Fact]
    public async Task ExpectedSizeRemainsPreferredOverContentLength()
    {
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        var progress = new List<OnlineArchiveDownloadProgress>();
        await new OnlineArchiveDownloadService(client).DownloadAsync(Request(Zip.Length + 1024), _root, progress.Add);
        Assert.All(progress, p => Assert.Equal(Zip.Length + 1024, p.TotalBytes));
    }

    [Theory]
    [InlineData("text/html", "<html>login required</html>")]
    [InlineData("application/json", "{\"error\":true}")]
    [InlineData("application/octet-stream", "Too many requests")]
    [InlineData("application/octet-stream", "<!DOCTYPE html>")]
    [InlineData("application/octet-stream", "")]
    public async Task NonArchiveWebOrEmptyResponsesAreNotPublished(string type, string body)
    {
        using var handler = new FakeHandler(_ => Response(new StringContent(body, System.Text.Encoding.UTF8, type)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task RecognizedSignatureStillWinsOverIncorrectTextMime()
    {
        using var handler = new FakeHandler(_ =>
        {
            var response = Response();
            response.Content.Headers.ContentType = new("text/plain");
            return response;
        });
        using var client = new HttpClient(handler);
        await new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task SizeMismatchCleansOnlyOwnedPartialAndReturnsStructuredSizes()
    {
        string original = Path.Combine(_root, "mod.zip");
        File.WriteAllText(original, "original");
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<OnlineArchiveSizeException>(() =>
            new OnlineArchiveDownloadService(client).DownloadAsync(Request(Zip.Length + 1025), _root));
        Assert.Equal(Zip.Length, error.ActualBytes);
        Assert.Equal(Zip.Length + 1025, error.ExpectedBytes);
        Assert.Equal("original", File.ReadAllText(original));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task HttpFailureDoesNotCreatePartialFile(HttpStatusCode status)
    {
        using var handler = new FakeHandler(_ => new(status));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task CancellationBeforeSendDoesNotIssueRequest()
    {
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OnlineArchiveDownloadService(client)
            .DownloadAsync(Request(), _root, cancellationToken: new CancellationToken(true)));
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task CancellationAfterReadCleansPartialAndPreservesExistingArchive()
    {
        using var cancellation = new CancellationTokenSource();
        File.WriteAllText(Path.Combine(_root, "mod.zip"), "keep");
        using var handler = new FakeHandler(_ => Response(new StreamContent(new NonSeekableStream(Zip, cancellation.Cancel))));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OnlineArchiveDownloadService(client)
            .DownloadAsync(Request(), _root, cancellationToken: cancellation.Token));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_root, "mod.zip")));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task BrokenResponseStreamCleansPartialFile()
    {
        using var handler = new FakeHandler(_ => Response(new StreamContent(new NonSeekableStream(Zip, () => throw new IOException("Synthetic connection failure")))));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<IOException>(() => new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task CancellationFromFinalProgressCannotPublishFile()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OnlineArchiveDownloadService(client)
            .DownloadAsync(Request(), _root, p => { if (p.BytesRead > 0) cancellation.Cancel(); }, cancellation.Token));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("file:///C:/secret.zip")]
    [InlineData("ftp://example.invalid/mod.zip")]
    [InlineData("not a URL")]
    public async Task NonHttpUrlIsRejectedBeforeSending(string url)
    {
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new OnlineArchiveDownloadService(client).DownloadAsync(Request() with { DownloadUrl = url }, _root));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task MissingDestinationDoesNotRecreateUserFolderOrSendRequest()
    {
        using var handler = new FakeHandler(_ => Response());
        using var client = new HttpClient(handler);
        string missing = Path.Combine(_root, "missing");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new OnlineArchiveDownloadService(client).DownloadAsync(Request(), missing));
        Assert.False(Directory.Exists(missing));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void RenameForDetectedExtensionDoesNotOverwriteAndReturnsActualPath()
    {
        string source = Path.Combine(_root, "download");
        File.WriteAllBytes(source, Zip);
        string desired = source + OnlineDownloadFilePolicy.DetectArchiveExtension(source);
        File.WriteAllText(desired, "existing");
        string result = OnlineDownloadFilePolicy.MoveWithoutOverwrite(source, desired);
        Assert.Equal("download-2.zip", Path.GetFileName(result));
        Assert.False(File.Exists(source));
        Assert.Equal("existing", File.ReadAllText(desired));
        Assert.Equal(Zip, File.ReadAllBytes(result));
    }

    [Fact]
    public void ExtremelyLongNameIsBoundedAndExtensionPreserved()
    {
        string result = OnlineDownloadFilePolicy.ResolveFileName(new string('a', 500) + ".zip", null, null, "", 1);
        Assert.Equal(200, result.Length);
        Assert.EndsWith(".zip", result);
    }

    [Fact]
    public async Task RealSyntheticZipCanDownloadIntoMatchedCharacterAndExtract()
    {
        string character = Path.Combine(_root, "庄方怡");
        Directory.CreateDirectory(character);
        Directory.CreateDirectory(Path.Combine(_root, "提弗洛斯"));
        string destination = OnlineDownloadSelectionPolicy.SelectCharacterFolder(
            Directory.GetDirectories(_root), ["Zhuang Fangyi", "庄方怡"])!;
        Assert.Equal(character, destination);
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("SyntheticMod/mod.ini").Open());
            writer.Write("[Constants]\n$synthetic = 1\n");
        }
        byte[] bytes = buffer.ToArray();
        using var handler = new FakeHandler(_ => Response(new ByteArrayContent(bytes), "synthetic.zip"));
        using var client = new HttpClient(handler);
        var result = await new OnlineArchiveDownloadService(client).DownloadAsync(Request(bytes.Length), destination);
        string extracted = Path.Combine(character, "Extracted");
        System.IO.Compression.ZipFile.ExtractToDirectory(result.ArchivePath, extracted);
        Assert.Contains("$synthetic = 1", File.ReadAllText(Path.Combine(extracted, "SyntheticMod", "mod.ini")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "提弗洛斯")));
        Assert.Equal(character, Path.GetDirectoryName(result.ArchivePath));
    }

    [Fact]
    public async Task FailureAfterFirstWrittenChunkCleansPartial()
    {
        using var handler = new FakeHandler(_ => Response(new StreamContent(new ChunkStream(Zip, failSecond: true))));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<IOException>(() => new OnlineArchiveDownloadService(client).DownloadAsync(Request(), _root));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task SlowChunkProgressIsMonotonicAndIntermediateCancellationCleansPartial()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new FakeHandler(_ => Response(new StreamContent(new ChunkStream(Zip, slow: true))));
        using var client = new HttpClient(handler);
        var samples = new List<OnlineArchiveDownloadProgress>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OnlineArchiveDownloadService(client)
            .DownloadAsync(Request(), _root, p => { samples.Add(p); if (p.BytesRead > 0) cancellation.Cancel(); }, cancellation.Token));
        Assert.Equal([0L, 4L], samples.Select(p => p.BytesRead));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData(" \"good.zip\" ", "good.zip")]
    [InlineData("COM9.txt", "_COM9.txt")]
    [InlineData("AUX.zip", "_AUX.zip")]
    [InlineData("PRN.zip", "_PRN.zip")]
    [InlineData("mod.zip. ", "mod.zip")]
    public void WindowsLeafNormalizationHandlesWhitespaceAndReservedNames(string name, string expected)
        => Assert.Equal(expected, OnlineDownloadFilePolicy.ResolveFileName(name, null, null, "", 1));

    [Theory]
    [InlineData("377ABCAF271C", ".7z")]
    [InlineData("526172211A0701", ".rar")]
    [InlineData("1F8B", ".gz")]
    [InlineData("425A68", ".bz2")]
    [InlineData("FD377A585A00", ".xz")]
    [InlineData("28B52FFD", ".zst")]
    [InlineData("00000000", "")]
    public void SignatureDetectionPreservesExistingFormats(string hex, string expected)
    {
        string file = Path.Combine(_root, "signature");
        File.WriteAllBytes(file, Convert.FromHexString(hex));
        Assert.Equal(expected, OnlineDownloadFilePolicy.DetectArchiveExtension(file));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount;
        public string? Referrer, Accept, Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Referrer = request.Headers.Referrer?.AbsoluteUri;
            Accept = request.Headers.GetValues("Accept").Single();
            Url = request.RequestUri!.AbsoluteUri;
            HttpResponseMessage result = response(request);
            result.RequestMessage = request;
            return Task.FromResult(result);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes, Action? afterRead = null) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.ReadAsync(buffer, cancellationToken);
            afterRead?.Invoke();
            return result;
        }
    }

    private sealed class ChunkStream(byte[] bytes, bool failSecond = false, bool slow = false) : MemoryStream(bytes)
    {
        private int _calls;
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (failSecond && ++_calls == 2) throw new IOException("Failure after an already written chunk");
            if (slow) await Task.Delay(160, cancellationToken);
            return await base.ReadAsync(buffer[..Math.Min(4, buffer.Length)], cancellationToken);
        }
    }
}
