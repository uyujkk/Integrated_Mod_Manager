namespace ModFolderCopier.WinUI;

public sealed class TrackedModOrigin
{
    public string DownloadFileId { get; set; } = string.Empty;
    public string DownloadFileName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public string SourceSite { get; set; } = string.Empty;

    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ProfileUrl { get; set; } = string.Empty;

    public string? PreviewUrl { get; set; }

    public DateTimeOffset? LastKnownUpdatedAt { get; set; }
}
