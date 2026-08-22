using System.Security.Cryptography;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class UpdatePackageFileTests
{
    [Fact]
    public async Task WriteAsync_WritesAllBytesAndReleasesDestination()
    {
        using var directory = new TemporaryDirectory();
        string destination = Path.Combine(directory.Path, "package.zip.download");
        byte[] content = Enumerable.Range(0, 300_000).Select(index => (byte)(index % 251)).ToArray();

        long written = await UpdatePackageFile.WriteAsync(new MemoryStream(content), destination);

        Assert.Equal(content.LongLength, written);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        using FileStream exclusive = new(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task WriteAsync_TruncatesAnExistingDestination()
    {
        using var directory = new TemporaryDirectory();
        string destination = Path.Combine(directory.Path, "package.zip.download");
        await File.WriteAllBytesAsync(destination, new byte[4096]);
        byte[] replacement = [1, 2, 3, 4];

        await UpdatePackageFile.WriteAsync(new MemoryStream(replacement), destination);

        Assert.Equal(replacement, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task WriteAsync_ReportsCumulativeProgress()
    {
        using var directory = new TemporaryDirectory();
        string destination = Path.Combine(directory.Path, "package.zip.download");
        byte[] content = new byte[300_000];
        var progress = new List<long>();

        await UpdatePackageFile.WriteAsync(new MemoryStream(content), destination, progress.Add);

        Assert.NotEmpty(progress);
        Assert.Equal(content.LongLength, progress[^1]);
        Assert.True(progress.SequenceEqual(progress.OrderBy(value => value)));
    }

    [Fact]
    public async Task WriteAsync_ReleasesDestinationWhenCancelled()
    {
        using var directory = new TemporaryDirectory();
        string destination = Path.Combine(directory.Path, "package.zip.download");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdatePackageFile.WriteAsync(new MemoryStream(new byte[32]), destination, cancellationToken: cancellation.Token));

        File.Delete(destination);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ComputeSha256Async_ReturnsUppercaseHashAndReleasesSource()
    {
        using var directory = new TemporaryDirectory();
        string source = Path.Combine(directory.Path, "package.zip.download");
        byte[] content = "Integrated Mod Manager"u8.ToArray();
        await File.WriteAllBytesAsync(source, content);
        string expected = Convert.ToHexString(SHA256.HashData(content));

        string actual = await UpdatePackageFile.ComputeSha256Async(source);

        Assert.Equal(expected, actual);
        using FileStream exclusive = new(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imm-core-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
