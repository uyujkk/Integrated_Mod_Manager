using System.Diagnostics;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class BandizipArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-bandizip-tests", Guid.NewGuid().ToString("N"));
    private readonly string _archivePath;

    public BandizipArchiveExtractorTests()
    {
        Directory.CreateDirectory(_root);
        _archivePath = Path.Combine(_root, "mod.zipx");
        // The fake process runner supplies listings; only signature inspection reads this file.
        File.WriteAllBytes(_archivePath, "PK\u0003\u0004"u8.ToArray());
    }

    [Fact]
    public void FindExecutable_UsesFirstConsoleToolAndIgnoresGuiOnlyInstallation()
    {
        string guiOnly = CreateDirectory("gui");
        string installed = CreateDirectory("installed");
        string later = CreateDirectory("later");
        File.WriteAllText(Path.Combine(guiOnly, "Bandizip.exe"), string.Empty);
        File.WriteAllText(Path.Combine(installed, "bz.exe"), string.Empty);
        File.WriteAllText(Path.Combine(later, "bz.exe"), string.Empty);

        Assert.Equal(Path.Combine(installed, "bz.exe"),
            BandizipArchiveExtractor.FindExecutable(["", " ", ".", "relative", guiOnly, installed, later]));
    }

    [Fact]
    public void FindExecutable_ReturnsNullWhenConsoleToolIsMissing()
    {
        Assert.Null(BandizipArchiveExtractor.FindExecutable([CreateDirectory("empty")]));
    }

    [Fact]
    public void Extract_InspectsBeforeWritingAndPreservesUnicodeAndSpacesInArguments()
    {
        string archive = Path.Combine(_root, "下载 文件", "测试 mod.7z");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        File.Copy(_archivePath, archive);
        string destination = Path.Combine(_root, "输出 文件夹") + Path.DirectorySeparatorChar;
        string executable = Path.Combine(_root, "Bandizip 安装", "bz.exe");
        var commands = new List<ProcessStartInfo>();

        BandizipArchiveExtractor.Extract(executable, archive, destination, startInfo =>
        {
            commands.Add(startInfo);
            return commands.Count switch
            {
                1 => (0, "Mod/\r\nMod/测试.ini\r\n", ""),
                2 => (0, "drwxr-xr-x 0 0 0 0 Mod/\n-rw-r--r-- 0 0 0 10 Mod/测试.ini\n", ""),
                _ => (0, "Extracted", "")
            };
        });

        Assert.Equal(3, commands.Count);
        string stagedArchive = commands[0].ArgumentList[1];
        Assert.NotEqual(archive, stagedArchive);
        Assert.Equal(Path.GetFileName(archive), Path.GetFileName(stagedArchive));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "tar.exe"), commands[0].FileName);
        Assert.Equal(new[] { "-tf", stagedArchive }, commands[0].ArgumentList);
        Assert.Equal(new[] { "-tvf", stagedArchive }, commands[1].ArgumentList);
        Assert.Equal(executable, commands[2].FileName);
        Assert.Equal(new[] { "x", "-y", "-aoa", "-consolemode:utf8", "-o:" + destination, "-", stagedArchive },
            commands[2].ArgumentList);
        Assert.All(commands, command =>
        {
            Assert.False(command.UseShellExecute);
            Assert.True(command.CreateNoWindow);
            Assert.True(command.RedirectStandardInput);
            Assert.True(command.RedirectStandardOutput);
            Assert.True(command.RedirectStandardError);
            Assert.Equal("utf-8", command.StandardOutputEncoding!.WebName);
            Assert.Equal("utf-8", command.StandardErrorEncoding!.WebName);
        });
        Assert.False(File.Exists(stagedArchive));
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagedArchive)));
    }

    [Theory]
    [InlineData("../outside.ini")]
    [InlineData("Mod/../../outside.ini")]
    public void Extract_RejectsTraversalBeforeStartingBandizip(string entry)
    {
        int calls = 0;
        Assert.Throws<InvalidDataException>(() => BandizipArchiveExtractor.Extract(
            "bz.exe", _archivePath, _root, _ => { calls++; return (0, entry, ""); }));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("lrwxrwxrwx 0 0 0 1 link -> ../outside")]
    [InlineData("hrw-r--r-- 0 0 0 1 link link to outside")]
    [InlineData("-rw-r--r-- 0 0 0 0 link link to outside")]
    [InlineData("crw-r--r-- 0 0 0 1 device")]
    [InlineData("prw-r--r-- 0 0 0 1 pipe")]
    [InlineData("unexpected listing format")]
    [InlineData("  ")]
    public void Extract_RejectsLinksAndUnknownEntryTypesBeforeStartingBandizip(string detail)
    {
        int calls = 0;
        Assert.Throws<InvalidDataException>(() => BandizipArchiveExtractor.Extract(
            "bz.exe", _archivePath, _root, _ => (++calls == 1 ? (0, "link", "") : (0, detail, ""))));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("mod.ini", "")]
    [InlineData("mod.ini", "-rw-r--r-- mod.ini\n-rw-r--r-- extra.ini")]
    public void Extract_RejectsEmptyOrInconsistentListings(string names, string details)
    {
        int calls = 0;
        Assert.Throws<InvalidDataException>(() => BandizipArchiveExtractor.Extract(
            "bz.exe", _archivePath, _root, _ => (++calls == 1 ? (0, names, "") : (0, details, ""))));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Extract_AllowsRegularFilenameContainingLinkAnnotationWords()
    {
        int calls = 0;
        BandizipArchiveExtractor.Extract("bz.exe", _archivePath, _root, _ => ++calls switch
        {
            1 => (0, "notes link to mod.txt", ""),
            2 => (0, "-rw-r--r-- 0 0 0 10 notes link to mod.txt", ""),
            _ => (0, "", "")
        });
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(1, "Cannot inspect archive")]
    [InlineData(2, "Cannot inspect links")]
    [InlineData(1, "")]
    public void Extract_StopsWhenEitherInspectionFails(int failedCall, string error)
    {
        int calls = 0;
        var exception = Assert.Throws<InvalidDataException>(() => BandizipArchiveExtractor.Extract(
            "bz.exe", _archivePath, _root, _ => (++calls == failedCall ? (1, "", error) : (0, "mod.ini", ""))));
        Assert.Equal(failedCall, calls);
        Assert.Contains(error.Length == 0 ? "Windows tar could not inspect" : error, exception.Message);
    }

    [Theory]
    [InlineData("", "CRC error", "CRC error")]
    [InlineData("Invalid password", "", "Invalid password")]
    [InlineData("", "", "Bandizip could not extract")]
    public void Extract_ReportsNonzeroExitFromBandizip(string output, string error, string expectedMessage)
    {
        int calls = 0;
        var exception = Assert.Throws<InvalidOperationException>(() => BandizipArchiveExtractor.Extract(
            "bz.exe", _archivePath, _root, _ => ++calls switch
            {
                1 => (0, "mod.ini", ""),
                2 => (0, "-rw-r--r-- mod.ini", ""),
                _ => (1, output, error)
            }));
        Assert.Equal(3, calls);
        Assert.Contains(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData("526172211A070100")]
    [InlineData("4D5A0000")]
    public void Extract_RejectsUninspectableRar5OrSfxBeforeRunningAnyTool(string bytes)
    {
        File.WriteAllBytes(_archivePath, Convert.FromHexString(bytes));
        Assert.Throws<InvalidDataException>(() => BandizipArchiveExtractor.Extract(
            "bz.exe", _archivePath, _root, _ => throw new Exception("Preflight should stop before starting a tool.")));
    }

    [Fact]
    public void Extract_UsesLockedSnapshotEvenWhenSourceChangesAfterInspection()
    {
        byte[] original = File.ReadAllBytes(_archivePath);
        string? stagedArchive = null;
        int calls = 0;

        BandizipArchiveExtractor.Extract("bz.exe", _archivePath, _root, startInfo =>
        {
            string input = startInfo.ArgumentList[^1];
            stagedArchive ??= input;
            Assert.NotEqual(_archivePath, input);
            Assert.Equal(stagedArchive, input);
            Assert.Equal(original, File.ReadAllBytes(input));
            if (OperatingSystem.IsWindows())
            {
                Assert.Throws<IOException>(() => File.WriteAllText(input, "replaced"));
                Assert.Throws<IOException>(() => File.Delete(input));
            }
            if (++calls == 1)
            {
                File.WriteAllText(_archivePath, "the source changed after inspection");
                return (0, "mod.ini", "");
            }
            return calls == 2 ? (0, "-rw-r--r-- mod.ini", "") : (0, "", "");
        });

        Assert.Equal(3, calls);
        Assert.Equal("the source changed after inspection", File.ReadAllText(_archivePath));
        Assert.False(File.Exists(stagedArchive));
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagedArchive)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Extract_CleansSnapshotWhenAnArchiveToolTimesOut(int failedCall)
    {
        string? stagedArchive = null;
        int calls = 0;
        var failure = new TimeoutException("The archive tool exceeded its time limit.");

        Exception actual = Assert.Throws<TimeoutException>(() => BandizipArchiveExtractor.Extract(
            "bz.exe", _archivePath, _root, startInfo =>
            {
                stagedArchive = startInfo.ArgumentList[^1];
                if (++calls == failedCall)
                {
                    throw failure;
                }
                return calls == 1 ? (0, "mod.ini", "") : (0, "-rw-r--r-- mod.ini", "");
            }));

        Assert.Same(failure, actual);
        Assert.Equal(failedCall, calls);
        Assert.False(File.Exists(stagedArchive));
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagedArchive)));
        Assert.True(File.Exists(_archivePath));
    }

    [Fact]
    public void Extract_PreservesTimeoutWhenAStillRunningReaderPreventsCleanup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? stagedArchive = null;
        FileStream? remainingReader = null;
        var failure = new TimeoutException("Termination of the archive tool could not be confirmed.");
        try
        {
            var actual = Assert.Throws<TimeoutException>(() => BandizipArchiveExtractor.Extract(
                "bz.exe", _archivePath, _root, startInfo =>
                {
                    stagedArchive = startInfo.ArgumentList[^1];
                    remainingReader = new FileStream(stagedArchive, FileMode.Open, FileAccess.Read, FileShare.Read);
                    throw failure;
                }));

            Assert.Same(failure, actual);
            Assert.IsType<IOException>(actual.Data[BandizipArchiveExtractor.StagingCleanupFailureKey]);
            Assert.True(File.Exists(stagedArchive));
            Assert.True(File.Exists(_archivePath));
        }
        finally
        {
            remainingReader?.Dispose();
            if (stagedArchive is not null)
            {
                File.Delete(stagedArchive);
                Directory.Delete(Path.GetDirectoryName(stagedArchive)!);
            }
        }
    }

    private string CreateDirectory(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
