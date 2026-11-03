using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void PopulateOnlineCharacterOptions()
    {
        EnsureOnlineCharacterOptionsForRepository(GetSelectedRepository());
        IReadOnlyList<OnlineCharacterOption> visibleCharacters = GetVisibleOnlineCharacterOptions();
        _isApplyingOnlineCharacterSelection = true;
        try
        {
            OnlineCharacterComboBox.Items.Clear();
            OnlineCharacterComboBox.Items.Add(new ComboBoxItem
            {
                Content = T("OnlineCharacters.AllCharacters"),
                Tag = string.Empty
            });

            foreach (OnlineCharacterOption character in visibleCharacters)
            {
                OnlineCharacterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = GetOnlineCharacterDisplayName(character),
                    Tag = character.Key
                });
            }

            ComboBoxItem? selectedItem = OnlineCharacterComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string ?? string.Empty, _onlinePage.CharacterFilter, StringComparison.OrdinalIgnoreCase));

            OnlineCharacterComboBox.SelectedItem = selectedItem ?? OnlineCharacterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isApplyingOnlineCharacterSelection = false;
        }
    }

    private IReadOnlyList<OnlineCharacterOption> GetVisibleOnlineCharacterOptions()
    {
        List<OnlineCharacterOption> wikiCharacters = _onlinePage.CharacterOptions
            .Where(character => character.IsWikiCharacter)
            .ToList();
        IEnumerable<OnlineCharacterOption> source = wikiCharacters.Count > 0 ? wikiCharacters : _onlinePage.CharacterOptions;
        return source
            .OrderByDescending(character => IsFavoriteCharacter(character.Key))
            .ThenBy(character => character.WikiOrder)
            .ThenBy(character => GetOnlineCharacterDisplayName(character), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void EnsureOnlineCharacterOptionsForRepository(WorkspaceRepository? repository)
    {
        string catalogKey = GetOnlineCharacterCatalogKey(repository);
        if (!string.Equals(_onlineCharacterCatalogKey, catalogKey, StringComparison.Ordinal))
        {
            _onlineCharacterCatalogKey = catalogKey;
            _onlinePage.CharacterOptions.Clear();
            _onlinePage.Mods.Clear();
            _onlinePage.CharacterFilter = string.Empty;
            _onlinePage.ClearTotals();
            _onlineCharacterStripSignature = null;
        }

        if (repository is null)
        {
            return;
        }

        if (IsEndfieldRepository(repository))
        {
            if (_onlinePage.CharacterOptions.Count == 0)
            {
                _onlinePage.CharacterOptions.AddRange(EndfieldCharacters.Select((character, index) => new OnlineCharacterOption
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

            return;
        }

        MergeOnlineCharacterOptionsFromMods(_onlinePage.Mods);
    }

    private void MergeOnlineCharacterOptionsFromMods(IEnumerable<OnlineModCard> mods)
    {
        bool changed = false;
        foreach (OnlineModCard mod in mods)
        {
            string name = mod.CharacterName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || IsGenericOnlineCharacterName(name))
            {
                continue;
            }

            OnlineCharacterOption? existing = _onlinePage.CharacterOptions.FirstOrDefault(item =>
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
                var rankedMatches = _onlinePage.CharacterOptions
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
                _onlinePage.CharacterOptions.Add(new OnlineCharacterOption
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
            OnlineCharacterCatalogMatcher.SortByWikiOrderThenNewest(_onlinePage.CharacterOptions);
            _onlineCharacterStripSignature = null;
            if (!string.IsNullOrWhiteSpace(_onlineCharacterCatalogKey))
            {
                _ = WriteOnlineCharacterCatalogCacheAsync(
                    _onlineCharacterCatalogKey,
                    _onlinePage.CharacterOptions,
                    preserveWikiCheckedAt: true);
            }
        }
    }


    private string GetOnlineCharacterCatalogKey(WorkspaceRepository? repository)
    {
        return repository is null
            ? string.Empty
            : $"wiki-monthly-v4|{repository.Id}|wiki={repository.WikiUrl.Trim()}";
    }

    private bool IsEndfieldRepository(WorkspaceRepository? repository)
    {
        if (repository is null)
        {
            return false;
        }

        string categoryId = GetEffectiveOnlineCategoryId(repository);
        string gameName = GetEffectiveOnlineGameName(repository);
        return string.Equals(categoryId, DefaultOnlineCategoryId, StringComparison.OrdinalIgnoreCase)
            || gameName.Contains("Endfield", StringComparison.OrdinalIgnoreCase)
            || gameName.Contains("终末地", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEndfieldCategoryId(string? categoryId)
    {
        return !string.IsNullOrWhiteSpace(categoryId)
            && (string.Equals(categoryId, DefaultOnlineCategoryId, StringComparison.OrdinalIgnoreCase)
                || EndfieldCharacters.Any(character => string.Equals(
                    character.GameBananaCategoryId,
                    categoryId,
                    StringComparison.OrdinalIgnoreCase)));
    }

    private string GetOnlineCharacterDisplayName(OnlineCharacterOption character)
    {
        return _currentLanguage == AppLanguage.ZhCn
            ? character.DisplayNameZh
            : character.DisplayNameEn;
    }

    private void RefreshOnlineCharacterAvatarStrip()
    {
        EnsureOnlineCharacterOptionsForRepository(GetSelectedRepository());
        IReadOnlyList<OnlineCharacterOption> visibleCharacters = GetVisibleOnlineCharacterOptions();
        string characterSignature = string.Join(";", visibleCharacters.Select(item => $"{item.Key}:{item.DisplayNameZh}:{item.DisplayNameEn}:{item.CategoryId}:{item.AvatarUrl}:{item.WikiOrder}"));
        string signature = $"{_currentLanguage}|{_isDarkTheme}|{_isLoadingOnlineMods}|{_onlinePage.CharacterFilter}|{_onlinePage.TotalCount}|horizontal={_useHorizontalOnlineCharacterRail}|{characterSignature}";
        if (string.Equals(signature, _onlineCharacterStripSignature, StringComparison.Ordinal))
        {
            return;
        }

        _onlineCharacterStripSignature = signature;
        _onlineCharacterAvatarCancellation.Cancel();
        _onlineCharacterAvatarCancellation.Dispose();
        _onlineCharacterAvatarCancellation = new CancellationTokenSource();
        CancellationToken avatarCancellationToken = _onlineCharacterAvatarCancellation.Token;
        int avatarRequestVersion = ++_onlineCharacterAvatarRequestVersion;
        OnlineCharacterAvatarPanel.Children.Clear();

        OnlineCharacterAvatarPanel.Children.Add(CreateOnlineCharacterButton(
            string.Empty,
            T("OnlineCharacters.All"),
            null,
            string.IsNullOrWhiteSpace(_onlinePage.CharacterFilter) ? _onlinePage.TotalCount : null,
            avatarRequestVersion,
            avatarCancellationToken));

        foreach (OnlineCharacterOption character in visibleCharacters)
        {
            int? characterModCount = string.Equals(_onlinePage.CharacterFilter, character.Key, StringComparison.OrdinalIgnoreCase)
                ? _onlinePage.TotalCount
                : null;
            OnlineCharacterAvatarPanel.Children.Add(CreateOnlineCharacterButton(
                character.Key,
                GetOnlineCharacterDisplayName(character),
                character.AvatarUrl,
                characterModCount,
                avatarRequestVersion,
                avatarCancellationToken));
        }

        string currentRole = string.IsNullOrWhiteSpace(_onlinePage.CharacterFilter)
            ? T("OnlineCharacters.All")
            : GetOnlineCharacterDisplayName(_onlinePage.CharacterFilter);
        OnlineCharacterRailHintTextBlock.Text = T("OnlineCharacters.CategoriesCurrent", visibleCharacters.Count, currentRole);
    }

    private FrameworkElement CreateOnlineCharacterButton(
        string character,
        string label,
        string? avatarUrl,
        int? modCount,
        int avatarRequestVersion,
        CancellationToken avatarCancellationToken)
    {
        bool isSelected = string.Equals(character, _onlinePage.CharacterFilter, StringComparison.OrdinalIgnoreCase);
        double avatarSize = _useHorizontalOnlineCharacterRail ? 46 : 42;
        var avatarHost = new Border
        {
            Width = avatarSize,
            Height = avatarSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(avatarSize / 2),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            BorderBrush = GetAppThemeBrush(isSelected ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush"),
            Background = GetAppThemeBrush("AppInsetBackgroundBrush")
        };

        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            var avatarLayer = new Grid();
            avatarLayer.Children.Add(new FontIcon
            {
                Glyph = "\uE77B",
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            var avatar = new Image
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.UniformToFill,
                Opacity = 0
            };
            avatarLayer.Children.Add(avatar);
            avatarHost.Child = avatarLayer;

            _ = SetCachedOnlineCharacterAvatarAsync(
                avatar, avatarUrl, avatarRequestVersion, avatarCancellationToken);
        }
        else
        {
            avatarHost.Child = new FontIcon
            {
                Glyph = string.IsNullOrWhiteSpace(character) ? "\uE77B" : "\uE77B",
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        FrameworkElement content;
        if (_useHorizontalOnlineCharacterRail)
        {
            var compactCard = new StackPanel
            {
                Width = 82,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            compactCard.Children.Add(avatarHost);
            compactCard.Children.Add(new TextBlock
            {
                Text = label,
                MaxWidth = 82,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12,
                FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            });
            if (modCount.HasValue)
            {
                compactCard.Children.Add(new TextBlock
                {
                    Text = T("OnlineCharacters.Mods", modCount.Value),
                    MaxWidth = 82,
                    TextAlignment = TextAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"]
                });
            }

            content = compactCard;
        }
        else
        {
            var compactRow = new Grid
            {
                ColumnSpacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            compactRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            compactRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (!string.IsNullOrWhiteSpace(character))
            {
                // Keep labels and counts clear of the overlaid favorite action.
                compactRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            }
            compactRow.Children.Add(avatarHost);

            var textPanel = new StackPanel
            {
                Spacing = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(textPanel, 1);
            textPanel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            });
            if (modCount.HasValue)
            {
                textPanel.Children.Add(new TextBlock
                {
                    Text = T("OnlineCharacters.Mods2", modCount.Value),
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                });
            }

            compactRow.Children.Add(textPanel);
            content = compactRow;
        }

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(5),
            MinWidth = _useHorizontalOnlineCharacterRail ? 92 : 0,
            MinHeight = _useHorizontalOnlineCharacterRail ? 80 : 54,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            IsEnabled = !_isLoadingOnlineMods,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = _useHorizontalOnlineCharacterRail
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch,
            Background = GetAppThemeBrush(isSelected ? "AppSecondarySelectedBrush" : "AppSecondaryDefaultBrush"),
            BorderBrush = GetAppThemeBrush(isSelected ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush"),
            Foreground = GetAppThemeBrush(isSelected ? "AppSecondarySelectedForegroundBrush" : "AppSecondaryDefaultForegroundBrush")
        };
        OnlineCharacterOption? characterOption = _onlinePage.CharacterOptions.FirstOrDefault(item =>
            string.Equals(item.Key, character, StringComparison.OrdinalIgnoreCase));
        string tooltip = characterOption is null
            ? label
            : $"{characterOption.DisplayNameZh} / {characterOption.DisplayNameEn}";
        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(character) ? T("OnlineCharacters.ShowAllCharacters") : tooltip);
        AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(character) ? T("OnlineCharacters.AllCharacters") : label);
        button.Click += async (_, _) => await RunUiActionAsync("SelectOnlineCharacterAsync", () => SelectOnlineCharacterAsync(character));
        return WrapCharacterButtonWithFavorite(button, character);
    }

    private string GetOnlineCharacterDisplayName(EndfieldCharacterInfo character)
    {
        return _currentLanguage == AppLanguage.ZhCn ? character.ChineseName : character.EnglishName;
    }

    private string GetOnlineCharacterDisplayName(string characterName)
    {
        OnlineCharacterOption? option = _onlinePage.CharacterOptions.FirstOrDefault(item =>
            string.Equals(item.Key, characterName, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            return GetOnlineCharacterDisplayName(option);
        }

        if (IsEndfieldRepository(GetSelectedRepository()))
        {
            EndfieldCharacterInfo? character = FindEndfieldCharacter(characterName);
            return character is null ? characterName : GetOnlineCharacterDisplayName(character);
        }

        return characterName;
    }

    private bool DoesOnlineModMatchCharacter(string characterKey, string modCharacterName)
    {
        if (string.Equals(characterKey, modCharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        OnlineCharacterOption? character = _onlinePage.CharacterOptions.FirstOrDefault(option =>
            string.Equals(option.Key, characterKey, StringComparison.OrdinalIgnoreCase));
        if (character is null)
        {
            return false;
        }

        if (character.Aliases.Contains(modCharacterName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var candidate = new OnlineCharacterOption
        {
            Key = modCharacterName,
            DisplayNameZh = modCharacterName,
            DisplayNameEn = modCharacterName,
            Aliases = [modCharacterName]
        };
        return OnlineCharacterCatalogMatcher.MatchScore(character, candidate) >= 0.82;
    }

    private string ResolveOnlineCharacterName(string? categoryName, string title, bool useEndfieldMetadata)
    {
        string normalizedCategory = categoryName?.Trim() ?? string.Empty;
        if (useEndfieldMetadata)
        {
            EndfieldCharacterInfo? categoryCharacter = FindEndfieldCharacter(normalizedCategory);
            if (categoryCharacter is not null)
            {
                return categoryCharacter.EnglishName;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedCategory)
            && !GenericOnlineCharacterNames.Contains(normalizedCategory))
        {
            return normalizedCategory;
        }

        if (useEndfieldMetadata)
        {
            EndfieldCharacterInfo? titleCharacter = EndfieldCharacters.FirstOrDefault(character =>
                character.AllNames.Any(alias => OnlineCharacterCatalogMatcher.ContainsAlias(title, alias)));
            if (titleCharacter is not null)
            {
                return titleCharacter.EnglishName;
            }
        }

        return "Uncategorized";
    }

    private static EndfieldCharacterInfo? FindEndfieldCharacter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string normalized = OnlineCharacterCatalogMatcher.NormalizeLookup(name);
        return EndfieldCharacters.FirstOrDefault(character =>
            character.AllNames.Any(alias => string.Equals(
                OnlineCharacterCatalogMatcher.NormalizeLookup(alias),
                normalized,
                StringComparison.OrdinalIgnoreCase)));
    }



    private static bool IsGenericOnlineCharacterName(string? categoryName)
    {
        return string.IsNullOrWhiteSpace(categoryName)
            || GenericOnlineCharacterNames.Contains(categoryName.Trim());
    }

    private async void OnOnlineCharacterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_isApplyingOnlineCharacterSelection || _restoringBrowseControls)
            {
                return;
            }

            string character = (OnlineCharacterComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
            await SelectOnlineCharacterAsync(character);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnOnlineCharacterSelectionChanged", ex);
        }
    }

    private async Task SelectOnlineCharacterAsync(string character)
    {
        character ??= string.Empty;
        if (string.Equals(character, _onlinePage.CharacterFilter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int animationVersion = ++_onlineCharacterAnimationVersion;
        await AnimateOnlineListExitAsync();
        if (animationVersion != _onlineCharacterAnimationVersion)
        {
            return;
        }

        ResetOnlineBrowsePosition();
        _onlinePage.CharacterFilter = character;
        _onlinePage.ResetPaging();
        _lastLoadedOnlineConfigKey = null;
        _activeOnlineDetailMod = null;
        _isApplyingOnlineCharacterSelection = true;
        try
        {
            OnlineCharacterComboBox.SelectedItem = OnlineCharacterComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string ?? string.Empty, character, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isApplyingOnlineCharacterSelection = false;
        }

        _animateOnlineCardsOnNextRefresh = true;
        await CloseOnlineDetailPaneAsync();
        await LoadOnlineModsAsync(forceReload: false);
    }

    private string GetOnlineCharacterCatalogCacheFile(string catalogKey)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalogKey))).ToLowerInvariant()[..16];
        return Path.Combine(_onlineCharacterCachePath, $"{key}.json");
    }

    // A cached catalog older than this is shown at once and refreshed in the background, so newly
    // released characters (and their avatars) appear within a day. Refreshing the online page reloads now.
    private static readonly TimeSpan OnlineCharacterCatalogFreshFor = TimeSpan.FromDays(1);

    private async Task<OnlineCharacterCatalogCacheSnapshot?> TryReadOnlineCharacterCatalogCacheAsync(string catalogKey)
    {
        CachedValue<OnlineCharacterCatalogCacheEntry>? sqliteEntry = _appDataStore.TryReadCache<OnlineCharacterCatalogCacheEntry>(
            "character-catalog",
            catalogKey,
            OnlineCharacterCatalogFreshFor,
            allowExpired: true);
        if (sqliteEntry?.Value.Characters is { Count: > 0 } storedCharacters)
        {
            return new OnlineCharacterCatalogCacheSnapshot(
                storedCharacters,
                sqliteEntry.CachedAtUtc,
                !sqliteEntry.IsExpired);
        }

        try
        {
            string cacheFile = GetOnlineCharacterCatalogCacheFile(catalogKey);
            if (!File.Exists(cacheFile))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(cacheFile);
            OnlineCharacterCatalogCacheEntry? entry = JsonSerializer.Deserialize<OnlineCharacterCatalogCacheEntry>(json);
            DateTimeOffset checkedAt = entry is not null && entry.CachedAtUtc > DateTimeOffset.MinValue
                ? entry.CachedAtUtc
                : new DateTimeOffset(File.GetLastWriteTimeUtc(cacheFile));
            return entry?.Characters is { Count: > 0 } characters
                ? new OnlineCharacterCatalogCacheSnapshot(
                    characters,
                    checkedAt,
                    DateTimeOffset.UtcNow - checkedAt <= OnlineCharacterCatalogFreshFor)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteOnlineCharacterCatalogCacheAsync(
        string catalogKey,
        List<OnlineCharacterOption> characters,
        bool preserveWikiCheckedAt = false)
    {
        try
        {
            Directory.CreateDirectory(_onlineCharacterCachePath);
            string cacheFile = GetOnlineCharacterCatalogCacheFile(catalogKey);
            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            if (preserveWikiCheckedAt && File.Exists(cacheFile))
            {
                try
                {
                    string existingJson = await File.ReadAllTextAsync(cacheFile);
                    OnlineCharacterCatalogCacheEntry? existingEntry = JsonSerializer.Deserialize<OnlineCharacterCatalogCacheEntry>(existingJson);
                    if (existingEntry?.CachedAtUtc > DateTimeOffset.MinValue)
                    {
                        checkedAt = existingEntry.CachedAtUtc;
                    }
                }
                catch (Exception ex)
                {
                    LogApplicationIssue("Read existing character catalog cache", ex);
                }
            }

            var entry = new OnlineCharacterCatalogCacheEntry
            {
                CachedAtUtc = checkedAt,
                Characters = characters
            };
            _appDataStore.WriteCache("character-catalog", catalogKey, entry, checkedAt);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Write character catalog cache", ex);
        }
    }

    // Each catalog request gets this long, then one more try; see CharacterCatalogClient.WithRetryAsync.
    private static readonly TimeSpan CharacterCatalogRequestTimeout = TimeSpan.FromSeconds(20);

    // The repository's own wiki link, else its game's built-in one (found by category or game name).
    private static WikiCatalogDescriptor? GetWikiCatalogDescriptor(WorkspaceRepository repository)
        => CharacterCatalogClient.Describe(repository.WikiUrl,
            BuiltInRepositoryPresets.FirstOrDefault(preset => IsRepositoryForPreset(repository, preset))?.WikiUrl);

    private Task<T> RequestCharacterCatalogAsync<T>(string operation, Func<CancellationToken, Task<T>> request)
        => CharacterCatalogClient.WithRetryAsync(request, CharacterCatalogRequestTimeout,
            ex => LogApplicationIssue(operation + ", trying again", ex));

    private async Task<List<OnlineCharacterOption>> FetchWikiCharacterCatalogAsync(WorkspaceRepository repository)
    {
        if (IsEndfieldRepository(repository))
        {
            return await FetchEndfieldCharacterCatalogAsync(repository);
        }

        return GetWikiCatalogDescriptor(repository) is { } descriptor
            ? await RequestCharacterCatalogAsync("Load wiki character catalog",
                token => CharacterCatalogClient.FetchWikiCharactersAsync(_httpClient, descriptor, repository.WikiUrl, token))
            : [];
    }

    private Task<List<OnlineCharacterOption>> FetchEndfieldCharacterCatalogAsync(WorkspaceRepository repository)
        => RequestCharacterCatalogAsync("Load Endfield operator catalog",
            token => CharacterCatalogClient.FetchEndfieldOperatorsAsync(_httpClient, EndfieldCharacters,
                string.IsNullOrWhiteSpace(repository.WikiUrl) ? EndfieldOfficialWikiUrl : repository.WikiUrl, token));

    private Task<List<OnlineCharacterOption>> FetchGameBananaCharacterCatalogAsync(string categoryId)
        => RequestCharacterCatalogAsync("Load GameBanana character categories",
            token => CharacterCatalogClient.FetchGameBananaCharactersAsync(_httpClient, categoryId, token));

    private async Task ApplyOfficialHoYoWikiEnglishNamesAsync(
        WorkspaceRepository repository,
        List<OnlineCharacterOption> characters)
    {
        WikiCatalogDescriptor? descriptor = GetWikiCatalogDescriptor(repository);
        if (descriptor is null
            || string.IsNullOrWhiteSpace(descriptor.HoYoWikiApp)
            || string.IsNullOrWhiteSpace(descriptor.HoYoWikiCharacterMenuId))
        {
            return;
        }

        try
        {
            // Fetch sequentially to keep the monthly Wiki refresh gentle and separate from mod loading.
            List<HoYoWikiLocalizedCharacter> chineseCharacters = await RequestCharacterCatalogAsync("Load HoYoWiki names",
                token => CharacterCatalogClient.FetchHoYoWikiNamesAsync(_httpClient, descriptor, "zh-cn", token));
            List<HoYoWikiLocalizedCharacter> englishCharacters = await RequestCharacterCatalogAsync("Load HoYoWiki names",
                token => CharacterCatalogClient.FetchHoYoWikiNamesAsync(_httpClient, descriptor, "en-us", token));
            CharacterCatalogClient.ApplyEnglishNames(characters, chineseCharacters, englishCharacters);
        }
        catch (Exception ex)
        {
            // The existing Chinese catalog and translation fallback remain usable when HoYoWiki is unavailable.
            LogApplicationIssue("Load HoYoWiki names", ex);
        }
    }

    private async Task TranslateWikiCharacterOptionsAsync(List<OnlineCharacterOption> characters)
    {
        List<OnlineCharacterOption> unresolvedCharacters = characters
            .Where(character => string.IsNullOrWhiteSpace(character.DisplayNameEn)
                || string.Equals(
                    OnlineCharacterCatalogMatcher.NormalizeLookup(character.DisplayNameEn),
                    OnlineCharacterCatalogMatcher.NormalizeLookup(character.DisplayNameZh),
                    StringComparison.Ordinal))
            .ToList();
        if (unresolvedCharacters.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> englishNames = await TranslateWikiCharacterNamesAsync(
            unresolvedCharacters.Select(character => character.DisplayNameZh).ToList());
        for (int index = 0; index < unresolvedCharacters.Count && index < englishNames.Count; index++)
        {
            string englishName = englishNames[index].Trim();
            if (string.IsNullOrWhiteSpace(englishName))
            {
                continue;
            }

            unresolvedCharacters[index].DisplayNameEn = englishName;
            if (!unresolvedCharacters[index].Aliases.Contains(englishName, StringComparer.OrdinalIgnoreCase))
            {
                unresolvedCharacters[index].Aliases.Add(englishName);
            }
        }
    }

    private async Task<IReadOnlyList<string>> TranslateWikiCharacterNamesAsync(IReadOnlyList<string> names)
    {
        string[] translatedNames = names.ToArray();
        const int batchSize = 16;
        const string separator = " [[[IMM_NAME]]] ";

        for (int start = 0; start < names.Count; start += batchSize)
        {
            int count = Math.Min(batchSize, names.Count - start);
            string[] batch = names.Skip(start).Take(count).ToArray();
            try
            {
                string translatedBatch = await TranslateChunkAsync(string.Join(separator, batch), "en");
                string[] parts = translatedBatch.Split("[[[IMM_NAME]]]", StringSplitOptions.TrimEntries);
                if (parts.Length == count)
                {
                    for (int index = 0; index < count; index++)
                    {
                        translatedNames[start + index] = parts[index];
                    }
                    continue;
                }

                Task<string>[] translations = batch.Select(name => TranslateChunkAsync(name, "en")).ToArray();
                string[] fallbackResults = await Task.WhenAll(translations);
                for (int index = 0; index < count; index++)
                {
                    translatedNames[start + index] = fallbackResults[index];
                }
            }
            catch
            {
                // Chinese names remain available when the optional translation service is unavailable.
            }
        }

        return translatedNames;
    }






    private async Task EnsureOnlineCharacterCatalogAsync(WorkspaceRepository repository, bool forceReload)
    {
        string catalogKey = GetOnlineCharacterCatalogKey(repository);
        if (string.IsNullOrWhiteSpace(catalogKey) || !_loadingOnlineCharacterCatalogKeys.Add(catalogKey))
        {
            return;
        }

        try
        {
            if (!forceReload)
            {
                OnlineCharacterCatalogCacheSnapshot? cachedCatalog = await TryReadOnlineCharacterCatalogCacheAsync(catalogKey);
                if (cachedCatalog is not null)
                {
                    bool requiresWikiCatalog = GetWikiCatalogDescriptor(repository) is not null
                        || IsEndfieldRepository(repository);
                    if (!requiresWikiCatalog || cachedCatalog.Characters.Any(character => character.IsWikiCharacter))
                    {
                        OnlineCharacterCatalogMatcher.ApplyManualMappings(repository.CharacterCategoryMappings, cachedCatalog.Characters);
                        ApplyOnlineCharacterCatalog(catalogKey, cachedCatalog.Characters);
                        if (cachedCatalog.IsFresh)
                        {
                            return;
                        }
                    }
                }
            }

            List<OnlineCharacterOption> wikiCharacters = await FetchWikiCharacterCatalogAsync(repository);
            if ((GetWikiCatalogDescriptor(repository) is not null || IsEndfieldRepository(repository))
                && wikiCharacters.Count == 0)
            {
                return;
            }

            if (wikiCharacters.Count > 0)
            {
                OnlineCharacterCatalogMatcher.ApplyManualMappings(repository.CharacterCategoryMappings, wikiCharacters);
                ApplyOnlineCharacterCatalog(catalogKey, wikiCharacters);
                await ApplyOfficialHoYoWikiEnglishNamesAsync(repository, wikiCharacters);
                await TranslateWikiCharacterOptionsAsync(wikiCharacters);

                List<OnlineCharacterOption> gameBananaCharacters = [];
                try
                {
                    gameBananaCharacters = await FetchGameBananaCharacterCatalogAsync(
                        GetEffectiveOnlineCategoryId(repository));
                }
                catch (Exception ex)
                {
                    // Wiki names remain usable if the optional category mapping request fails.
                    LogApplicationIssue("Load GameBanana character categories", ex);
                }

                List<OnlineCharacterOption> mergedCharacters = OnlineCharacterCatalogMatcher.Merge(
                    wikiCharacters,
                    gameBananaCharacters,
                    repository.CharacterCategoryMappings);
                OnlineCharacterCatalogMatcher.ApplyManualMappings(repository.CharacterCategoryMappings, mergedCharacters);
                await WriteOnlineCharacterCatalogCacheAsync(catalogKey, mergedCharacters);
                ApplyOnlineCharacterCatalog(catalogKey, mergedCharacters);
            }
        }
        catch (Exception ex)
        {
            // The current page still supplies a usable partial character list; the log says why the catalog did not load.
            LogApplicationIssue("Load online character catalog", ex);
        }
        finally
        {
            _loadingOnlineCharacterCatalogKeys.Remove(catalogKey);
        }
    }


    private void ApplyOnlineCharacterCatalog(string catalogKey, List<OnlineCharacterOption> characters)
    {
        if (!string.Equals(_onlineCharacterCatalogKey, catalogKey, StringComparison.Ordinal))
        {
            return;
        }

        OnlineCharacterCatalogMatcher.SortByWikiOrderThenNewest(characters);
        _onlinePage.CharacterOptions.Clear();
        _onlinePage.CharacterOptions.AddRange(characters);
        // Mod pages and the monthly Wiki catalog run independently; merge any page already on screen.
        MergeOnlineCharacterOptionsFromMods(_onlinePage.Mods);
        if (!string.IsNullOrWhiteSpace(_onlinePage.CharacterFilter)
            && !_onlinePage.CharacterOptions.Any(character => string.Equals(
                character.Key,
                _onlinePage.CharacterFilter,
                StringComparison.OrdinalIgnoreCase)))
        {
            _onlinePage.CharacterFilter = string.Empty;
            _onlinePage.CurrentPage = 1;
            _lastLoadedOnlineConfigKey = null;
        }

        _onlineCharacterStripSignature = null;
        PopulateOnlineCharacterOptions();
        RefreshOnlineCharacterAvatarStrip();
        RefreshOnlinePaneV2();
    }

    private IReadOnlyCollection<string> GetCharacterFolderAliases(OnlineModCard mod)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddAlias(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsGenericOnlineCharacterName(value))
            {
                aliases.Add(value.Trim());
            }
        }

        AddAlias(mod.CharacterName);

        string normalizedCharacterName = OnlineCharacterCatalogMatcher.NormalizeLookup(mod.CharacterName);
        OnlineCharacterOption? option = _onlinePage.CharacterOptions.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(mod.CategoryId)
                && string.Equals(item.CategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(OnlineCharacterCatalogMatcher.NormalizeLookup(item.Key), normalizedCharacterName, StringComparison.OrdinalIgnoreCase)
            || item.Aliases.Any(alias => string.Equals(
                OnlineCharacterCatalogMatcher.NormalizeLookup(alias),
                normalizedCharacterName,
                StringComparison.OrdinalIgnoreCase)));
        if (option is not null)
        {
            AddAlias(option.Key);
            AddAlias(option.DisplayNameZh);
            AddAlias(option.DisplayNameEn);
            foreach (string alias in option.Aliases)
            {
                AddAlias(alias);
            }
        }

        EndfieldCharacterInfo? endfieldCharacter = FindEndfieldCharacter(mod.CharacterName)
            ?? EndfieldCharacters.FirstOrDefault(character =>
                !string.IsNullOrWhiteSpace(mod.CategoryId)
                && string.Equals(
                    character.GameBananaCategoryId,
                    mod.CategoryId,
                    StringComparison.OrdinalIgnoreCase));
        if (endfieldCharacter is not null)
        {
            foreach (string alias in endfieldCharacter.AllNames)
            {
                AddAlias(alias);
            }
        }

        return aliases;
    }

    private (string? Chinese, string? English) GetCharacterFolderNames(OnlineModCard mod)
    {
        string normalized = OnlineCharacterCatalogMatcher.NormalizeLookup(mod.CharacterName);
        OnlineCharacterOption? option = _onlinePage.CharacterOptions.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(mod.CategoryId) && string.Equals(item.CategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(OnlineCharacterCatalogMatcher.NormalizeLookup(item.Key), normalized, StringComparison.OrdinalIgnoreCase));
        EndfieldCharacterInfo? endfield = FindEndfieldCharacter(mod.CharacterName)
            ?? EndfieldCharacters.FirstOrDefault(character => !string.IsNullOrWhiteSpace(mod.CategoryId)
                && string.Equals(character.GameBananaCategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase));
        string? chinese = endfield?.ChineseName ?? option?.DisplayNameZh;
        string? english = endfield?.EnglishName ?? option?.DisplayNameEn;
        // A bare site category name still names the folder in its own script.
        if (!IsGenericOnlineCharacterName(mod.CharacterName))
        {
            if (mod.CharacterName.Any(character => character is >= '一' and <= '鿿')) chinese ??= mod.CharacterName;
            else english ??= mod.CharacterName;
        }
        return (chinese, english);
    }

    private CharacterFolderPlan PlanCharacterFolder(OnlineModCard mod, string repositoryPath, IReadOnlyCollection<string> aliases)
    {
        (string? chinese, string? english) = GetCharacterFolderNames(mod);
        return CharacterFolderPlanner.Plan(repositoryPath,
            Directory.GetDirectories(repositoryPath),
            Directory.GetFiles(repositoryPath).Select(Path.GetFileName).OfType<string>().ToArray(),
            aliases, mod.CharacterName, chinese, english, _currentLanguage == AppLanguage.ZhCn);
    }

    private string CharacterFolderReasonText(CharacterFolderPlan plan, string character) => plan.Reason switch
    {
        CharacterFolderReason.Ambiguous => T("OnlineCharacters.SeveralFoldersMayBelongToChoose", character),
        CharacterFolderReason.UnknownCharacter => T("OnlineCharacters.TheModSCharacterIsUnknown"),
        CharacterFolderReason.MultipleCharacters => T("OnlineCharacters.CoversSeveralCharactersChooseADestination", character),
        CharacterFolderReason.MixedNamingLanguage => T("OnlineCharacters.TheRepositoryMixesChineseAndEnglish"),
        CharacterFolderReason.NoNameInLanguage => T("OnlineCharacters.NoNameForInTheRepository", character),
        CharacterFolderReason.InvalidName => T("OnlineCharacters.CannotBeUsedAsAFolder", character),
        CharacterFolderReason.NameConflict => T("OnlineCharacters.AFileWithThatNameAlready"),
        _ => string.Empty
    };
}
