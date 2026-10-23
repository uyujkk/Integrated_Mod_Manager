namespace ModFolderCopier.WinUI;

public sealed record OnlineCharacterCatalogCacheSnapshot(
    List<OnlineCharacterOption> Characters,
    DateTimeOffset CheckedAtUtc,
    bool IsFresh);
