using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class PathSafetyTests
{
    [Fact]
    public void ResolveInsideDirectory_ReturnsNestedPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-path-tests", "root");

        string result = PathSafety.ResolveInsideDirectory(root, Path.Combine("mods", "preview.png"));

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "mods", "preview.png")), result);
    }

    [Fact]
    public void ResolveInsideDirectory_AllowsTheRootDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-path-tests", "root");

        string result = PathSafety.ResolveInsideDirectory(root, string.Empty);

        Assert.Equal(Path.GetFullPath(root), result);
    }

    [Fact]
    public void ResolveInsideDirectory_RejectsParentTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-path-tests", "root");

        Assert.Throws<InvalidDataException>(() =>
            PathSafety.ResolveInsideDirectory(root, Path.Combine("..", "outside.txt")));
    }

    [Fact]
    public void ResolveInsideDirectory_RejectsAbsoluteOutsidePath()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-path-tests", "root");
        string outside = Path.Combine(Path.GetTempPath(), "imm-path-tests", "outside.txt");

        Assert.Throws<InvalidDataException>(() => PathSafety.ResolveInsideDirectory(root, outside));
    }

    [Fact]
    public void ResolveInsideDirectory_DoesNotAcceptSiblingWithSharedPrefix()
    {
        string parent = Path.Combine(Path.GetTempPath(), "imm-path-tests");
        string root = Path.Combine(parent, "mods");
        string sibling = Path.Combine(parent, "mods-old", "file.ini");

        Assert.Throws<InvalidDataException>(() => PathSafety.ResolveInsideDirectory(root, sibling));
    }

    [Fact]
    public void ResolveInsideDirectory_RejectsMissingRoot()
    {
        Assert.Throws<ArgumentException>(() => PathSafety.ResolveInsideDirectory(string.Empty, "file.txt"));
    }
}
