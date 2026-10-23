using System.Text.Json;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Online;

namespace IntegratedModManager.Core.Tests;

public sealed class DownloadTaskJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-queue-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "tasks.json");
    public sealed record Request(string Repository, string RepositoryRoot, string Destination, string FileId);
    private static DownloadTaskRecord<Request> Record(DownloadTaskState state = DownloadTaskState.Downloading, string id = "original")
        => new(id, "Mod A", "GameBanana:1", "original/output", state, 42, "下载中", "Downloading",
            new("original-repository", "original/root", "original/character", "file-123"), "original/file.zip");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData(DownloadTaskState.Queued)]
    [InlineData(DownloadTaskState.Preparing)]
    [InlineData(DownloadTaskState.Downloading)]
    [InlineData(DownloadTaskState.Extracting)]
    [InlineData(DownloadTaskState.Canceling)]
    public void RestartMarksActiveAttemptInterruptedAndRetainsOriginalRequest(DownloadTaskState state)
    {
        new DownloadTaskJournal<Request>(FilePath).Save([Record(state)]);
        var loaded = new DownloadTaskJournal<Request>(FilePath).Load();
        var task = Assert.Single(loaded.Tasks);
        Assert.True(task.Interrupted); Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal(Record(state).Request, task.Request);
        Assert.Equal("original/output", task.DestinationPath);
        Assert.Equal("original/file.zip", task.ArchivePath);
        Assert.Contains("重新下载", task.StatusZh); Assert.False(loaded.UsedBackup);
    }

    [Theory]
    [InlineData(DownloadTaskState.Completed)]
    [InlineData(DownloadTaskState.Failed)]
    [InlineData(DownloadTaskState.Canceled)]
    public void TerminalHistoryDoesNotBecomeAnActiveDownload(DownloadTaskState state)
    {
        var journal = new DownloadTaskJournal<Request>(FilePath);
        journal.Save([Record(state)]);
        var task = Assert.Single(journal.Load().Tasks);
        Assert.Equal(state, task.State); Assert.False(task.Interrupted);
    }

    [Fact]
    public void TruncatedPrimaryRecoversPreviousCompleteRecordWithoutDestroyingEvidence()
    {
        var journal = new DownloadTaskJournal<Request>(FilePath);
        journal.Save([Record(id: "previous")]); journal.Save([Record(id: "current")]);
        File.WriteAllText(FilePath, "{truncated");
        var restarted = new DownloadTaskJournal<Request>(FilePath);
        var recovered = restarted.Load();
        Assert.True(recovered.UsedBackup); Assert.Equal("previous", Assert.Single(recovered.Tasks).Id);
        restarted.Save(recovered.Tasks);
        Assert.Equal("{truncated", File.ReadAllText(Assert.Single(Directory.GetFiles(_root, "*.corrupt-*"))));
        Assert.Single(new DownloadTaskJournal<Request>(FilePath).Load().Tasks);
        Assert.Equal("previous", Assert.Single(RecoveryJsonFile.Read<DownloadTaskJournalDocument<Request>>(FilePath + ".backup").Tasks).Id);
    }

    [Fact]
    public void UnreadableQueueIsNotSilentlyReplacedWithAnEmptyHistory()
    {
        Directory.CreateDirectory(_root); File.WriteAllText(FilePath, "broken");
        Assert.Throws<JsonException>(() => new DownloadTaskJournal<Request>(FilePath).Load());
        Assert.Equal("broken", File.ReadAllText(FilePath));
    }

    [Fact]
    public void MissingPrimaryCanRecoverBackupAndClearingHistoryPersists()
    {
        var journal = new DownloadTaskJournal<Request>(FilePath);
        journal.Save([Record()]); journal.Save([Record()]); File.Delete(FilePath);
        Assert.True(new DownloadTaskJournal<Request>(FilePath).Load().UsedBackup);
        journal.Save([]);
        Assert.Empty(new DownloadTaskJournal<Request>(FilePath).Load().Tasks);
    }

    [Fact]
    public void DuplicateIdsMissingRequestsAndOversizedQueuesAreRejectedBeforePublication()
    {
        var journal = new DownloadTaskJournal<Request>(FilePath);
        Assert.Throws<InvalidDataException>(() => journal.Save([Record(), Record()]));
        Assert.Throws<InvalidDataException>(() => journal.Save([Record() with { Request = null }]));
        Assert.Throws<InvalidDataException>(() => journal.Save(Enumerable.Range(0, 513).Select(i => Record(id: i.ToString()))));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void UnsupportedVersionAndInvalidStatesAreRejected()
    {
        Directory.CreateDirectory(_root);
        RecoveryJsonFile.Write(FilePath, new DownloadTaskJournalDocument<Request> { Version = 99 });
        Assert.Throws<InvalidDataException>(() => new DownloadTaskJournal<Request>(FilePath).Load());
        Assert.Throws<InvalidDataException>(() => new DownloadTaskJournal<Request>(FilePath).Save([Record() with { State = (DownloadTaskState)999 }]));
    }
}
