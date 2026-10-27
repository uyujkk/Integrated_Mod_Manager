using IntegratedModManager.Core.Repository;

namespace IntegratedModManager.Core.Tests;

public sealed class RepositoryContentSummaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-summary-tests", Guid.NewGuid().ToString("N"));

    public RepositoryContentSummaryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Scan_CountsCharacterFoldersAndTheModsInsideThem()
    {
        string source = Path.Combine(_root, "Repository");
        string target = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(Path.Combine(source, "A", "Mod1"));
        Directory.CreateDirectory(Path.Combine(source, "A", "Mod2"));
        Directory.CreateDirectory(Path.Combine(source, "B", "Mod3"));
        Directory.CreateDirectory(Path.Combine(source, "C"));
        File.WriteAllText(Path.Combine(source, "A", "readme.txt"), "not a Mod");
        Directory.CreateDirectory(target);

        RepositoryContentSummary summary = RepositoryContentSummary.Scan(source, target);

        Assert.Equal((true, true, 3, 3), (summary.SourceReady, summary.TargetReady, summary.CategoryCount, summary.ModCount));
        Assert.True(summary.IsReady);
        Assert.Equal(
            new[] { ("A", 2), ("B", 1), ("C", 0) },
            summary.Categories.Select(category => (category.Name, category.ModCount)));
        Assert.Equal(Path.Combine(source, "A"), summary.Categories[0].Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    public void Scan_WithoutASourceFolderCountsNothing(string? sourceName)
    {
        string? source = sourceName is null or "" ? sourceName : Path.Combine(_root, sourceName);

        RepositoryContentSummary summary = RepositoryContentSummary.Scan(source, _root);

        Assert.Equal((false, true, 0, 0), (summary.SourceReady, summary.TargetReady, summary.CategoryCount, summary.ModCount));
        Assert.Empty(summary.Categories);
        Assert.False(summary.IsReady);
    }

    [Fact]
    public void Scan_IsNotReadyWithoutATargetFolder()
    {
        RepositoryContentSummary summary = RepositoryContentSummary.Scan(_root, Path.Combine(_root, "missing"));

        Assert.True(summary.SourceReady);
        Assert.False(summary.TargetReady);
        Assert.False(summary.IsReady);
    }

    [Fact]
    public void Evaluate_DistinguishesEmptyReadyAndMissingPaths()
    {
        string file = Path.Combine(_root, "launcher.exe");
        File.WriteAllText(file, string.Empty);

        Assert.Equal(PathFieldStatus.Empty, PathFieldStatuses.Evaluate("  ", expectFile: false));
        Assert.Equal(PathFieldStatus.Ready, PathFieldStatuses.Evaluate(_root, expectFile: false));
        Assert.Equal(PathFieldStatus.Ready, PathFieldStatuses.Evaluate(" " + file + " ", expectFile: true));
        Assert.Equal(PathFieldStatus.Missing, PathFieldStatuses.Evaluate(file, expectFile: false));
        Assert.Equal(PathFieldStatus.Missing, PathFieldStatuses.Evaluate(_root, expectFile: true));
        Assert.Equal(PathFieldStatus.Missing, PathFieldStatuses.Evaluate(Path.Combine(_root, "nope"), expectFile: false));
    }
}
