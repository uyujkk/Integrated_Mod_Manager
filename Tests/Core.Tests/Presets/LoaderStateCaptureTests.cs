using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class LoaderStateCaptureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "imm-flush-" + Guid.NewGuid().ToString("N"));
    private string Ini => Path.Combine(root, "d3dx_user.ini");
    public LoaderStateCaptureTests() { Directory.CreateDirectory(root); File.WriteAllText(Ini, "old"); }
    public void Dispose() { File.SetAttributes(Ini, FileAttributes.Normal); Directory.Delete(root, true); }

    [Fact]
    public async Task WaitsForStableChangedBytesAndNeverMakesFileReadOnly()
    {
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? writer = null;
        await LoaderStateCapture.FlushAsync(Ini, _ =>
        {
            Assert.False(File.GetAttributes(Ini).HasFlag(FileAttributes.ReadOnly));
            writer = Task.Run(async () => { await Task.Delay(30); await File.WriteAllTextAsync(Ini, "new complete values"); releaseWriter.SetResult(); });
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(20));
        await writer!;
        Assert.True(releaseWriter.Task.IsCompleted);
        Assert.Equal("new complete values", File.ReadAllText(Ini));
        Assert.False(File.GetAttributes(Ini).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task UnchangedFileTimesOutAndSendFailurePropagates()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => LoaderStateCapture.FlushAsync(Ini, _ => Task.CompletedTask,
            TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(10)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => LoaderStateCapture.FlushAsync(Ini,
            _ => throw new InvalidOperationException("not sent")));
        Assert.Equal("old", File.ReadAllText(Ini));
    }

    [Fact]
    public async Task DetectsSameLengthWriteEvenWithPreservedTimestamp()
    {
        DateTime stamp = File.GetLastWriteTimeUtc(Ini);
        await LoaderStateCapture.FlushAsync(Ini, _ => { File.WriteAllText(Ini, "new"); File.SetLastWriteTimeUtc(Ini, stamp); return Task.CompletedTask; },
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task ReadOnlyAndInvalidPathNeverSendAndCancellationStopsWaiting()
    {
        bool sent = false;
        Task Send(CancellationToken _) { sent = true; return Task.CompletedTask; }
        File.SetAttributes(Ini, FileAttributes.ReadOnly);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => LoaderStateCapture.FlushAsync(Ini, Send));
        await Assert.ThrowsAsync<ArgumentException>(() => LoaderStateCapture.FlushAsync(Path.Combine(root, "wrong.ini"), Send));
        Assert.False(sent);
        File.SetAttributes(Ini, FileAttributes.Normal);
        using var cancel = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoaderStateCapture.FlushAsync(Ini, Send, cancellationToken: cancel.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => LoaderStateCapture.FlushAsync(Ini, Send, TimeSpan.Zero));
    }
}
