using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ModUpdateRecoveryPolicyTests
{
    [Theory]
    [InlineData(DownloadTaskState.Queued)]
    [InlineData(DownloadTaskState.Downloading)]
    [InlineData(DownloadTaskState.Extracting)]
    [InlineData(DownloadTaskState.Completed)]
    public void ReplacementHistoryRestoresWithoutAnOrdinaryDownloadRetry(DownloadTaskState state)
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-update-journal", Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new DownloadTaskJournal<string>(Path.Combine(root, "tasks.json"));
            var input = new DownloadTaskRecord<string>("id", "Mod", "mod-update|gamebanana:1", "original", state, 50, "", "", "unsafe normal download retry");
            journal.Save([ModUpdateRecoveryPolicy.ForSnapshot(input)]);
            var restored = Assert.Single(journal.Load().Tasks);
            Assert.Null(restored.Request);
            Assert.Equal("original", restored.DestinationPath);
            if (DownloadProgressPolicy.IsActive(state))
            {
                Assert.Equal(DownloadTaskState.Failed, restored.State);
                Assert.True(restored.Interrupted);
            }
            else Assert.Equal(state, restored.State);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void OrdinaryDownloadRetainsItsRetry()
    {
        var input = new DownloadTaskRecord<string>("id", "Mod", "gamebanana:1", "folder", DownloadTaskState.Downloading, 50, "", "", "normal retry");
        Assert.Same(input, ModUpdateRecoveryPolicy.ForSnapshot(input));
    }
}
