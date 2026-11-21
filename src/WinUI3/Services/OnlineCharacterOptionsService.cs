using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The character list of the current repository's catalog (stage J finish, step 5b: moved from the Online page
/// unchanged): built-in Endfield characters, the loaded wiki catalog and the characters the listed Mods name. The
/// Online page shows it, and favourites, workspace avatars and the category mapping dialog read it, so it is kept up to
/// date whether or not the page exists.
/// </summary>
internal sealed class OnlineCharacterOptionsService
{
    private readonly AppServices _services;

    public OnlineCharacterOptionsService(AppServices services)
    {
        _services = services;
        services.CharacterCatalog.Loaded += (_, loaded) => ApplyCatalog(loaded.Key, loaded.Characters);
    }

    /// <summary>A loaded catalog replaced the list; true when the selected character was no longer in it and was cleared.</summary>
    public event EventHandler<bool>? CatalogApplied;

    /// <summary>The list for the repository's catalog; true when it was reset because the catalog changed.</summary>
    public bool Ensure(WorkspaceRepository? repository)
    {
        bool reset = false;
        string catalogKey = _services.CharacterCatalog.CatalogKey(repository);
        if (!string.Equals(_services.Online.CharacterCatalogKey, catalogKey, StringComparison.Ordinal))
        {
            _services.Online.CharacterCatalogKey = catalogKey;
            _services.Online.Page.CharacterOptions.Clear();
            _services.Online.Page.Mods.Clear();
            _services.Online.Page.CharacterFilter = string.Empty;
            _services.Online.Page.ClearTotals();
            reset = true;
        }

        if (repository is null)
        {
            return reset;
        }

        if (_services.OnlineSource.IsEndfield(repository))
        {
            if (_services.Online.Page.CharacterOptions.Count == 0)
            {
                _services.Online.Page.CharacterOptions.AddRange(EndfieldCharacterCatalog.BuiltIn.Select((character, index) => new OnlineCharacterOption
                {
                    Key = character.EnglishName,
                    DisplayNameZh = character.ChineseName,
                    DisplayNameEn = character.EnglishName,
                    CategoryId = character.GameBananaCategoryId,
                    AvatarUrl = character.AvatarUrl,
                    WikiUrl = repository.WikiUrl,
                    Aliases = [.. character.AllNames],
                    IsWikiCharacter = true,
                    WikiOrder = index
                }));
            }

            return reset;
        }

        MergeFromMods(_services.Online.Page.Mods);
        return reset;
    }

    /// <summary>Adds the characters the listed Mods name; true when the list changed.</summary>
    public bool MergeFromMods(IEnumerable<OnlineModCard> mods)
    {
        bool changed = false;
        foreach (OnlineModCard mod in mods)
        {
            string name = mod.CharacterName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || OnlineDownloadService.IsGenericOnlineCharacterName(name))
            {
                continue;
            }

            OnlineCharacterOption? existing = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
                string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase)
                || item.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (existing is null)
            {
                var modCharacter = new OnlineCharacterOption
                {
                    Key = name,
                    DisplayNameZh = name,
                    DisplayNameEn = name,
                    Aliases = [name]
                };
                var rankedMatches = _services.Online.Page.CharacterOptions
                    .Where(item => item.IsWikiCharacter)
                    .Select(item => new
                    {
                        Character = item,
                        Score = OnlineCharacterCatalogMatcher.MatchScore(item, modCharacter)
                    })
                    .OrderByDescending(match => match.Score)
                    .Take(2)
                    .ToList();
                if (rankedMatches.Count > 0)
                {
                    double bestScore = rankedMatches[0].Score;
                    double secondScore = rankedMatches.Count > 1 ? rankedMatches[1].Score : 0;
                    if (bestScore >= 0.74 && (bestScore >= 0.94 || bestScore - secondScore >= 0.08))
                    {
                        existing = rankedMatches[0].Character;
                    }
                }
            }

            if (existing is null)
            {
                _services.Online.Page.CharacterOptions.Add(new OnlineCharacterOption
                {
                    Key = name,
                    DisplayNameZh = name,
                    DisplayNameEn = name,
                    CategoryId = mod.CategoryId,
                    AvatarUrl = mod.PreviewUrl,
                    Aliases = [name],
                    LatestModUpdatedAt = mod.UpdatedAt
                });
                changed = true;
                continue;
            }

            if (!existing.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                existing.Aliases.Add(name);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(existing.CategoryId) && !string.IsNullOrWhiteSpace(mod.CategoryId))
            {
                existing.CategoryId = mod.CategoryId;
                changed = true;
            }

            if (!existing.IsWikiCharacter
                && string.IsNullOrWhiteSpace(existing.AvatarUrl)
                && !string.IsNullOrWhiteSpace(mod.PreviewUrl))
            {
                existing.AvatarUrl = mod.PreviewUrl;
                changed = true;
            }

            if (mod.UpdatedAt > existing.LatestModUpdatedAt)
            {
                existing.LatestModUpdatedAt = mod.UpdatedAt;
                changed = true;
            }
        }

        if (changed)
        {
            OnlineCharacterCatalogMatcher.SortByWikiOrderThenNewest(_services.Online.Page.CharacterOptions);
            if (!string.IsNullOrWhiteSpace(_services.Online.CharacterCatalogKey))
            {
                _ = _services.CharacterCatalog.WriteCacheAsync(
                    _services.Online.CharacterCatalogKey,
                    _services.Online.Page.CharacterOptions,
                    preserveWikiCheckedAt: true);
            }
        }

        return changed;
    }

    // A catalog loaded for the list's repository replaces it (from CharacterCatalogService.Loaded).
    private void ApplyCatalog(string catalogKey, List<OnlineCharacterOption> characters)
    {
        if (!string.Equals(_services.Online.CharacterCatalogKey, catalogKey, StringComparison.Ordinal))
        {
            return;
        }

        OnlineCharacterCatalogMatcher.SortByWikiOrderThenNewest(characters);
        _services.Online.Page.CharacterOptions.Clear();
        _services.Online.Page.CharacterOptions.AddRange(characters);
        // Mod pages and the monthly Wiki catalog run independently; merge any page already on screen.
        MergeFromMods(_services.Online.Page.Mods);
        bool filterCleared = false;
        if (!string.IsNullOrWhiteSpace(_services.Online.Page.CharacterFilter)
            && !_services.Online.Page.CharacterOptions.Any(character => string.Equals(
                character.Key,
                _services.Online.Page.CharacterFilter,
                StringComparison.OrdinalIgnoreCase)))
        {
            _services.Online.Page.CharacterFilter = string.Empty;
            _services.Online.Page.CurrentPage = 1;
            filterCleared = true;
        }

        CatalogApplied?.Invoke(this, filterCleared);
    }
}
