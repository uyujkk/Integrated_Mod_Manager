using IntegratedModManager.Core.Deployment;

namespace IntegratedModManager.Core.Tests;

// Issues #105 (overlapping repository and loader folders) and #109 (an unreachable loader folder before a delete).
public sealed class DeploymentSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-safety-" + Guid.NewGuid().ToString("N"));

    public DeploymentSafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose() => DeleteTree(_root);

    // Links first, without following them: deleting a junction's folder recursively is refused.
    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        if (DirectoryLinkDeployment.IsDirectoryLink(root)) { DirectoryLinkDeployment.RemoveLink(root); return; }
        foreach (string folder in Directory.GetDirectories(root)) DeleteTree(folder);
        Directory.Delete(root, recursive: true);
    }

    private string Folder(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Compare_LoaderSetToTheCharacterFolderIsInsideTheRepository()
    {
        // The reproduction of #105: the loader folder is Repository/TestCharacter, so TestModA would deploy onto itself.
        string repository = Folder("Repository");
        string character = Folder(@"Repository\TestCharacter");
        string mod = Folder(@"Repository\TestCharacter\TestModA");

        Assert.Equal(PathOverlap.TargetInsideSource, DeploymentPathSeparation.Compare(repository, character));
        Assert.Equal(PathOverlap.Same, DeploymentPathSeparation.Compare(mod, ModDeploymentLayout.DeployedPath(character, mod)));
    }

    [Fact]
    public void Compare_TheSameFolderAndBothNestings()
    {
        string repository = Folder("Repository");
        string inner = Folder(@"Repository\Loader");

        Assert.Equal(PathOverlap.Same, DeploymentPathSeparation.Compare(repository, repository + Path.DirectorySeparatorChar));
        Assert.Equal(PathOverlap.TargetInsideSource, DeploymentPathSeparation.Compare(repository, inner));
        Assert.Equal(PathOverlap.SourceInsideTarget, DeploymentPathSeparation.Compare(inner, repository));
    }

    [Fact]
    public void Compare_SeparateFoldersAreFine()
    {
        Assert.Equal(PathOverlap.None, DeploymentPathSeparation.Compare(Folder("Repository"), Folder("Loader")));
        // A sibling whose name starts the same is not inside.
        Assert.Equal(PathOverlap.None, DeploymentPathSeparation.Compare(Folder("Mods"), Folder("Mods2")));
    }

    [Fact]
    public void Compare_SeesThroughAJunctionToTheSameFolder()
    {
        string repository = Folder("Repository");
        string link = Path.Combine(_root, "LoaderLink");
        DirectoryLinkDeployment.CreateJunction(link, repository);

        Assert.Equal(PathOverlap.Same, DeploymentPathSeparation.Compare(repository, link));
    }

    [Fact]
    public void Compare_SeesThroughAJunctionIntoTheRepository()
    {
        Folder(@"Repository\TestCharacter");
        string link = Path.Combine(_root, "LoaderLink");
        DirectoryLinkDeployment.CreateJunction(link, Path.Combine(_root, "Repository", "TestCharacter"));

        Assert.Equal(PathOverlap.TargetInsideSource, DeploymentPathSeparation.Compare(Path.Combine(_root, "Repository"), link));
    }

    [Fact]
    public void EnsureSeparate_NamesBothPaths()
    {
        string repository = Folder("Repository");
        string character = Folder(@"Repository\TestCharacter");

        var error = Assert.Throws<InvalidDataException>(() => DeploymentPathSeparation.EnsureSeparate(repository, character));
        Assert.Contains(character, error.Message);
    }

    [Fact]
    public void ScanLinks_FindsTheLinkWhenTheLoaderIsThere()
    {
        string mod = Folder(@"Repository\TestCharacter\TestModA");
        string loader = Folder("Loader");
        DirectoryLinkDeployment.CreateJunction(Path.Combine(loader, "TestModA"), mod);

        LinkScanResult scan = DirectoryLinkDeployment.ScanLinksTargeting(mod, loader);

        Assert.Equal(LinkScanStatus.Scanned, scan.Status);
        Assert.True(scan.IsComplete);
        Assert.Single(scan.Links);
    }

    [Fact]
    public void ScanLinks_AScannedLoaderWithoutLinksAllowsTheDelete()
    {
        LinkScanResult scan = DirectoryLinkDeployment.ScanLinksTargeting(Folder(@"Repository\TestCharacter\TestModA"), Folder("Loader"));

        Assert.Equal(LinkScanStatus.Scanned, scan.Status);
        Assert.True(scan.IsComplete);
        Assert.Empty(scan.Links);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ScanLinks_NoLoaderConfiguredAllowsTheDelete(string? loader)
    {
        LinkScanResult scan = DirectoryLinkDeployment.ScanLinksTargeting(Folder(@"Repository\TestCharacter\TestModA"), loader);

        Assert.Equal(LinkScanStatus.NotConfigured, scan.Status);
        Assert.True(scan.IsComplete);
    }

    [Fact]
    public void ScanLinks_AnUnreachableLoaderBlocksTheDeleteUntilItIsBack()
    {
        // The reproduction of #109: 1 link, the loader moved away (0 links but unavailable, not empty), then 1 again.
        string mod = Folder(@"Repository\TestCharacter\TestModA");
        string loader = Folder("Loader");
        DirectoryLinkDeployment.CreateJunction(Path.Combine(loader, "TestModA"), mod);
        Assert.Single(DirectoryLinkDeployment.ScanLinksTargeting(mod, loader).Links);

        string moved = Path.Combine(_root, "LoaderMoved");
        Directory.Move(loader, moved);
        LinkScanResult unreachable = DirectoryLinkDeployment.ScanLinksTargeting(mod, loader);
        Assert.Equal(LinkScanStatus.Unavailable, unreachable.Status);
        Assert.False(unreachable.IsComplete);
        Assert.Equal(loader, unreachable.Detail);
        Assert.True(Directory.Exists(mod));

        Directory.Move(moved, loader);
        LinkScanResult restored = DirectoryLinkDeployment.ScanLinksTargeting(mod, loader);
        Assert.Equal(LinkScanStatus.Scanned, restored.Status);
        Assert.Single(restored.Links);
    }

    [Fact]
    public void ScanLinks_AFailedScanBlocksTheDelete()
    {
        // A loader "folder" that is a file cannot be listed.
        string file = Path.Combine(_root, "not-a-folder.txt");
        File.WriteAllText(file, "x");

        LinkScanResult scan = DirectoryLinkDeployment.ScanLinksTargeting(Folder(@"Repository\TestCharacter\TestModA"), file);

        Assert.False(scan.IsComplete);
    }
}
