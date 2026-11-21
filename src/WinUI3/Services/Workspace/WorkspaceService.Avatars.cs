using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ModFolderCopier.WinUI;

// Avatars for repository character folders (stage J finish, step 7b: moved from the workspace page unchanged): the
// folder's own image, the character the folder is bound to, or the catalog character with the same name
// (CharacterFolderAvatars). The catalog is the online character catalog (official list and GameBanana categories), with
// the built-in Endfield list filling gaps. Without a fresh catalog one is loaded in the background once per session, so
// newly released characters get their avatars. The dashboard's character chips and the workspace show them.
internal sealed partial class WorkspaceService
{
    private const int CharacterAvatarDecodeWidth = 64;

    private CancellationTokenSource _characterAvatarCancellation = new();

    private readonly HashSet<string> _avatarCatalogRefreshRequested = new(StringComparer.Ordinal);

    internal async Task RefreshCharacterFolderAvatarsAsync()
    {
        _characterAvatarCancellation.Cancel();
        _characterAvatarCancellation.Dispose();
        _characterAvatarCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _characterAvatarCancellation.Token;

        WorkspaceRepository? repository = _services.Repositories.Selected;
        FirstLevelFolderItem[] items = [.. Characters.All];
        if (repository is null || items.Length == 0)
        {
            return;
        }

        try
        {
            (IReadOnlyList<OnlineCharacterOption> catalog, bool fresh) = await GetCharacterAvatarCatalogAsync(repository);
            var bindings = new Dictionary<string, string>(repository.CharacterAvatarBindings ?? [], StringComparer.OrdinalIgnoreCase);
            CharacterAvatar[] avatars = await Task.Run(
                () => items.Select(item => CharacterFolderAvatars.Resolve(item.Path, catalog, bindings)).ToArray(),
                cancellationToken);
            for (int index = 0; index < items.Length && !cancellationToken.IsCancellationRequested; index++)
            {
                await ApplyCharacterAvatarAsync(items[index], avatars[index], cancellationToken);
            }

            if (!fresh && !cancellationToken.IsCancellationRequested && _avatarCatalogRefreshRequested.Add(repository.Id))
            {
                await _services.CharacterCatalog.EnsureAsync(repository, forceReload: false);
                if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_services.Repositories.Selected, repository))
                {
                    await RefreshCharacterFolderAvatarsAsync();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log("Character folder avatars", exception);
        }
    }

    internal async Task RefreshCharacterFolderAvatarAsync(FirstLevelFolderItem item)
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null)
        {
            return;
        }

        (IReadOnlyList<OnlineCharacterOption> catalog, _) = await GetCharacterAvatarCatalogAsync(repository);
        var bindings = new Dictionary<string, string>(repository.CharacterAvatarBindings ?? [], StringComparer.OrdinalIgnoreCase);
        item.AvatarImageSource = null;
        await ApplyCharacterAvatarAsync(item, CharacterFolderAvatars.Resolve(item.Path, catalog, bindings), _characterAvatarCancellation.Token);
    }

    private async Task ApplyCharacterAvatarAsync(FirstLevelFolderItem item, CharacterAvatar avatar, CancellationToken cancellationToken)
    {
        try
        {
            BitmapImage? bitmap = avatar switch
            {
                { FilePath: { } path } => await ImageService.LoadLocalBitmapAsync(path, CharacterAvatarDecodeWidth),
                { ImageUrl: { } url } => await _services.Images.LoadOnlineBitmapAsync(url, cancellationToken, CharacterAvatarDecodeWidth),
                _ => null
            };
            if (!cancellationToken.IsCancellationRequested)
            {
                item.AvatarImageSource = bitmap;
                _avatarLoaded(item);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // An unreadable image leaves the initial placeholder.
            _log("Character folder avatar " + item.Name, exception);
        }
    }

    // The catalog for avatar matching and whether it is fresh (see CharacterCatalogService.FreshFor).
    internal async Task<(IReadOnlyList<OnlineCharacterOption> Characters, bool Fresh)> GetCharacterAvatarCatalogAsync(WorkspaceRepository repository)
    {
        string catalogKey = _services.CharacterCatalog.CatalogKey(repository);
        OnlineCharacterCatalogCacheSnapshot? cached = await _services.CharacterCatalog.TryReadCacheAsync(catalogKey);
        IReadOnlyList<OnlineCharacterOption> online =
            string.Equals(_services.Online.CharacterCatalogKey, catalogKey, StringComparison.Ordinal) && _services.Online.Page.CharacterOptions.Count > 0
                ? [.. _services.Online.Page.CharacterOptions]
                : cached?.Characters ?? [];
        if (!_services.OnlineSource.IsEndfield(repository))
        {
            return (online, cached?.IsFresh == true);
        }

        OnlineCharacterOption[] builtIn = EndfieldCharacterCatalog.BuiltIn.Select((character, index) => new OnlineCharacterOption
        {
            Key = character.EnglishName,
            DisplayNameZh = character.ChineseName,
            DisplayNameEn = character.EnglishName,
            AvatarUrl = character.AvatarUrl,
            Aliases = [.. character.AllNames],
            WikiOrder = index
        }).ToArray();
        return (OnlineCharacterCatalogMatcher.WithFallback(online, builtIn), cached?.IsFresh == true);
    }
}
