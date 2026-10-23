using IntegratedModManager.Core.Infrastructure;

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

    [Fact]
    public void ResolveInsideDirectory_RejectsNullRelativePath()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-path-tests", "root");

        Assert.Throws<ArgumentNullException>(() => PathSafety.ResolveInsideDirectory(root, null!));
    }

    [Fact]
    public void ResolveInsideDirectory_NormalizesDotSegmentsInsideRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-path-tests", "root") + Path.DirectorySeparatorChar;

        string result = PathSafety.ResolveInsideDirectory(root, Path.Combine("mods", ".", "preview.png"));

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "mods", "preview.png")), result);
    }

    [Fact]
    public void ResolveInsideDirectory_ResolvesUnderDriveRootNotCurrentDirectory()
    {
        // The current directory is on this drive, so "D:" + relative would resolve under it.
        string driveRoot = Path.GetPathRoot(Environment.CurrentDirectory)!;

        string result = PathSafety.ResolveInsideDirectory(driveRoot, Path.Combine("mods", "a.ini"));

        Assert.Equal(Path.Combine(driveRoot, "mods", "a.ini"), result);
    }

    [Theory]
    [InlineData(@"C:\Mods", @"C:\Mods", true)]
    [InlineData(@"C:\Mods\A\b.ini", @"C:\Mods", true)]
    [InlineData(@"c:\mods\a", @"C:\Mods\", true)]
    [InlineData(@"C:\Mods-old\a", @"C:\Mods", false)]
    [InlineData(@"C:\Mods\..\Other", @"C:\Mods", false)]
    [InlineData(@"C:\Mods\A", @"C:\", true)]
    [InlineData(@"D:\Mods", @"C:\", false)]
    public void IsSameOrInside_MatchesOnlyTheRootAndDescendants(string candidate, string root, bool expected)
    {
        Assert.Equal(expected, PathSafety.IsSameOrInside(candidate, root));
    }

    [Theory]
    [InlineData(@"C:\Mods", @"C:\Mods\", false)]
    [InlineData(@"C:\Mods\A", @"C:\Mods", true)]
    [InlineData(@"C:\Mods-old", @"C:\Mods", false)]
    public void IsStrictlyInside_ExcludesTheRoot(string candidate, string root, bool expected)
    {
        Assert.Equal(expected, PathSafety.IsStrictlyInside(candidate, root));
    }
}
