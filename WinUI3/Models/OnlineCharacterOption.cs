namespace ModFolderCopier.WinUI;

public sealed class OnlineCharacterOption
{
    public string Key { get; set; } = string.Empty;

    public string DisplayNameZh { get; set; } = string.Empty;

    public string DisplayNameEn { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public string WikiUrl { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    public bool IsWikiCharacter { get; set; }

    public int WikiOrder { get; set; } = int.MaxValue;

    public DateTimeOffset LatestModUpdatedAt { get; set; }
}
