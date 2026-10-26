using IntegratedModManager.Core.Deployment;

namespace IntegratedModManager.Core.Tests;

public sealed class ModFileConflictDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-conflicts-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];
    private string Target => Path.Combine(_root, "Mods");

    public ModFileConflictDetectorTests()
    {
        Directory.CreateDirectory(Target);
    }

    public void Dispose()
    {
        foreach (string link in _links.Where(Directory.Exists))
        {
            DirectoryLinkDeployment.RemoveLink(link);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("mod.ini", true)]
    [InlineData(@"textures\body.dds", true)]
    [InlineData(@"preview\readme.txt", true)]
    [InlineData("preview.png", false)]
    [InlineData("Cover.JPG", false)]
    [InlineData("thumbnail.webp", false)]
    [InlineData("image.png", false)]
    [InlineData("README.md", false)]
    [InlineData("license.txt", false)]
    [InlineData("desktop.ini", false)]
    public void IsConflictRelevantRelativePath_IgnoresOnlyRootLevelExtras(string relativePath, bool expected)
    {
        Assert.Equal(expected, ModFileConflictDetector.IsConflictRelevantRelativePath(relativePath));
    }

    [Fact]
    public void Detect_ReportsSharedFilesButNotRootExtras()
    {
        string incoming = Mod(_root, "Incoming", "mod.ini", @"tex\a.dds", "preview.png", "readme.txt");
        Mod(Target, "Installed", "mod.ini", @"tex\a.dds", "preview.png", "readme.txt");
        Mod(Target, "Unrelated", "other.ini");

        List<string> conflicts = ModFileConflictDetector.Detect(incoming, Target, []);

        Assert.Equal(new[] { "Installed  →  mod.ini", @"Installed  →  tex\a.dds" }, conflicts.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Detect_SkipsIgnoredTargetsAndBrokenLinks()
    {
        string incoming = Mod(_root, "Incoming", "mod.ini");
        string previous = Mod(Target, "Incoming", "mod.ini");
        string gone = Mod(_root, "Gone", "mod.ini");
        Link(Path.Combine(Target, "Broken"), gone);
        Directory.Delete(gone, recursive: true);

        Assert.Empty(ModFileConflictDetector.Detect(incoming, Target, [previous]));
    }

    [Fact]
    public void Detect_FollowsInstalledJunctionsToTheirRepositoryFolder()
    {
        string incoming = Mod(_root, "Incoming", "mod.ini");
        string repositoryMod = Mod(Path.Combine(_root, "repo"), "Linked", "mod.ini");
        Link(Path.Combine(Target, "Linked"), repositoryMod);

        Assert.Equal(new[] { "Linked  →  mod.ini" }, ModFileConflictDetector.Detect(incoming, Target, []));
    }

    [Fact]
    public void Detect_StopsAfterOneHundredConflicts()
    {
        string[] files = Enumerable.Range(0, 120).Select(index => $"f{index:D3}.ini").ToArray();
        string incoming = Mod(_root, "Incoming", files);
        Mod(Target, "Installed", files);

        Assert.Equal(100, ModFileConflictDetector.Detect(incoming, Target, []).Count);
    }

    private void Link(string link, string target)
    {
        DirectoryLinkDeployment.CreateJunction(link, target);
        _links.Add(link);
    }

    private static string Mod(string parent, string name, params string[] files)
    {
        string mod = Path.Combine(parent, name);
        Directory.CreateDirectory(mod);
        foreach (string relative in files)
        {
            string file = Path.Combine(mod, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, relative);
        }

        return mod;
    }
}
