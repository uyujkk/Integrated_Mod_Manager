namespace IntegratedModManager.Core.Online;

public sealed record OnlineCharacterCatalogCacheSnapshot(
    List<OnlineCharacterOption> Characters,
    DateTimeOffset CheckedAtUtc,
    bool IsFresh);
