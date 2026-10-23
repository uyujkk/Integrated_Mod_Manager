using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

public sealed class OnlineModCard
{
    public int AuthorId { get; set; }
    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;

    public bool IsCharacterNameInferred { get; set; }

    public string RootCategoryName { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public int Likes { get; set; }

    public int Views { get; set; }

    public int Downloads { get; set; }

    public double HotnessScore { get; set; }

    public long FileSizeBytes { get; set; }

    public string? PreviewUrl { get; set; }

    public string ProfileUrl { get; set; } = string.Empty;

    public string? DownloadUrl { get; set; }

    public string DownloadFileName { get; set; } = string.Empty;
    public string DownloadFileId { get; set; } = string.Empty;

    public List<OnlineDownloadCandidate> DownloadFiles { get; set; } = [];

    public bool HasUpdates { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
