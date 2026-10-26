using System.Diagnostics;
using System.IO.Compression;
using IntegratedModManager.Core.Archives;

namespace IntegratedModManager.Core.Tests;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-archive-extractor-" + Guid.NewGuid().ToString("N"));

    public ArchiveExtractorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"C:\Downloads\Mod.tar.gz", ".tar.gz")]
    [InlineData("pack.ZIP", ".zip")]
    [InlineData("pack.tgz", ".tgz")]
    [InlineData("pack.7z", ".7z")]
    [InlineData("notes.txt", null)]
    public void GetSupportedExtension_PrefersCompoundSuffixes(string path, string? expected)
    {
        Assert.Equal(expected, ArchiveExtractor.GetSupportedExtension(path));
    }

    [Fact]
    public void Extract_Zip_WritesEntriesAndReportsProgressInOrder()
    {
        string archive = CreateZip("mod.zip", ("Mod/mod.ini", "[Constants]"), ("Mod/textures/a.dds", "dds"));
        string destination = Destination();
        var progress = new List<ArchiveExtractionProgress>();

        Extractor().Extract(archive, destination, progress.Add);

        Assert.Equal("[Constants]", File.ReadAllText(Path.Combine(destination, "Mod", "mod.ini")));
        Assert.True(File.Exists(Path.Combine(destination, "Mod", "textures", "a.dds")));
        Assert.Equal(new ArchiveExtractionProgress(10, ArchiveExtractionStage.Preparing), progress[0]);
        Assert.Equal(new ArchiveExtractionProgress(100, ArchiveExtractionStage.Completed), progress[^1]);
        Assert.All(progress.Skip(1).SkipLast(1), item =>
        {
            Assert.Equal(ArchiveExtractionStage.Extracting, item.Stage);
            Assert.InRange(item.Percent, 11, 95);
        });
        Assert.Equal(progress.Select(item => item.Percent).Order(), progress.Select(item => item.Percent));
    }

    [Fact]
    public void Extract_ZipWithOnlyDirectories_IsTaggedNoExtractableFiles()
    {
        string archive = Path.Combine(_root, "dirs.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            zip.CreateEntry("Mod/");
            zip.CreateEntry("Mod/textures/");
        }

        var error = Assert.Throws<InvalidOperationException>(() => Extractor().Extract(archive, Destination()));

        Assert.True(ArchiveErrors.Is(error, ArchiveErrorKind.NoExtractableFiles));
    }

    [Fact]
    public void Extract_ZipSymbolicLinkEntry_IsRejectedBeforeWriting()
    {
        string archive = Path.Combine(_root, "link.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            ZipArchiveEntry link = zip.CreateEntry("Mod/shared.ini");
            link.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
            using StreamWriter writer = new(link.Open());
            writer.Write("../../outside.ini");
        }

        string destination = Destination();
        var error = Assert.Throws<InvalidDataException>(() => Extractor().Extract(archive, destination));

        Assert.True(ArchiveErrors.Is(error, ArchiveErrorKind.ZipSymbolicLink));
        Assert.False(File.Exists(Path.Combine(destination, "Mod", "shared.ini")));
    }

    [Fact]
    public void Extract_ZipTraversalEntry_NeverWritesOutsideTheDestination()
    {
        string archive = CreateZip("evil.zip", ("../escaped.ini", "x"));
        string destination = Destination();

        Assert.Throws<InvalidDataException>(() => Extractor().Extract(archive, destination));

        Assert.False(File.Exists(Path.Combine(_root, "escaped.ini")));
    }

    [Theory]
    [InlineData("pack.7z")]
    [InlineData("pack.rar")]
    [InlineData("pack.cab")]
    public void Extract_ExternalFormatWithoutTools_IsTaggedToolUnavailableAfterTryingBoth(string name)
    {
        string archive = Path.Combine(_root, name);
        File.WriteAllBytes(archive, [1, 2, 3]);
        var lookups = new List<string>();
        var extractor = new ArchiveExtractor(
            () => { lookups.Add("7z"); return null; },
            () => { lookups.Add("bz"); return null; },
            TimeSpan.FromMinutes(1));

        var error = Assert.Throws<InvalidOperationException>(() => extractor.Extract(archive, Destination()));

        Assert.True(ArchiveErrors.Is(error, ArchiveErrorKind.ToolUnavailable));
        Assert.Equal(["7z", "bz"], lookups);
    }

    [Fact]
    public void Extract_Tar_UsesWindowsTarAndChecksTheExtractedTree()
    {
        string source = Path.Combine(_root, "tar-source");
        Directory.CreateDirectory(Path.Combine(source, "Mod"));
        File.WriteAllText(Path.Combine(source, "Mod", "mod.ini"), "[TextureOverride]");
        string archive = Path.Combine(_root, "pack.tar");
        RunTar("-cf", archive, "-C", source, "Mod");
        string destination = Destination();

        Extractor().Extract(archive, destination);

        Assert.Equal("[TextureOverride]", File.ReadAllText(Path.Combine(destination, "Mod", "mod.ini")));
    }

    [Fact]
    public void Extract_CorruptTar_FailsInspectionWithoutExtracting()
    {
        string archive = Path.Combine(_root, "broken.tar.gz");
        File.WriteAllBytes(archive, [0x1F, 0x8B, 0x08, 0x00, 0xFF, 0xFF]);
        string destination = Destination();

        Assert.Throws<InvalidDataException>(() => Extractor().Extract(archive, destination));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    [Fact]
    public void Extract_ToolOverTimeLimit_IsTaggedToolTimeoutWithTheToolName()
    {
        string archive = Path.Combine(_root, "slow.tar");
        File.WriteAllBytes(archive, new byte[1024]);
        // Starting a process alone takes far longer than one millisecond.
        var extractor = new ArchiveExtractor(() => null, () => null, TimeSpan.FromMilliseconds(1));

        var error = Assert.Throws<TimeoutException>(() => extractor.Extract(archive, Destination()));

        Assert.True(ArchiveErrors.Is(error, ArchiveErrorKind.ToolTimeout));
        Assert.Equal("tar.exe", ArchiveErrors.DetailOf(error));
        Assert.IsType<TimeoutException>(error.InnerException);
    }

    [Fact]
    public void Tag_KeepsTheExceptionTypeAndRecordsKindAndDetail()
    {
        var original = new InvalidOperationException("tool failed");

        InvalidOperationException tagged = ArchiveErrors.Tag(original, ArchiveErrorKind.SevenZipExtractionFailed, "stderr text");

        Assert.Same(original, tagged);
        Assert.Equal(ArchiveErrorKind.SevenZipExtractionFailed, ArchiveErrors.KindOf(tagged));
        Assert.Equal("stderr text", ArchiveErrors.DetailOf(tagged));
        Assert.Null(ArchiveErrors.KindOf(new InvalidOperationException()));
        Assert.Null(ArchiveErrors.DetailOf(new InvalidOperationException()));
    }

    private static ArchiveExtractor Extractor() => new(() => null, () => null, TimeSpan.FromMinutes(1));

    private string Destination()
    {
        string destination = Path.Combine(_root, "out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        return destination;
    }

    private string CreateZip(string name, params (string Entry, string Content)[] entries)
    {
        string path = Path.Combine(_root, name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entry, string content) in entries)
        {
            using StreamWriter writer = new(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }

        return path;
    }

    private static void RunTar(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
