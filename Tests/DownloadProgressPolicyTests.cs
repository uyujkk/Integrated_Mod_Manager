using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class DownloadProgressPolicyTests
{
    [Theory]
    [InlineData(DownloadTaskState.Queued)]
    [InlineData(DownloadTaskState.Preparing)]
    [InlineData(DownloadTaskState.Downloading)]
    [InlineData(DownloadTaskState.Extracting)]
    [InlineData(DownloadTaskState.Canceling)]
    public void ActiveStates_AreNotTerminal(DownloadTaskState state)
    {
        Assert.True(DownloadProgressPolicy.IsActive(state));
        Assert.False(DownloadProgressPolicy.IsTerminal(state));
    }

    [Theory]
    [InlineData(DownloadTaskState.Completed)]
    [InlineData(DownloadTaskState.Failed)]
    [InlineData(DownloadTaskState.Canceled)]
    public void FinishedStates_AreTerminal(DownloadTaskState state)
    {
        Assert.False(DownloadProgressPolicy.IsActive(state));
        Assert.True(DownloadProgressPolicy.IsTerminal(state));
    }

    [Fact]
    public void CanApply_AllowsSameAndForwardPhases()
    {
        Assert.True(DownloadProgressPolicy.CanApply(DownloadTaskState.Downloading, DownloadTaskState.Downloading));
        Assert.True(DownloadProgressPolicy.CanApply(DownloadTaskState.Queued, DownloadTaskState.Downloading));
        Assert.True(DownloadProgressPolicy.CanApply(DownloadTaskState.Downloading, DownloadTaskState.Extracting));
        Assert.True(DownloadProgressPolicy.CanApply(DownloadTaskState.Canceling, DownloadTaskState.Canceled));
    }

    [Fact]
    public void CanApply_RejectsOlderQueuedCallbackAndTerminalOverwrite()
    {
        Assert.False(DownloadProgressPolicy.CanApply(DownloadTaskState.Extracting, DownloadTaskState.Downloading));
        Assert.False(DownloadProgressPolicy.CanApply(DownloadTaskState.Canceling, DownloadTaskState.Downloading));
        Assert.False(DownloadProgressPolicy.CanApply(DownloadTaskState.Completed, DownloadTaskState.Failed));
        Assert.False(DownloadProgressPolicy.CanApply(DownloadTaskState.Canceled, DownloadTaskState.Downloading));
    }

    [Fact]
    public void CanApply_RejectsUnknownStateValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DownloadProgressPolicy.CanApply(DownloadTaskState.Queued, (DownloadTaskState)999));
    }

    [Theory]
    [InlineData(52, 41, 52)]
    [InlineData(52, 73, 73)]
    [InlineData(-4, 12, 12)]
    [InlineData(101, 50, 100)]
    [InlineData(20, 120, 100)]
    public void MergeProgress_ClampsAndNeverMovesBackwards(double current, double requested, double expected)
    {
        Assert.Equal(expected, DownloadProgressPolicy.MergeProgress(current, requested));
    }
}
