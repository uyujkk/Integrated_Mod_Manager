using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

public sealed class DownloadTaskItem
{
    public bool ReplacementCommitting { get; set; }
    public bool RestoredFromJournal { get; set; }
    public bool Interrupted { get; set; }
    public OnlineDownloadRetryRequest? RetryRequest { get; set; }
    public bool RetryInProgress { get; set; }
    public string SourceIdentity { get; set; } = string.Empty;
    public OnlineDownloadSession? Session { get; set; }
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public string StatusZh { get; set; } = string.Empty;

    public string StatusEn { get; set; } = string.Empty;

    public double Progress { get; set; }

    public string DestinationPath { get; set; } = string.Empty;

    public string ArchivePath { get; set; } = string.Empty;

    public DownloadTaskState State { get; set; } = DownloadTaskState.Queued;

    public bool HasKnownTotalLength { get; set; }

    public DateTimeOffset LastUiRefreshUtc { get; set; } = DateTimeOffset.MinValue;

    public CancellationTokenSource Cancellation { get; } = new();
}
