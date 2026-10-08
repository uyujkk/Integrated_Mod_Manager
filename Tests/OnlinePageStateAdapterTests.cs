using IntegratedModManager.Core;
using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

public sealed class OnlinePageStateAdapterTests
{
    // These tests compile the production adapter source, not a duplicate test implementation.
    private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class DetailFixture
    {
        public OnlinePageStateAdapter Adapter { get; } = new();
        public List<string> Published { get; } = [];
        public List<Exception> Errors { get; } = [];
        public List<bool> Loading { get; } = [];
        public bool Current { get; set; } = true;
        public CancellationToken LastToken { get; private set; }

        public Task Run(Func<CancellationToken, Task<string>> load,
            Func<string, CancellationToken, Task<string>>? project = null,
            Func<Task>? open = null, Action<string, string>? publish = null,
            Func<Exception, Task>? error = null)
            => Adapter.RunLatestAsync(open ?? (() => Task.CompletedTask), token =>
                {
                    LastToken = token;
                    return load(token);
                }, project ?? ((value, _) => Task.FromResult(value)),
                publish ?? ((raw, display) => Published.Add(raw + ":" + display)),
                error ?? (ex => { Errors.Add(ex); return Task.CompletedTask; }),
                () => Current, Loading.Add);
    }

    [Fact]
    public async Task NormalPipeline_PublishesRawAndProjectedAndFinishesLoading()
    {
        var f = new DetailFixture();
        await f.Run(_ => Task.FromResult("raw"), (raw, _) => Task.FromResult("translated"));
        Assert.Equal(["raw:translated"], f.Published);
        Assert.Equal([true, false], f.Loading);
        Assert.Empty(f.Errors);
        Assert.False(f.Adapter.IsLoading);
    }

    [Fact]
    public async Task SameItemReopened_OldLoadCannotPublishOrFinishNewLoading()
    {
        var f = new DetailFixture();
        var old = Pending<string>();
        var newer = Pending<string>();
        Task first = f.Run(_ => old.Task);
        CancellationToken oldToken = f.LastToken;
        Task second = f.Run(_ => newer.Task);
        Assert.True(oldToken.IsCancellationRequested);
        old.SetResult("old");
        await first;
        Assert.Empty(f.Published);
        Assert.Equal([true, true], f.Loading);
        Assert.True(f.Adapter.IsLoading);
        newer.SetResult("new");
        await second;
        Assert.Equal(["new:new"], f.Published);
        Assert.Equal([true, true, false], f.Loading);
    }

    [Fact]
    public async Task StaleProjection_CannotOverwriteNewDetail()
    {
        var f = new DetailFixture();
        var projection = Pending<string>();
        Task first = f.Run(_ => Task.FromResult("raw-old"), (_, _) => projection.Task);
        await f.Run(_ => Task.FromResult("new"));
        projection.SetResult("translated-old");
        await first;
        Assert.Equal(["new:new"], f.Published);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task ClosedDuringOpen_DoesNotStartMetadataRequest()
    {
        var f = new DetailFixture();
        var opened = Pending<bool>();
        int requests = 0;
        Task work = f.Run(_ => { requests++; return Task.FromResult("raw"); }, open: () => opened.Task);
        f.Adapter.Invalidate();
        opened.SetResult(true);
        await work;
        Assert.Equal(0, requests);
        Assert.Empty(f.Published);
        Assert.Equal([true], f.Loading);
        Assert.False(f.Adapter.IsLoading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseOrResetDuringLoad_RejectsLateSuccessOrFailure(bool fail)
    {
        var f = new DetailFixture();
        var pending = Pending<string>();
        Task work = f.Run(_ => pending.Task);
        CancellationToken token = f.LastToken;
        f.Adapter.Invalidate();
        Assert.True(token.IsCancellationRequested);
        if (fail) pending.SetException(new IOException("late"));
        else pending.SetResult("old");
        await work;
        Assert.Empty(f.Published);
        Assert.Empty(f.Errors);
        Assert.Equal([true], f.Loading);
        Assert.False(f.Adapter.IsLoading);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ChangedRepositoryOrLanguage_StopsAtEveryAwaitBoundary(int stage)
    {
        var f = new DetailFixture();
        var step = Pending<string>();
        int loads = 0, projects = 0;
        Task work = f.Run(_ => { loads++; return stage == 1 ? step.Task : Task.FromResult("raw"); },
            (_, _) => { projects++; return stage == 2 ? step.Task : Task.FromResult("display"); },
            stage == 0 ? (() => step.Task) : null);
        f.Current = false;
        step.SetResult("late");
        await work;
        Assert.Empty(f.Published);
        Assert.Empty(f.Errors);
        Assert.Equal(stage == 0 ? 0 : 1, loads);
        Assert.Equal(stage == 2 ? 1 : 0, projects);
        Assert.Equal([true], f.Loading); // No callbacks into the changed page.
        Assert.False(f.Adapter.IsLoading);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CurrentFailure_ReportsAndReleasesLoading(int stage)
    {
        var f = new DetailFixture();
        var failure = new IOException("fixture");
        await f.Run(_ => stage == 1 ? Task.FromException<string>(failure) : Task.FromResult("raw"),
            (_, _) => stage == 2 ? Task.FromException<string>(failure) : Task.FromResult("display"),
            () => stage == 0 ? Task.FromException(failure) : Task.CompletedTask,
            (_, _) => { if (stage == 3) throw failure; });
        Assert.Same(failure, Assert.Single(f.Errors));
        Assert.Equal([true, false], f.Loading);
        Assert.False(f.Adapter.IsLoading);
    }

    [Fact]
    public async Task CanceledHttp_SilentAndNewRequestStillWorks()
    {
        var f = new DetailFixture();
        Task first = f.Run(async token => { await Task.Delay(Timeout.Infinite, token); return "never"; });
        await f.Run(_ => Task.FromResult("new"));
        await first;
        Assert.Equal(["new:new"], f.Published);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task IndependentTimeout_IsReportedNotMistakenForClosing()
    {
        var f = new DetailFixture();
        await f.Run(_ => Task.FromException<string>(new OperationCanceledException("upstream timeout")));
        Assert.IsType<OperationCanceledException>(Assert.Single(f.Errors));
        Assert.False(f.Adapter.IsLoading);
    }

    [Fact]
    public async Task ErrorDialogFailure_StillReleasesRequest()
    {
        var f = new DetailFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run(
            _ => Task.FromException<string>(new IOException("http")),
            error: _ => Task.FromException(new InvalidOperationException("dialog"))));
        Assert.False(f.Adapter.IsLoading);
        Assert.Equal([true, false], f.Loading);
        await f.Run(_ => Task.FromResult("retry"));
        Assert.Equal(["retry:retry"], f.Published);
    }

    [Fact]
    public async Task InvalidatingTwice_AndAfterCompletion_IsSafe()
    {
        var f = new DetailFixture();
        f.Adapter.Invalidate();
        f.Adapter.Invalidate();
        await f.Run(_ => Task.FromResult("ok"));
        f.Adapter.Invalidate();
        Assert.False(f.Adapter.IsLoading);
    }

    [Fact]
    public async Task ReopenedWhileOldErrorDialogWaits_OldFinallyDoesNotUnlockNewPage()
    {
        var f = new DetailFixture();
        var dialog = Pending<bool>();
        Task first = f.Run(_ => Task.FromException<string>(new IOException("old")), error: _ => dialog.Task);
        var load = Pending<string>();
        Task second = f.Run(_ => load.Task);
        dialog.SetResult(true);
        await first;
        Assert.True(f.Adapter.IsLoading);
        Assert.Equal([true, true], f.Loading);
        load.SetResult("new");
        await second;
        Assert.Equal(["new:new"], f.Published);
    }

    private static OnlineDownloadContext Context(string id = "a", string path = @"G:\mock\repo") => new(id, path, "fixture");

    [Fact]
    public void DefaultAndManualCommands_SharePreflightGate()
    {
        var gate = new OnlineDownloadActionAdapter();
        using var first = gate.TryBegin(Context(), "GameBanana:1");
        Assert.NotNull(first);
        Assert.True(gate.IsPreparing);
        Assert.Null(gate.TryBegin(Context(), "GameBanana:1"));
        Assert.Null(gate.TryBegin(Context(), "GameBanana:2"));
        Assert.Null(gate.TryBegin(Context("b"), "GameBanana:1"));
    }

    [Fact]
    public void ActiveTask_RejectsSameSource_ButAllowsAnotherAfterPreflight()
    {
        var gate = new OnlineDownloadActionAdapter();
        using var first = gate.TryBegin(Context(), "GameBanana:1");
        first!.MarkTaskStarted();
        Assert.False(gate.IsPreparing);
        Assert.True(gate.IsActive(Context(), "GameBanana:1"));
        Assert.Null(gate.TryBegin(Context(), "GameBanana:1"));
        using var other = gate.TryBegin(Context(), "GameBanana:2");
        Assert.NotNull(other);
    }

    [Theory]
    [InlineData("b", @"G:\mock\repo")]
    [InlineData("a", @"G:\mock\other")]
    public void CapturedRepositoryScopes_AllowIndependentInstallations(string id, string path)
    {
        var gate = new OnlineDownloadActionAdapter();
        using var first = gate.TryBegin(Context(), "GameBanana:1");
        first!.MarkTaskStarted();
        using var other = gate.TryBegin(Context(id, path), "GameBanana:1");
        Assert.NotNull(other);
    }

    [Fact]
    public void RepositoryDisplayName_DoesNotBypassSourceLock()
    {
        var gate = new OnlineDownloadActionAdapter();
        using var first = gate.TryBegin(Context(), "GameBanana:1");
        first!.MarkTaskStarted();
        Assert.Null(gate.TryBegin(Context() with { RepositoryName = "renamed" }, "GameBanana:1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelOrFinish_ReleasesLock_Idempotently(bool started)
    {
        var gate = new OnlineDownloadActionAdapter();
        var first = gate.TryBegin(Context(), "GameBanana:1")!;
        if (started) first.MarkTaskStarted();
        first.Dispose();
        first.Dispose();
        Assert.False(gate.IsPreparing);
        Assert.False(gate.IsActive(Context(), "GameBanana:1"));
        using var retry = gate.TryBegin(Context(), "GameBanana:1");
        Assert.NotNull(retry);
        Assert.Throws<ObjectDisposedException>(() => first.MarkTaskStarted());
    }

    [Fact]
    public void WindowClose_CancelsPreparation_ButNotCreatedDownloadTask()
    {
        var gate = new OnlineDownloadActionAdapter();
        using var active = gate.TryBegin(Context(), "GameBanana:1");
        active!.MarkTaskStarted();
        using var preparing = gate.TryBegin(Context(), "GameBanana:2");
        gate.CancelPreparation();
        Assert.True(preparing!.Token.IsCancellationRequested);
        Assert.False(active.Token.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() => preparing.MarkTaskStarted());
    }

    [Fact]
    public void OldTaskFinishing_DoesNotReleaseAnotherPicker()
    {
        var gate = new OnlineDownloadActionAdapter();
        var first = gate.TryBegin(Context(), "GameBanana:1")!;
        first.MarkTaskStarted();
        using var preparing = gate.TryBegin(Context(), "GameBanana:2");
        first.Dispose();
        Assert.True(gate.IsPreparing);
        Assert.Null(gate.TryBegin(Context(), "GameBanana:3"));
    }

    [Fact]
    public void RootCaseAndTrailingSeparator_DoNotBypassLock()
    {
        var gate = new OnlineDownloadActionAdapter();
        using var first = gate.TryBegin(Context(), "GameBanana:1");
        first!.MarkTaskStarted();
        Assert.Null(gate.TryBegin(Context("A", @"g:\MOCK\REPO\"), "GameBanana:1"));
    }

    [Fact]
    public async Task PreflightFailure_AndRetry_UseActualAdapterLease()
    {
        var gate = new OnlineDownloadActionAdapter();
        async Task Run(bool fail)
        {
            using var lease = gate.TryBegin(Context(), "GameBanana:1");
            Assert.NotNull(lease);
            await Task.Yield();
            if (fail) throw new IOException("fixture metadata/picker failure");
            lease!.MarkTaskStarted();
        }
        await Assert.ThrowsAsync<IOException>(() => Run(true));
        await Run(false);
        Assert.False(gate.IsPreparing);
        Assert.False(gate.IsActive(Context(), "GameBanana:1"));
    }
}
