using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Infrastructure;

namespace IntegratedModManager.Core.Tests;

public sealed class SafeDirectoryTreeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-safe-tree-" + Guid.NewGuid().ToString("N"));

    public SafeDirectoryTreeTests()
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

    [Fact]
    public void Inspect_ListsNestedDirectoriesAndFilesSorted()
    {
        Directory.CreateDirectory(Path.Combine(_root, "b", "inner"));
        Directory.CreateDirectory(Path.Combine(_root, "a"));
        File.WriteAllText(Path.Combine(_root, "b", "inner", "z.ini"), "z");
        File.WriteAllText(Path.Combine(_root, "a", "y.ini"), "y");

        (List<string> directories, List<string> files) = SafeDirectoryTree.Inspect(_root);

        Assert.Equal(
            new[] { Path.Combine(_root, "a"), Path.Combine(_root, "b"), Path.Combine(_root, "b", "inner") },
            directories);
        Assert.Equal(
            new[] { Path.Combine(_root, "a", "y.ini"), Path.Combine(_root, "b", "inner", "z.ini") },
            files);
    }

    [Fact]
    public void Inspect_MissingRootThrowsDirectoryNotFound()
    {
        Assert.Throws<DirectoryNotFoundException>(() => SafeDirectoryTree.Inspect(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Inspect_RejectsAJunctionInsideTheTree()
    {
        string target = Path.Combine(_root, "target");
        string tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(tree);
        string link = Path.Combine(tree, "linked");
        DirectoryLinkDeployment.CreateJunction(link, target);

        try { Assert.Throws<InvalidDataException>(() => SafeDirectoryTree.Inspect(tree)); }
        finally { DirectoryLinkDeployment.RemoveLink(link); }
    }

    [Fact]
    public void Copy_ReproducesTheTreeAndOverwritesExistingFiles()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        Directory.CreateDirectory(Path.Combine(source, "tex"));
        File.WriteAllText(Path.Combine(source, "mod.ini"), "new");
        File.WriteAllText(Path.Combine(source, "tex", "a.dds"), "a");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "mod.ini"), "old");

        SafeDirectoryTree.Copy(source, target);

        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "mod.ini")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(target, "tex", "a.dds")));
        Assert.True(Directory.Exists(Path.Combine(target, "empty")));
    }

    [Fact]
    public void Delete_RemovesNestedLinksWithoutTouchingTheirTargets()
    {
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.ini"), "keep");
        string tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "sub", "a.ini"), "a");
        DirectoryLinkDeployment.CreateJunction(Path.Combine(tree, "sub", "linked"), outside);

        SafeDirectoryTree.Delete(tree);
        SafeDirectoryTree.Delete(Path.Combine(_root, "missing"));

        Assert.False(Directory.Exists(tree));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep.ini")));
    }

    [Fact]
    public void Delete_RemovesARootLinkAsALink()
    {
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.ini"), "keep");
        string link = Path.Combine(_root, "link");
        DirectoryLinkDeployment.CreateJunction(link, outside);

        SafeDirectoryTree.Delete(link);

        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(outside, "keep.ini")));
    }
}
