using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

public sealed class OnlineCategoryPageCacheEntry
{
    public DateTimeOffset CachedAtUtc { get; set; }

    public List<OnlineModCard> Mods { get; set; } = [];

    public int TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; } = 20;
}
