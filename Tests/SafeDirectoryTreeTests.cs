using IntegratedModManager.Core;

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
}
