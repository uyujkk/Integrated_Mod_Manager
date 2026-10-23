namespace ModFolderCopier.WinUI;

public sealed class OnlineCharacterCatalogCacheEntry
{
    public DateTimeOffset CachedAtUtc { get; set; }

    public List<OnlineCharacterOption> Characters { get; set; } = [];
}
