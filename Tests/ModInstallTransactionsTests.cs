using System.Text.Json;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Tests;

public sealed class ModInstallTransactionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-install-tx-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];
    private string Source => Path.Combine(_root, "repository");
    private string Target => Path.Combine(_root, "loader", "Mods");
    private string Backups => Path.Combine(_root, "backups");

    public ModInstallTransactionsTests()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Target);
        Directory.CreateDirectory(Backups);
    }

    public void Dispose()
    {
        foreach (string link in _links.Where(Directory.Exists))
        {
            DirectoryLinkDeployment.RemoveLink(link);
        }

        // Rollback recreates links; remove any left in the loader before deleting the tree.
        if (Directory.Exists(Target))
        {
            foreach (string directory in Directory.GetDirectories(Target).Where(DirectoryLinkDeployment.IsDirectoryLink))
            {
                DirectoryLinkDeployment.RemoveLink(directory);
            }
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Create_BacksUpCopiesRecordsLinksAndMissingTargets()
    {
        string copied = WriteMod(Target, "Copied", ("mod.ini", "original"), ("tex/a.dds", "a"));
        string repositoryMod = WriteMod(Path.Combine(Source, "Char"), "Linked", ("mod.ini", "linked"));
        string linked = Link(Path.Combine(Target, "Linked"), repositoryMod);
        string missing = Path.Combine(Target, "New");

        string path = Create(copied, linked, missing, copied);
        ModInstallTransaction transaction = ModInstallTransactions.ReadManifest(path);

        Assert.Equal("Install test", transaction.Description);
        Assert.Equal("repo-1", transaction.RepositoryId);
        Assert.Equal(3, transaction.Entries.Count);
        ModInstallBackupEntry copiedEntry = transaction.Entries.Single(entry => entry.TargetPath == copied);
        Assert.True(copiedEntry.ExistedBefore);
        Assert.Null(copiedEntry.LinkTarget);
        Assert.Equal("original", File.ReadAllText(Path.Combine(path, copiedEntry.BackupRelativePath, "mod.ini")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(path, copiedEntry.BackupRelativePath, "tex", "a.dds")));
        ModInstallBackupEntry linkEntry = transaction.Entries.Single(entry => entry.TargetPath == linked);
        Assert.Equal(repositoryMod, linkEntry.LinkTarget);
        Assert.Equal(string.Empty, linkEntry.BackupRelativePath);
        ModInstallBackupEntry missingEntry = transaction.Entries.Single(entry => entry.TargetPath == missing);
        Assert.False(missingEntry.ExistedBefore);
    }

    [Fact]
    public void Create_KeepsTheManifestFormatOfEarlierVersions()
    {
        string path = Create(WriteMod(Target, "Copied", ("mod.ini", "x")));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, ModInstallTransactions.ManifestFileName)));
        JsonElement root = manifest.RootElement;
        foreach (string property in new[] { "Id", "Description", "RepositoryId", "CreatedAtUtc", "Entries" })
        {
            Assert.True(root.TryGetProperty(property, out _), property);
        }

        JsonElement entry = root.GetProperty("Entries")[0];
        foreach (string property in new[] { "TargetPath", "ExistedBefore", "BackupRelativePath", "LinkTarget" })
        {
            Assert.True(entry.TryGetProperty(property, out _), property);
        }
    }

    [Fact]
    public void ReadManifest_AcceptsAManifestWrittenByAnEarlierVersion()
    {
        string path = Path.Combine(Backups, "legacy");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ModInstallTransactions.ManifestFileName), """
            {
              "Id": "abc",
              "Description": "Copy Mod",
              "RepositoryId": "repo-1",
              "CreatedAtUtc": "2026-10-01T00:00:00+00:00",
              "Entries": [ { "TargetPath": "C:\\Mods\\A", "ExistedBefore": false, "BackupRelativePath": "", "LinkTarget": null } ]
            }
            """);

        ModInstallTransaction transaction = ModInstallTransactions.ReadManifest(path);

        Assert.Equal("Copy Mod", transaction.Description);
        Assert.Equal(@"C:\Mods\A", Assert.Single(transaction.Entries).TargetPath);
    }

    [Fact]
    public void Create_RejectsTargetsOutsideTheLoaderFolderAndLeavesNoBackup()
    {
        string outside = WriteMod(_root, "Outside", ("mod.ini", "x"));

        var error = Assert.Throws<InvalidOperationException>(() => Create(outside));

        Assert.Equal(InstallTransactionErrorKind.TargetOutsideTargetRoot, ErrorTags.KindOf<InstallTransactionErrorKind>(error));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Backups));
    }

    [Fact]
    public void Create_RejectsAJunctionThatPointsOutsideTheRepository()
    {
        string foreign = WriteMod(_root, "Foreign", ("mod.ini", "x"));
        string linked = Link(Path.Combine(Target, "Foreign"), foreign);

        var error = Assert.Throws<InvalidOperationException>(() => Create(linked));

        Assert.Equal(InstallTransactionErrorKind.LinkOutsideRepository, ErrorTags.KindOf<InstallTransactionErrorKind>(error));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Backups));
    }

    [Fact]
    public void Rollback_RestoresCopiesLinksAndRemovesNewFolders()
    {
        string copied = WriteMod(Target, "Copied", ("mod.ini", "original"));
        string repositoryMod = WriteMod(Path.Combine(Source, "Char"), "Linked", ("mod.ini", "linked"));
        string linked = Link(Path.Combine(Target, "Linked"), repositoryMod);
        string added = Path.Combine(Target, "New");
        string path = Create(copied, linked, added);

        // Simulate the operation the transaction protects.
        File.WriteAllText(Path.Combine(copied, "mod.ini"), "changed");
        File.WriteAllText(Path.Combine(copied, "extra.ini"), "extra");
        DirectoryLinkDeployment.RemoveLink(linked);
        WriteMod(Target, "Linked", ("mod.ini", "replaced copy"));
        WriteMod(Target, "New", ("mod.ini", "new"));

        Rollback(path);

        Assert.Equal("original", File.ReadAllText(Path.Combine(copied, "mod.ini")));
        Assert.False(File.Exists(Path.Combine(copied, "extra.ini")));
        Assert.True(DirectoryLinkDeployment.IsDirectoryLink(linked));
        Assert.Equal(repositoryMod, DirectoryLinkDeployment.TryGetTarget(linked));
        Assert.False(Directory.Exists(added));
        Assert.Equal("linked", File.ReadAllText(Path.Combine(repositoryMod, "mod.ini")));
        Assert.Equal(new[] { "Copied", "Linked" }, Directory.GetDirectories(Target).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Rollback_MissingOrEmptyManifestIsTagged()
    {
        string empty = Path.Combine(Backups, "empty");
        Directory.CreateDirectory(empty);

        var missing = Assert.Throws<FileNotFoundException>(() => ModInstallTransactions.ReadManifest(empty));
        File.WriteAllText(Path.Combine(empty, ModInstallTransactions.ManifestFileName), "null");
        var invalid = Assert.Throws<InvalidDataException>(() => ModInstallTransactions.ReadManifest(empty));

        Assert.Equal(InstallTransactionErrorKind.ManifestMissing, ErrorTags.KindOf<InstallTransactionErrorKind>(missing));
        Assert.Equal(InstallTransactionErrorKind.ManifestInvalid, ErrorTags.KindOf<InstallTransactionErrorKind>(invalid));
    }

    [Fact]
    public void Rollback_TargetOutsideTheLoaderStopsBeforeAnyChange()
    {
        string copied = WriteMod(Target, "Copied", ("mod.ini", "current"));
        var transaction = Manifest(
            Entry(copied, existed: false),
            Entry(Path.Combine(_root, "Elsewhere"), existed: false));

        AssertRollbackRejected<InvalidOperationException>(transaction, InstallTransactionErrorKind.RestoreTargetOutsideTargetRoot);

        Assert.Equal("current", File.ReadAllText(Path.Combine(copied, "mod.ini")));
    }

    [Fact]
    public void Rollback_DuplicateTargetsAreRejected()
    {
        string target = Path.Combine(Target, "A");
        AssertRollbackRejected<InvalidDataException>(Manifest(Entry(target, existed: false), Entry(target, existed: false)), InstallTransactionErrorKind.DuplicateTargets);
    }

    [Fact]
    public void Rollback_AFileInPlaceOfATargetIsRejected()
    {
        string target = Path.Combine(Target, "A");
        File.WriteAllText(target, "file");

        AssertRollbackRejected<IOException>(Manifest(Entry(target, existed: false)), InstallTransactionErrorKind.TargetOccupiedByFile);

        Assert.Equal("file", File.ReadAllText(target));
    }

    [Fact]
    public void Rollback_MissingBackupCopyIsRejected()
    {
        AssertRollbackRejected<DirectoryNotFoundException>(
            Manifest(Entry(Path.Combine(Target, "A"), existed: true, backup: Path.Combine("files", "0000"))),
            InstallTransactionErrorKind.BackupIncomplete);
    }

    [Fact]
    public void Rollback_EntryWithoutBackupOrLinkIsInvalid()
    {
        AssertRollbackRejected<InvalidDataException>(
            Manifest(Entry(Path.Combine(Target, "A"), existed: true)),
            InstallTransactionErrorKind.ManifestInvalid);
    }

    [Fact]
    public void Rollback_LinkTargetOutsideTheRepositoryIsRejected()
    {
        string foreign = WriteMod(_root, "Foreign", ("mod.ini", "x"));
        AssertRollbackRejected<InvalidDataException>(
            Manifest(Entry(Path.Combine(Target, "A"), existed: true, linkTarget: foreign)),
            InstallTransactionErrorKind.InvalidLinkTarget);
    }

    [Fact]
    public void Rollback_LeavesNoStagingOrDisplacedFoldersBehind()
    {
        string copied = WriteMod(Target, "Copied", ("mod.ini", "original"));
        string path = Create(copied, Path.Combine(Target, "New"));
        File.WriteAllText(Path.Combine(copied, "mod.ini"), "changed");
        WriteMod(Target, "New", ("mod.ini", "new"));

        Rollback(path);

        Assert.Empty(Directory.GetFileSystemEntries(Target, ".*"));
    }

    private void AssertRollbackRejected<TException>(ModInstallTransaction transaction, InstallTransactionErrorKind kind)
        where TException : Exception
    {
        string path = WriteManifest(transaction);
        string[] before = Directory.GetFileSystemEntries(Target);

        var error = Assert.ThrowsAny<TException>(() => ModInstallTransactions.Rollback(path, transaction, Target, Source));

        Assert.Equal(kind, ErrorTags.KindOf<InstallTransactionErrorKind>(error));
        Assert.Equal(before, Directory.GetFileSystemEntries(Target));
    }

    private string Create(params string[] targets) =>
        ModInstallTransactions.Create(Backups, "Install test", "repo-1", Target, Source, targets);

    private void Rollback(string path) =>
        ModInstallTransactions.Rollback(path, ModInstallTransactions.ReadManifest(path), Target, Source);

    private static ModInstallTransaction Manifest(params ModInstallBackupEntry[] entries) =>
        new() { Description = "test", RepositoryId = "repo-1", Entries = [.. entries] };

    private static ModInstallBackupEntry Entry(string target, bool existed, string backup = "", string? linkTarget = null) =>
        new() { TargetPath = target, ExistedBefore = existed, BackupRelativePath = backup, LinkTarget = linkTarget };

    private string WriteManifest(ModInstallTransaction transaction)
    {
        string path = Path.Combine(Backups, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ModInstallTransactions.ManifestFileName), JsonSerializer.Serialize(transaction));
        return path;
    }

    private string Link(string link, string target)
    {
        DirectoryLinkDeployment.CreateJunction(link, target);
        _links.Add(link);
        return link;
    }

    private static string WriteMod(string parent, string name, params (string RelativePath, string Content)[] files)
    {
        string mod = Path.Combine(parent, name);
        Directory.CreateDirectory(mod);
        foreach ((string relativePath, string content) in files)
        {
            string file = Path.Combine(mod, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        return mod;
    }
}
