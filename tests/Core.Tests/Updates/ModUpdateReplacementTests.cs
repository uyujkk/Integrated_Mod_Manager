using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Updates;

namespace IntegratedModManager.Core.Tests;

public sealed class ModUpdateReplacementTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "imm-update-tests", Guid.NewGuid().ToString("N"));
    private string Repo => Path.Combine(root, "仓库 with spaces");
    private string Mod => Path.Combine(Repo, "角色", "原 Mod");
    private string Payload => Path.Combine(root, "payload");
    private string Backups => Path.Combine(root, "backups");

    public ModUpdateReplacementTests()
    {
        Directory.CreateDirectory(Mod);
        Directory.CreateDirectory(Payload);
        File.WriteAllText(Path.Combine(Mod, "old.ini"), "user-edited original");
        File.WriteAllText(Path.Combine(Payload, "new.ini"), "new version");
    }

    [Fact]
    public void ReplacementKeepsPathAndFullOriginalBackupWithoutMerging()
    {
        Directory.CreateDirectory(Path.Combine(Payload, "textures"));
        File.WriteAllText(Path.Combine(Payload, "textures", "x.dds"), "texture");
        Directory.CreateDirectory(Path.Combine(Mod, "empty"));
        var result = ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "original tracking");
        Assert.Equal(Mod, result.ModPath);
        Assert.False(File.Exists(Path.Combine(Mod, "old.ini")));
        Assert.Equal("new version", File.ReadAllText(Path.Combine(Mod, "new.ini")));
        Assert.Equal("texture", File.ReadAllText(Path.Combine(Mod, "textures", "x.dds")));
        Assert.Equal("user-edited original", File.ReadAllText(Path.Combine(result.BackupPath, "old.ini")));
        Assert.True(Directory.Exists(Path.Combine(result.BackupPath, "empty")));
        Assert.Contains("original tracking", File.ReadAllText(Path.Combine(Path.GetDirectoryName(result.BackupPath)!, "update.json")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Mod)!, ".imm-update-stage-*"));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("empty-directory")]
    public void ChangesAfterConfirmationAbortAndPreserveUserWork(string change)
    {
        var snapshot = ModUpdateReplacement.Capture(Repo, Mod);
        string old = Path.Combine(Mod, "old.ini");
        switch(change)
        {
            case "edit": File.WriteAllText(old, "later edit"); break;
            case "add": File.WriteAllText(Path.Combine(Mod, "added.ini"), "new file"); break;
            case "delete": File.Delete(old); break;
            case "rename": File.Move(old, Path.Combine(Mod, "renamed.ini")); break;
            case "empty-directory": Directory.CreateDirectory(Path.Combine(Mod, "new empty")); break;
        }
        var changed = ModUpdateReplacement.Capture(Repo, Mod);
        Assert.Throws<IOException>(() => ModUpdateReplacement.Replace(snapshot, Payload, Backups, "{}"));
        Assert.Equal(changed.Fingerprint, ModUpdateReplacement.Capture(Repo, Mod).Fingerprint);
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Mod)!, ".imm-update-stage-*"));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void CanceledUpdateLeavesOriginalAndNoStaging()
    {
        var snapshot = ModUpdateReplacement.Capture(Repo, Mod);
        Assert.Throws<OperationCanceledException>(() => ModUpdateReplacement.Replace(snapshot, Payload, Backups, "{}", new CancellationToken(true)));
        Assert.Equal(snapshot, ModUpdateReplacement.Capture(Repo, Mod));
        Assert.False(Directory.Exists(Backups));
    }

    [Theory]
    [InlineData("same")]
    [InlineData("inside")]
    [InlineData("parent")]
    public void OverlappingPayloadIsRejected(string kind)
    {
        string path = kind == "same" ? Mod : kind == "parent" ? Repo : Path.Combine(Mod, "payload");
        Assert.Throws<IOException>(() => ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), path, Backups, "{}"));
        Assert.True(File.Exists(Path.Combine(Mod, "old.ini")));
    }

    [Theory]
    [InlineData("inside")]
    [InlineData("same")]
    [InlineData("parent")]
    public void BackupCannotOverlapRepository(string kind)
    {
        string path = kind == "inside" ? Path.Combine(Repo, "backup") : kind == "same" ? Repo : root;
        Assert.Throws<IOException>(() => ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, path, "{}"));
    }

    [Fact]
    public void EmptyReplacementIsRejected()
    {
        File.Delete(Path.Combine(Payload, "new.ini"));
        Directory.CreateDirectory(Path.Combine(Payload, "empty"));
        Assert.Throws<InvalidDataException>(() => ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}"));
        Assert.True(File.Exists(Path.Combine(Mod, "old.ini")));
    }

    [Fact]
    public void MissingOriginalIsRejected()
    {
        var snapshot = ModUpdateReplacement.Capture(Repo, Mod);
        Directory.Move(Mod, Mod + "-renamed");
        Assert.Throws<DirectoryNotFoundException>(() => ModUpdateReplacement.Replace(snapshot, Payload, Backups, "{}"));
    }

    [Fact]
    public void CaptureRejectsOutsideRepositoryAndRootItself()
    {
        Assert.Throws<IOException>(() => ModUpdateReplacement.Capture(Repo, Payload));
        Assert.Throws<IOException>(() => ModUpdateReplacement.Capture(Repo, Repo));
        Assert.Throws<ArgumentException>(() => ModUpdateReplacement.Capture("", Mod));
    }

    [Fact]
    public void LinkedInputsAreRejectedWithoutTouchingTheirTargets()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(Repo, "link");
        DirectoryLinkDeployment.CreateJunction(link, Payload);
        try { Assert.Throws<IOException>(() => ModUpdateReplacement.Capture(Repo, link)); }
        finally { DirectoryLinkDeployment.RemoveLink(link); }
        link = Path.Combine(Payload, "nested-link");
        DirectoryLinkDeployment.CreateJunction(link, Mod);
        try { Assert.Throws<IOException>(() => ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}")); }
        finally { DirectoryLinkDeployment.RemoveLink(link); }
        Assert.Equal("user-edited original", File.ReadAllText(Path.Combine(Mod, "old.ini")));
    }

    [Fact]
    public void ExternalDeployedJunctionStillResolvesAfterReplacement()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(root, "deployed-link");
        DirectoryLinkDeployment.CreateJunction(link, Mod);
        try
        {
            ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}");
            Assert.Equal("new version", File.ReadAllText(Path.Combine(link, "new.ini")));
        }
        finally { DirectoryLinkDeployment.RemoveLink(link); }
    }

    [Fact]
    public void PreviewMissingFromTheUpdateIsCarriedOver()
    {
        // Issue #14: the update archive has no images, the old folder had a downloaded preview.
        File.WriteAllBytes(Path.Combine(Mod, "preview.jpg"), [1, 2, 3]);
        var result = ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}");
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Mod, "preview.jpg")));
        Assert.Equal(["preview.jpg"], result.PreservedImages);
        Assert.True(File.Exists(Path.Combine(result.BackupPath, "preview.jpg")));
        Assert.False(File.Exists(Path.Combine(Mod, "old.ini")));
    }

    [Fact]
    public void ConflictingPreviewKeepsTheUpdateAndSavesTheOldBeside()
    {
        File.WriteAllBytes(Path.Combine(Mod, "preview.png"), [9, 9]);
        File.WriteAllBytes(Path.Combine(Payload, "preview.png"), [1]);
        var result = ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}");
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(Mod, "preview.png")));
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(Mod, "preview.before-update.png")));
        Assert.Equal(["preview.before-update.png"], result.PreservedImages);
    }

    [Fact]
    public void IdenticalPreviewIsNotDuplicatedAndOtherImagesAreKept()
    {
        File.WriteAllBytes(Path.Combine(Mod, "preview.png"), [4]);
        File.WriteAllBytes(Path.Combine(Payload, "preview.png"), [4]);
        File.WriteAllBytes(Path.Combine(Mod, "My Custom Shot.WEBP"), [5]);
        var result = ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}");
        Assert.False(File.Exists(Path.Combine(Mod, "preview.before-update.png")));
        Assert.Equal(["My Custom Shot.WEBP"], result.PreservedImages);
    }

    [Fact]
    public void ImagesInSubfoldersAndNonImagesAreNotCarriedOver()
    {
        Directory.CreateDirectory(Path.Combine(Mod, "textures"));
        File.WriteAllBytes(Path.Combine(Mod, "textures", "old.png"), [7]);
        File.WriteAllText(Path.Combine(Mod, "readme.txt"), "old notes");
        var result = ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}");
        Assert.Empty(result.PreservedImages);
        Assert.False(Directory.Exists(Path.Combine(Mod, "textures")));
        Assert.False(File.Exists(Path.Combine(Mod, "readme.txt")));
    }

    [Fact]
    public void RepeatedConflictsGetDistinctNames()
    {
        File.WriteAllBytes(Path.Combine(Mod, "cover.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(Payload, "cover.jpg"), [2]);
        File.WriteAllBytes(Path.Combine(Payload, "cover.before-update.jpg"), [3]);
        var result = ModUpdateReplacement.Replace(ModUpdateReplacement.Capture(Repo, Mod), Payload, Backups, "{}");
        Assert.Equal(["cover.before-update (2).jpg"], result.PreservedImages);
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(Mod, "cover.before-update.jpg")));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(Mod, "cover.before-update (2).jpg")));
    }

    [Fact]
    public void FailedReplacementLeavesTheOriginalPreviewUntouched()
    {
        File.WriteAllBytes(Path.Combine(Mod, "preview.jpg"), [1, 2]);
        var snapshot = ModUpdateReplacement.Capture(Repo, Mod);
        File.WriteAllText(Path.Combine(Mod, "old.ini"), "changed after confirmation");
        Assert.Throws<IOException>(() => ModUpdateReplacement.Replace(snapshot, Payload, Backups, "{}"));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(Mod, "preview.jpg")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Mod)!, ".imm-update-stage-*"));
    }

    public void Dispose() { if(Directory.Exists(root)) Directory.Delete(root, true); }
}
