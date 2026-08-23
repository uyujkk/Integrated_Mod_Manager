using System.IO.Compression;
using Xunit;

public sealed class LocalUpdateAgentTests
{
    [Fact]
    public void ParseArguments_UsesCaseInsensitiveKeysAndLastValue()
    {
        Dictionary<string, string> result = LocalUpdateAgent.ParseArguments(
            ["--package", "old.zip", "--PACKAGE", "new.zip", "ignored"]);

        Assert.Equal("new.zip", result["package"]);
        Assert.Single(result);
    }

    [Fact]
    public void NormalizeVersionLabel_ReplacesInvalidFileNameCharacters()
    {
        string result = LocalUpdateAgent.NormalizeVersionLabel(" 3.8:preview ");

        Assert.DoesNotContain(':', result);
        Assert.Equal("3.8-preview", result);
    }

    [Fact]
    public void ExtractZipSafely_ExtractsRegularFiles()
    {
        using var directory = new TemporaryDirectory();
        string archivePath = Path.Combine(directory.Path, "update.zip");
        string destination = Path.Combine(directory.Path, "payload");
        CreateArchive(archivePath, archive => WriteEntry(archive, "WinUI3/app.dll", "payload"));

        LocalUpdateAgent.ExtractZipSafely(archivePath, destination);

        Assert.Equal("payload", File.ReadAllText(Path.Combine(destination, "WinUI3", "app.dll")));
    }

    [Fact]
    public void ExtractZipSafely_CreatesExplicitDirectoryEntries()
    {
        using var directory = new TemporaryDirectory();
        string archivePath = Path.Combine(directory.Path, "update.zip");
        string destination = Path.Combine(directory.Path, "payload");
        CreateArchive(archivePath, archive => archive.CreateEntry("WinUI3/cache/"));

        LocalUpdateAgent.ExtractZipSafely(archivePath, destination);

        Assert.True(Directory.Exists(Path.Combine(destination, "WinUI3", "cache")));
    }

    [Fact]
    public void ExtractZipSafely_RejectsParentTraversal()
    {
        using var directory = new TemporaryDirectory();
        string archivePath = Path.Combine(directory.Path, "update.zip");
        string destination = Path.Combine(directory.Path, "payload");
        string outside = Path.Combine(directory.Path, "outside.txt");
        CreateArchive(archivePath, archive => WriteEntry(archive, "../outside.txt", "unsafe"));

        Assert.Throws<InvalidDataException>(() => LocalUpdateAgent.ExtractZipSafely(archivePath, destination));
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public void ExtractZipSafely_RejectsSymbolicLinks()
    {
        using var directory = new TemporaryDirectory();
        string archivePath = Path.Combine(directory.Path, "update.zip");
        string destination = Path.Combine(directory.Path, "payload");
        CreateArchive(archivePath, archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry("link");
            entry.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("target");
        });

        Assert.Throws<InvalidDataException>(() => LocalUpdateAgent.ExtractZipSafely(archivePath, destination));
    }

    [Fact]
    public void FindPayloadRoot_FindsNestedReleaseDirectory()
    {
        using var directory = new TemporaryDirectory();
        string payload = Path.Combine(directory.Path, "Integrated_Mod_Manager-v3.8.0");
        Directory.CreateDirectory(Path.Combine(payload, "WinUI3"));
        File.WriteAllText(Path.Combine(payload, "ModFolderCopier.exe"), "launcher");
        File.WriteAllText(Path.Combine(payload, "WinUI3", "ModFolderCopier.WinUI.exe"), "runtime");

        string result = LocalUpdateAgent.FindPayloadRoot(directory.Path);

        Assert.Equal(payload, result, ignoreCase: true);
    }

    [Fact]
    public void FindPayloadRoot_RejectsUnrelatedArchiveContent()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "README.txt"), "not an update");

        Assert.Throws<InvalidDataException>(() => LocalUpdateAgent.FindPayloadRoot(directory.Path));
    }

    [Theory]
    [InlineData("config.ini", true)]
    [InlineData("WinUI3/config.ini", true)]
    [InlineData("WinUI3/cache/app-index.db", true)]
    [InlineData("WinUI3/cache/images/preview.jpg", true)]
    [InlineData("backups/updates/backup.zip", true)]
    [InlineData("cache/app-index.db", false)]
    [InlineData("WinUI3/cache-old/app-index.db", false)]
    [InlineData("config.ini.bak", false)]
    public void IsPreserved_MatchesOnlyConfiguredFilesAndDirectories(string path, bool expected)
    {
        string platformPath = path.Replace('/', Path.DirectorySeparatorChar);

        Assert.Equal(expected, LocalUpdateAgent.IsPreserved(platformPath));
    }

    [Fact]
    public void ReadManagedFileSet_RejectsUnsafeManifestEntry()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, ".managed-files.txt"), "../outside.txt");

        Assert.Throws<InvalidDataException>(() => LocalUpdateAgent.ReadManagedFileSet(directory.Path));
    }

    [Fact]
    public void BuildPayloadManagedFileSet_ExcludesPreservedStateAndIncludesManifest()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "WinUI3"));
        File.WriteAllText(Path.Combine(directory.Path, "WinUI3", "app.dll"), "runtime");
        File.WriteAllText(Path.Combine(directory.Path, "config.ini"), "user state");

        HashSet<string> result = LocalUpdateAgent.BuildPayloadManagedFileSet(directory.Path);

        Assert.Contains(Path.Combine("WinUI3", "app.dll"), result);
        Assert.Contains(".managed-files.txt", result);
        Assert.DoesNotContain("config.ini", result);
    }

    [Fact]
    public void GetPathInsideRoot_RejectsRootedAndSiblingPaths()
    {
        using var directory = new TemporaryDirectory();
        string outside = Path.Combine(Path.GetDirectoryName(directory.Path)!, "outside.txt");

        Assert.Throws<InvalidDataException>(() => LocalUpdateAgent.GetPathInsideRoot(directory.Path, outside));
        Assert.Throws<InvalidDataException>(() => LocalUpdateAgent.GetPathInsideRoot(directory.Path, "../outside.txt"));
    }

    [Fact]
    public void GetRelativePath_ReturnsNestedPathAndRejectsOutsideFile()
    {
        using var directory = new TemporaryDirectory();
        string root = Path.Combine(directory.Path, "root");
        string nested = Path.Combine(root, "WinUI3", "app.dll");
        string outside = Path.Combine(directory.Path, "outside.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);

        Assert.Equal(Path.Combine("WinUI3", "app.dll"), LocalUpdateAgent.GetRelativePath(root, nested));
        Assert.Throws<InvalidOperationException>(() => LocalUpdateAgent.GetRelativePath(root, outside));
    }

    [Fact]
    public void PayloadTransaction_RollbackRestoresReplacedFilesAndRemovesCreatedFiles()
    {
        using var directory = new TemporaryDirectory();
        string payload = Path.Combine(directory.Path, "payload");
        string install = Path.Combine(directory.Path, "install");
        string backup = Path.Combine(directory.Path, "backup");
        Directory.CreateDirectory(Path.Combine(payload, "WinUI3"));
        Directory.CreateDirectory(Path.Combine(install, "WinUI3"));
        File.WriteAllText(Path.Combine(payload, "WinUI3", "app.dll"), "new app");
        File.WriteAllText(Path.Combine(payload, "WinUI3", "new.dll"), "new file");
        File.WriteAllText(Path.Combine(install, "WinUI3", "app.dll"), "old app");
        var createdFiles = new List<string>();

        LocalUpdateAgent.BackupFilesThatWillBeReplaced(payload, install, backup, []);
        LocalUpdateAgent.CopyPayload(payload, install, createdFiles);

        Assert.Equal("new app", File.ReadAllText(Path.Combine(install, "WinUI3", "app.dll")));
        Assert.Equal("new file", File.ReadAllText(Path.Combine(install, "WinUI3", "new.dll")));
        Assert.Single(createdFiles);

        LocalUpdateAgent.RollBackFiles(backup, install, createdFiles);

        Assert.Equal("old app", File.ReadAllText(Path.Combine(install, "WinUI3", "app.dll")));
        Assert.False(File.Exists(Path.Combine(install, "WinUI3", "new.dll")));
    }

    [Fact]
    public void PayloadTransaction_SuccessPreservesConfigurationAndRemovesObsoleteManagedFiles()
    {
        using var directory = new TemporaryDirectory();
        string payload = Path.Combine(directory.Path, "payload");
        string install = Path.Combine(directory.Path, "install");
        string backup = Path.Combine(directory.Path, "backup");
        Directory.CreateDirectory(Path.Combine(payload, "WinUI3"));
        Directory.CreateDirectory(Path.Combine(install, "WinUI3", "cache"));
        File.WriteAllText(Path.Combine(payload, "ModFolderCopier.exe"), "new launcher");
        File.WriteAllText(Path.Combine(payload, "WinUI3", "ModFolderCopier.WinUI.exe"), "new runtime");
        File.WriteAllText(Path.Combine(payload, "WinUI3", "config.ini"), "package config");
        File.WriteAllText(Path.Combine(install, "ModFolderCopier.exe"), "old launcher");
        File.WriteAllText(Path.Combine(install, "WinUI3", "ModFolderCopier.WinUI.exe"), "old runtime");
        File.WriteAllText(Path.Combine(install, "WinUI3", "obsolete.dll"), "obsolete");
        File.WriteAllText(Path.Combine(install, "WinUI3", "config.ini"), "user config");
        File.WriteAllText(Path.Combine(install, "WinUI3", "cache", "page.json"), "user cache");
        File.WriteAllLines(Path.Combine(install, ".managed-files.txt"),
        [
            "ModFolderCopier.exe",
            Path.Combine("WinUI3", "ModFolderCopier.WinUI.exe"),
            Path.Combine("WinUI3", "obsolete.dll"),
            ".managed-files.txt"
        ]);

        HashSet<string> newManagedFiles = LocalUpdateAgent.BuildPayloadManagedFileSet(payload);
        HashSet<string> previousManagedFiles = LocalUpdateAgent.ReadManagedFileSet(install);
        List<string> obsoleteFiles = previousManagedFiles
            .Except(newManagedFiles, StringComparer.OrdinalIgnoreCase)
            .Where(path => !LocalUpdateAgent.IsPreserved(path))
            .ToList();
        var createdFiles = new List<string>();

        LocalUpdateAgent.BackupFilesThatWillBeReplaced(payload, install, backup, obsoleteFiles);
        LocalUpdateAgent.CopyPayload(payload, install, createdFiles);
        LocalUpdateAgent.DeleteObsoleteManagedFiles(install, obsoleteFiles);
        LocalUpdateAgent.WriteManagedFileSet(install, newManagedFiles, createdFiles);
        LocalUpdateAgent.RestorePreservedConfiguration(backup, install);

        Assert.Equal("new launcher", File.ReadAllText(Path.Combine(install, "ModFolderCopier.exe")));
        Assert.Equal("new runtime", File.ReadAllText(Path.Combine(install, "WinUI3", "ModFolderCopier.WinUI.exe")));
        Assert.False(File.Exists(Path.Combine(install, "WinUI3", "obsolete.dll")));
        Assert.Equal("user config", File.ReadAllText(Path.Combine(install, "WinUI3", "config.ini")));
        Assert.Equal("user cache", File.ReadAllText(Path.Combine(install, "WinUI3", "cache", "page.json")));
        Assert.Equal(
            newManagedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            LocalUpdateAgent.ReadManagedFileSet(install).OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void PayloadTransaction_RollbackRestoresDeletedObsoleteFileAndManifest()
    {
        using var directory = new TemporaryDirectory();
        string payload = Path.Combine(directory.Path, "payload");
        string install = Path.Combine(directory.Path, "install");
        string backup = Path.Combine(directory.Path, "backup");
        Directory.CreateDirectory(Path.Combine(payload, "WinUI3"));
        Directory.CreateDirectory(Path.Combine(install, "WinUI3"));
        File.WriteAllText(Path.Combine(payload, "WinUI3", "new.dll"), "new");
        File.WriteAllText(Path.Combine(install, "WinUI3", "obsolete.dll"), "old obsolete");
        File.WriteAllLines(Path.Combine(install, ".managed-files.txt"),
            [Path.Combine("WinUI3", "obsolete.dll"), ".managed-files.txt"]);
        string obsolete = Path.Combine("WinUI3", "obsolete.dll");
        var createdFiles = new List<string>();

        LocalUpdateAgent.BackupFilesThatWillBeReplaced(payload, install, backup, [obsolete]);
        LocalUpdateAgent.CopyPayload(payload, install, createdFiles);
        LocalUpdateAgent.DeleteObsoleteManagedFiles(install, [obsolete]);
        LocalUpdateAgent.WriteManagedFileSet(
            install,
            [Path.Combine("WinUI3", "new.dll"), ".managed-files.txt"],
            createdFiles);

        LocalUpdateAgent.RollBackFiles(backup, install, createdFiles);

        Assert.Equal("old obsolete", File.ReadAllText(Path.Combine(install, obsolete)));
        Assert.False(File.Exists(Path.Combine(install, "WinUI3", "new.dll")));
        Assert.Contains(obsolete, LocalUpdateAgent.ReadManagedFileSet(install));
    }

    [Fact]
    public void ManagedFileOperations_WriteReadAndDeleteOnlyRequestedFiles()
    {
        using var directory = new TemporaryDirectory();
        string install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(Path.Combine(install, "WinUI3"));
        File.WriteAllText(Path.Combine(install, "WinUI3", "obsolete.dll"), "old");
        File.WriteAllText(Path.Combine(install, "WinUI3", "keep.dll"), "keep");
        var createdFiles = new List<string>();
        string[] managed = [Path.Combine("WinUI3", "keep.dll"), ".managed-files.txt"];

        LocalUpdateAgent.WriteManagedFileSet(install, managed, createdFiles);
        HashSet<string> restored = LocalUpdateAgent.ReadManagedFileSet(install);
        LocalUpdateAgent.DeleteObsoleteManagedFiles(install, [Path.Combine("WinUI3", "obsolete.dll")]);

        Assert.Equal(managed.OrderBy(path => path), restored.OrderBy(path => path));
        Assert.Contains(Path.Combine(install, ".managed-files.txt"), createdFiles);
        Assert.False(File.Exists(Path.Combine(install, "WinUI3", "obsolete.dll")));
        Assert.True(File.Exists(Path.Combine(install, "WinUI3", "keep.dll")));
    }

    [Fact]
    public void RestorePreservedConfiguration_ReplacesOnlyPreservedFiles()
    {
        using var directory = new TemporaryDirectory();
        string backup = Path.Combine(directory.Path, "backup");
        string install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(backup, "config.ini"), "restored");
        File.WriteAllText(Path.Combine(install, "config.ini"), "changed");

        LocalUpdateAgent.RestorePreservedConfiguration(backup, install);

        Assert.Equal("restored", File.ReadAllText(Path.Combine(install, "config.ini")));
    }

    [Fact]
    public void CopyPayload_DoesNotInstallPackagedUserState()
    {
        using var directory = new TemporaryDirectory();
        string payload = Path.Combine(directory.Path, "payload");
        string install = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(Path.Combine(payload, "WinUI3", "cache"));
        File.WriteAllText(Path.Combine(payload, "config.ini"), "root config");
        File.WriteAllText(Path.Combine(payload, "WinUI3", "config.ini"), "runtime config");
        File.WriteAllText(Path.Combine(payload, "WinUI3", "cache", "page.json"), "cache");
        File.WriteAllText(Path.Combine(payload, "application.dll"), "application");
        var createdFiles = new List<string>();

        LocalUpdateAgent.CopyPayload(payload, install, createdFiles);

        Assert.True(File.Exists(Path.Combine(install, "application.dll")));
        Assert.False(File.Exists(Path.Combine(install, "config.ini")));
        Assert.False(File.Exists(Path.Combine(install, "WinUI3", "config.ini")));
        Assert.False(File.Exists(Path.Combine(install, "WinUI3", "cache", "page.json")));
    }

    private static void CreateArchive(string path, Action<ZipArchive> build)
    {
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        build(archive);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imm-updater-tests", Guid.NewGuid().ToString("N"));
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
