using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineDownloadSelectionPolicyTests
{
    [Fact]
    public void SelectDefault_ChoosesNewestActiveArchiveRegardlessOfApiOrder()
    {
        OnlineDownloadCandidate older = Candidate("100", "older.zip", day: 1);
        OnlineDownloadCandidate newest = Candidate("300", "newest.zip", day: 3);
        OnlineDownloadCandidate middle = Candidate("200", "middle.zip", day: 2);

        OnlineDownloadCandidate? selected = OnlineDownloadSelectionPolicy.SelectDefault([older, newest, middle]);

        Assert.Same(newest, selected);
    }

    [Fact]
    public void SelectDefault_PrefersArchiveOverNewerLooseFile()
    {
        OnlineDownloadCandidate archive = Candidate("100", "mod.zip", day: 1);
        OnlineDownloadCandidate looseFile = Candidate("200", "readme.txt", day: 2, supportedArchive: false);

        OnlineDownloadCandidate? selected = OnlineDownloadSelectionPolicy.SelectDefault([looseFile, archive]);

        Assert.Same(archive, selected);
    }

    [Fact]
    public void SelectDefault_FallsBackToNewestArchivedArchive()
    {
        OnlineDownloadCandidate older = Candidate("100", "old.zip", day: 1, archived: true);
        OnlineDownloadCandidate newer = Candidate("200", "new.zip", day: 2, archived: true);

        OnlineDownloadCandidate? selected = OnlineDownloadSelectionPolicy.SelectDefault([older, newer]);

        Assert.Same(newer, selected);
    }

    [Fact]
    public void SelectDefault_IgnoresCandidatesWithoutDownloadUrl()
    {
        OnlineDownloadCandidate invalid = Candidate("300", "missing.zip", day: 3);
        invalid.DownloadUrl = string.Empty;
        OnlineDownloadCandidate valid = Candidate("100", "valid.zip", day: 1);

        OnlineDownloadCandidate? selected = OnlineDownloadSelectionPolicy.SelectDefault([invalid, valid]);

        Assert.Same(valid, selected);
    }

    [Fact]
    public void OrderForManualSelection_ShowsActiveBeforeArchivedAndNewestFirst()
    {
        OnlineDownloadCandidate activeOlder = Candidate("100", "active-old.zip", day: 1);
        OnlineDownloadCandidate archivedNewest = Candidate("400", "archived-new.zip", day: 4, archived: true);
        OnlineDownloadCandidate activeNewest = Candidate("300", "active-new.zip", day: 3);

        IReadOnlyList<OnlineDownloadCandidate> ordered =
            OnlineDownloadSelectionPolicy.OrderForManualSelection([archivedNewest, activeOlder, activeNewest]);

        Assert.Equal([activeNewest, activeOlder, archivedNewest], ordered);
    }

    [Fact]
    public void SelectCharacterFolder_MatchesLocalizedCharacterAlias()
    {
        string[] folders = [@"F:\mod仓库\提弗洛斯", @"F:\mod仓库\庄方怡"];

        string? selected = OnlineDownloadSelectionPolicy.SelectCharacterFolder(
            folders,
            ["Zhuang Fangyi", "庄方怡", "Fangyi"]);

        Assert.Equal(@"F:\mod仓库\庄方怡", selected);
    }

    [Fact]
    public void SelectCharacterFolder_IgnoresSpacesAndPunctuation()
    {
        string[] folders = [@"D:\Mods\Zhuang-Fangyi"];

        string? selected = OnlineDownloadSelectionPolicy.SelectCharacterFolder(
            folders,
            ["Zhuang Fangyi"]);

        Assert.Equal(@"D:\Mods\Zhuang-Fangyi", selected);
    }

    [Fact]
    public void SelectCharacterFolder_AllowsDecoratedCharacterFolderName()
    {
        string[] folders = [@"D:\Mods\庄方怡 Mods"];

        string? selected = OnlineDownloadSelectionPolicy.SelectCharacterFolder(
            folders,
            ["庄方怡"]);

        Assert.Equal(@"D:\Mods\庄方怡 Mods", selected);
    }

    [Fact]
    public void SelectCharacterFolder_DoesNotGuessWhenBestMatchesAreTied()
    {
        string[] folders = [@"D:\Mods\Fangyi A", @"D:\Mods\Fangyi B"];

        string? selected = OnlineDownloadSelectionPolicy.SelectCharacterFolder(
            folders,
            ["Fangyi"]);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectCharacterFolder_UsesShortAliasOnlyForExactMatch()
    {
        string[] folders = [@"D:\Mods\ZFY", @"D:\Mods\ZFY Old"];

        string? selected = OnlineDownloadSelectionPolicy.SelectCharacterFolder(
            folders,
            ["ZFY"]);

        Assert.Equal(@"D:\Mods\ZFY", selected);
    }

    private static OnlineDownloadCandidate Candidate(
        string id,
        string fileName,
        int day,
        bool archived = false,
        bool supportedArchive = true)
    {
        return new OnlineDownloadCandidate
        {
            FileId = id,
            FileName = fileName,
            DownloadUrl = $"https://gamebanana.com/dl/{id}",
            AddedAt = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero),
            IsArchived = archived,
            IsSupportedArchive = supportedArchive
        };
    }
}
