using IntegratedModManager.Core.Repository;
using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

public sealed class DashboardModelsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "imm-dashboard-tests-" + Guid.NewGuid().ToString("N"));

    public DashboardModelsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static RepositoryPresetInfo Genshin => new("genshin", "原神", "Genshin Impact", "18140", "https://wiki.example/genshin");

    [Fact]
    public void Paths_ReportWhetherEachPathExists()
    {
        string launcher = Path.Combine(_folder, "XXMI Launcher.exe");
        File.WriteAllText(launcher, string.Empty);
        var paths = new RepositoryPathsViewModel { Source = _folder, Target = Path.Combine(_folder, "missing") };

        Assert.Equal(PathFieldStatus.Ready, paths.SourceStatus);
        Assert.Equal(PathFieldStatus.Missing, paths.TargetStatus);
        Assert.Equal(PathFieldStatus.Empty, paths.LauncherStatus);
        Assert.False(paths.CanRunLauncher);

        paths.Launcher = launcher;
        Assert.True(paths.CanRunLauncher);
        // A folder is not a launcher.
        paths.Launcher = _folder;
        Assert.False(paths.CanRunLauncher);
    }

    [Fact]
    public void Paths_AnnounceTheStatusWithTheText()
    {
        var paths = new RepositoryPathsViewModel();
        var changed = new List<string>();
        paths.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        paths.Source = _folder;
        paths.Target = _folder;
        paths.Launcher = "x";

        Assert.Equal(
            [nameof(RepositoryPathsViewModel.Source), nameof(RepositoryPathsViewModel.SourceStatus),
             nameof(RepositoryPathsViewModel.Target), nameof(RepositoryPathsViewModel.TargetStatus),
             nameof(RepositoryPathsViewModel.Launcher), nameof(RepositoryPathsViewModel.LauncherStatus), nameof(RepositoryPathsViewModel.CanRunLauncher)],
            changed);
    }

    [Fact]
    public void Paths_CountWhatIsSetAndWhatIsMissing()
    {
        var paths = new RepositoryPathsViewModel();
        Assert.Equal((0, 2), (paths.ReadyCount, paths.MissingRequiredCount));

        paths.Source = _folder;
        Assert.Equal((1, 1), (paths.ReadyCount, paths.MissingRequiredCount));

        // A launcher that is missing is not counted as set, but it is not required either.
        paths.Target = _folder;
        paths.Launcher = Path.Combine(_folder, "missing.exe");
        Assert.Equal((2, 0), (paths.ReadyCount, paths.MissingRequiredCount));
    }

    [Fact]
    public void Paths_KeepNullsOutAndTrimOnRequest()
    {
        var paths = new RepositoryPathsViewModel { Source = null!, Target = "  D:\\Game\\Mods  " };

        Assert.Equal(string.Empty, paths.Source);
        Assert.Equal("D:\\Game\\Mods", paths.TrimmedTarget);
        Assert.Equal("  D:\\Game\\Mods  ", paths.Target);
    }

    [Fact]
    public void Paths_EditableFlagsAreSeparate()
    {
        var paths = new RepositoryPathsViewModel { ArePathsEditable = false };

        Assert.True(paths.IsLinkDeploymentEditable);
        paths.IsLinkDeploymentEditable = false;
        paths.ArePathsEditable = true;
        Assert.False(paths.IsLinkDeploymentEditable);
        Assert.True(paths.ArePathsEditable);
    }

    [Theory]
    [InlineData("18140", "", true)]
    [InlineData("17510", "", true)]
    [InlineData("", "Genshin Impact", true)]
    [InlineData("", " 原神 ", true)]
    [InlineData("42770", "Arknights: Endfield", false)]
    [InlineData("", "", false)]
    public void Preset_MatchesByCategoryOrGameName(string category, string game, bool expected)
    {
        var repository = new WorkspaceRepository { OnlineCategoryId = category, OnlineGameName = game };

        Assert.Equal(expected, RepositoryPresetMatching.IsForPreset(repository, Genshin));
    }

    [Fact]
    public void Preset_FindsTheExistingRepositoryOnTheSameSite()
    {
        var elsewhere = new WorkspaceRepository { Id = "a", OnlineSourceSite = "Other", OnlineCategoryId = "18140" };
        var banana = new WorkspaceRepository { Id = "b", OnlineSourceSite = "gamebanana", OnlineCategoryId = "18140" };

        Assert.Same(banana, RepositoryPresetMatching.FindExisting([elsewhere, banana], Genshin, "GameBanana"));
        Assert.Null(RepositoryPresetMatching.FindExisting([elsewhere], Genshin, "GameBanana"));
    }
}
