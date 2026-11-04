namespace ModFolderCopier.WinUI;

public sealed class TrackedModUpdateResult
{
    public bool CheckFailed { get; set; }
    public string? BackupPath { get; set; }
    public string Path { get; set; } = string.Empty;

    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ProfileUrl { get; set; } = string.Empty;

    public string? PreviewUrl { get; set; }

    public DateTimeOffset? LastKnownUpdatedAt { get; set; }

    public DateTimeOffset? LatestUpdatedAt { get; set; }

    public bool HasUpdate { get; set; }

    // Shown on the row when a check failed; null when there is nothing to say.
    internal UiMessage? Status { get; set; }
}
