using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlineDownloadSessionTests
{
    private static readonly string Destination = Path.Combine(Path.GetTempPath(), "imm-session-fixture", "角色");
    private static string Child(string name) => Path.Combine(Destination, name);
    private static OnlineDownloadSession Session() => new(new("repo-a", Destination, "仓库 A"), Destination);

    [Fact]
    public async Task PreparationIsDeferred_AndUsesCapturedContext()
    {
        var context = new OnlineDownloadContext("repo-a", Destination, "仓库 A");
        var session = new OnlineDownloadSession(context, Destination);
        session.RegisterExtraction(Child("mod"));
        var ready = new TaskCompletionSource<string>();
        bool committed = false;
        Task<string> work = session.PrepareAndCommitAsync(_ => ready.Task, value => committed = value == "ready", default);
        context = new("repo-b", Child("another"), "仓库 B");
        Assert.False(committed);
        Assert.Equal("repo-a", session.Context.RepositoryId);
        ready.SetResult("ready");
        Assert.Equal("ready", await work);
        Assert.True(committed);
        Assert.True(session.IsCommitted);
        Assert.False(session.CanCancel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationBeforeCommit_DoesNotCommit(bool beforePreparation)
    {
        var session = Session();
        session.RegisterArchive(Child("download.zip"));
        session.RegisterExtraction(Child("mod"));
        using var cancellation = new CancellationTokenSource();
        if (beforePreparation) cancellation.Cancel();
        bool prepared = false;
        bool committed = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.PrepareAndCommitAsync(_ =>
        {
            prepared = true;
            cancellation.Cancel();
            return Task.FromResult(1);
        }, _ => committed = true, cancellation.Token));
        Assert.Equal(!beforePreparation, prepared);
        Assert.False(committed);
        Assert.False(session.CommitStarted);
        Assert.Equal(Child("mod"), session.GetCleanup(canceled: true).ExtractionPath);
        Assert.Equal(Child("download.zip"), session.GetCleanup(canceled: true).ArchivePath);
    }

    [Fact]
    public async Task LateCancellationAfterCommitStarts_DoesNotDeleteInstalledFiles()
    {
        var session = Session();
        session.RegisterExtraction(Child("mod"));
        using var cancellation = new CancellationTokenSource();
        await session.PrepareAndCommitAsync(_ => Task.FromResult(1), _ => cancellation.Cancel(), cancellation.Token);
        Assert.True(session.IsCommitted);
        Assert.Null(session.GetCleanup(true).ExtractionPath);
        Assert.Null(session.GetCleanup(false).ArchivePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PrepareAndCommitAsync(_ => Task.FromResult(1), _ => { }, default));
        Assert.Throws<InvalidOperationException>(() => session.RegisterArchive(Child("new.zip")));
    }

    [Fact]
    public async Task FailedPreparation_DoesNotChangeTracking_AndKeepsArchiveForDiagnosis()
    {
        var session = Session();
        session.RegisterArchive(Child("download.zip"));
        session.RegisterExtraction(Child("mod"));
        bool committed = false;
        await Assert.ThrowsAsync<IOException>(() => session.PrepareAndCommitAsync<int>(_ => throw new IOException("fixture"), _ => committed = true, default));
        Assert.False(committed);
        Assert.True(session.CanCancel);
        Assert.Null(session.GetCleanup(false).ArchivePath);
        Assert.Equal(Child("mod"), session.GetCleanup(false).ExtractionPath);
    }

    [Fact]
    public async Task FailedCommitIsNotMarkedCommitted()
    {
        var session = Session();
        session.RegisterExtraction(Child("mod"));
        await Assert.ThrowsAsync<IOException>(() => session.PrepareAndCommitAsync(_ => Task.FromResult(1), _ => throw new IOException("fixture"), default));
        Assert.False(session.IsCommitted);
        Assert.Equal(Child("mod"), session.GetCleanup(false).ExtractionPath);
    }

    [Fact]
    public async Task NoExtractionCannotCommit()
    {
        var session = Session();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PrepareAndCommitAsync(_ => Task.FromResult(1), _ => { }, default));
        Assert.Null(session.GetCleanup(true).ExtractionPath);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside.zip")]
    [InlineData("mod/nested.zip")]
    public void CannotClaimParentSiblingOrNestedResources(string relative)
    {
        var session = Session();
        Assert.Throws<ArgumentException>(() => session.RegisterArchive(Child(relative)));
        Assert.Throws<ArgumentException>(() => session.RegisterExtraction(Child(relative)));
        Assert.Null(session.ArchivePath);
        Assert.Null(session.ExtractionPath);
    }

    [Fact]
    public void CorrectedArchivePathReplacesCleanupTarget_OnlyOneExtractionCanBeOwned()
    {
        var session = Session();
        session.RegisterArchive(Child("download"));
        session.RegisterArchive(Child("download.zip"));
        Assert.Throws<ArgumentException>(() => session.RegisterExtraction(Child("download.zip")));
        session.RegisterExtraction(Child("mod"));
        Assert.Throws<ArgumentException>(() => session.RegisterArchive(Child("mod")));
        Assert.Throws<InvalidOperationException>(() => session.RegisterExtraction(Child("another")));
        Assert.Equal(Child("download.zip"), session.GetCleanup(true).ArchivePath);
    }

    [Fact]
    public void ScopesPreserveOtherRepositoryAndUseMostSpecificRoot()
    {
        string a = Path.Combine(Destination, "A");
        string b = Path.Combine(Destination, "B");
        string nested = Path.Combine(a, "nested");
        string[] roots = [a, b, nested, "invalid\0root", ""];
        Assert.Equal(a, OnlineTrackingScopePolicy.GetScope(Path.Combine(a, "角色", "mod"), roots));
        Assert.Equal(b, OnlineTrackingScopePolicy.GetScope(Path.Combine(b, "角色", "mod"), roots));
        Assert.Equal(nested, OnlineTrackingScopePolicy.GetScope(Path.Combine(nested, "mod"), roots));
        Assert.Equal(Destination, OnlineTrackingScopePolicy.GetScope(a + "suffix", roots));
        Assert.Equal(a, OnlineTrackingScopePolicy.GetScope(a, roots));
    }

    [Fact]
    public void StaleDedupPreservesOtherRepositoriesAndMultipleExistingCopies()
    {
        string a = Child("A"), b = Child("B");
        string[] roots = [a, b];
        OnlineTrackedInstallation[] installations =
        [
            new(Path.Combine(a, "角色", "installed"), "id:42", true),
            new(Path.Combine(a, "角色", "second copy"), "id:42", true),
            new(Path.Combine(a, "角色", "stale"), "id:42", false),
            new(Path.Combine(b, "角色", "independent"), "id:42", false),
            new("invalid\0path", "id:42", false),
            new(Path.Combine(a, "no identity"), "", false)
        ];
        Assert.Equal([Path.Combine(a, "角色", "stale")], OnlineTrackingScopePolicy.FindStaleDuplicatePaths(installations, roots));
    }

    [Fact]
    public void DedupWithoutRepositoryUsesConfirmedParent_AndNewestStaleRecordWins()
    {
        var first = new OnlineTrackedInstallation(Child("first"), "id:42", false, DateTimeOffset.UnixEpoch);
        var newest = new OnlineTrackedInstallation(Child("newest"), "ID:42", false, DateTimeOffset.UnixEpoch.AddDays(1));
        var otherParent = new OnlineTrackedInstallation(Path.Combine(Destination, "other", "mod"), "id:42", false);
        Assert.Equal([first.Path], OnlineTrackingScopePolicy.FindStaleDuplicatePaths([first, newest, otherParent], []));
    }

    [Fact]
    public async Task OverlappingPreparationsCannotCommitTwice()
    {
        var session = Session();
        session.RegisterExtraction(Child("mod"));
        var delayed = new TaskCompletionSource<int>();
        int commits = 0;
        Task<int> first = session.PrepareAndCommitAsync(_ => delayed.Task, _ => commits++, default);
        await session.PrepareAndCommitAsync(_ => Task.FromResult(2), _ => commits++, default);
        delayed.SetResult(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.Equal(1, commits);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task CleanupPlanAppliedToRealFixture_NeverRemovesUnrelatedOrCommittedFiles(bool canceled, bool committed)
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-online-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var session = new OnlineDownloadSession(new("a", root, "mock"), root);
            string unrelated = Path.Combine(root, "existing.zip"), archive = Path.Combine(root, "new.zip");
            string extracted = Path.Combine(root, "new-mod");
            File.WriteAllText(unrelated, "preserve");
            File.WriteAllText(archive, "owned");
            Directory.CreateDirectory(extracted);
            File.WriteAllText(Path.Combine(extracted, "mod.ini"), "mock");
            session.RegisterArchive(archive);
            session.RegisterExtraction(extracted);
            if (committed)
                await session.PrepareAndCommitAsync(_ => Task.FromResult(1), _ => { }, default);
            OnlineDownloadCleanup cleanup = session.GetCleanup(canceled);
            if (cleanup.ArchivePath is not null) File.Delete(cleanup.ArchivePath);
            if (cleanup.ExtractionPath is not null) Directory.Delete(cleanup.ExtractionPath, true);
            Assert.Equal("preserve", File.ReadAllText(unrelated));
            Assert.Equal(committed || !canceled, File.Exists(archive));
            Assert.Equal(committed, Directory.Exists(extracted));
            Assert.True(Directory.Exists(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
