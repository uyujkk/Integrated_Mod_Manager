namespace ModFolderCopier.WinUI;

public sealed class OnlineCategoryPageResult
{
    public OnlineCategoryPageResult(List<OnlineModCard> mods, int totalCount, int page, int pageSize)
    {
        Mods = mods;
        TotalCount = totalCount;
        Page = page;
        PageSize = Math.Max(1, pageSize);
    }

    public List<OnlineModCard> Mods { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public DateTimeOffset? CachedAtUtc { get; init; }

    public bool IsFromCache { get; init; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
