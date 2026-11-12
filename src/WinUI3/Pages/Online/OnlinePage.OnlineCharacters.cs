using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    internal void PopulateOnlineCharacterOptions()
    {
        EnsureOnlineCharacterOptionsForRepository(_services.Repositories.Selected);
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
            if (CurrentOnlineCategories() is { CharacterTop: { } characterTop } index && index.CharacterTopId != OnlineRepositoryCategoryId())
                OnlineCharacterComboBox.Items.Add(new ComboBoxItem { Content = T("OnlineCategories.AllOfTop", characterTop.Name), Tag = AllCharacterTopKey });

            foreach (OnlineCharacterOption character in visibleCharacters)
            {
                OnlineCharacterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = GetOnlineCharacterDisplayName(character),
                    Tag = character.Key
                });
            }

            string selectedKey = ShowsAllOfCharacterTop() ? AllCharacterTopKey : _services.Online.Page.CharacterFilter;
            ComboBoxItem? selectedItem = OnlineCharacterComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string ?? string.Empty, selectedKey, StringComparison.OrdinalIgnoreCase));

            OnlineCharacterComboBox.SelectedItem = selectedItem ?? OnlineCharacterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isApplyingOnlineCharacterSelection = false;
        }
    }

    private IReadOnlyList<OnlineCharacterOption> GetVisibleOnlineCharacterOptions()
    {
        List<OnlineCharacterOption> wikiCharacters = _services.Online.Page.CharacterOptions
            .Where(character => character.IsWikiCharacter)
            .ToList();
        IEnumerable<OnlineCharacterOption> source = wikiCharacters.Count > 0 ? wikiCharacters : _services.Online.Page.CharacterOptions;
        return source
            .OrderByDescending(character => _host.IsFavoriteCharacter(character.Key))
            .ThenBy(character => character.WikiOrder)
            .ThenBy(character => GetOnlineCharacterDisplayName(character), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal void EnsureOnlineCharacterOptionsForRepository(WorkspaceRepository? repository)
    {
        string catalogKey = _services.CharacterCatalog.CatalogKey(repository);
        if (!string.Equals(_services.Online.CharacterCatalogKey, catalogKey, StringComparison.Ordinal))
        {
            _services.Online.CharacterCatalogKey = catalogKey;
            _services.Online.Page.CharacterOptions.Clear();
            _services.Online.Page.Mods.Clear();
            _services.Online.Page.CharacterFilter = string.Empty;
            _services.Online.Page.ClearTotals();
            _onlineCharacterStripSignature = null;
        }

        if (repository is null)
        {
            return;
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

            return;
        }

        MergeOnlineCharacterOptionsFromMods(_services.Online.Page.Mods);
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
            _onlineCharacterStripSignature = null;
            if (!string.IsNullOrWhiteSpace(_services.Online.CharacterCatalogKey))
            {
                _ = _services.CharacterCatalog.WriteCacheAsync(
                    _services.Online.CharacterCatalogKey,
                    _services.Online.Page.CharacterOptions,
                    preserveWikiCheckedAt: true);
            }
        }
    }

    private string GetOnlineCharacterDisplayName(OnlineCharacterOption character)
    {
        return IsChineseUi
            ? character.DisplayNameZh
            : character.DisplayNameEn;
    }

    internal void RefreshOnlineCharacterAvatarStrip()
    {
        EnsureOnlineCharacterOptionsForRepository(_services.Repositories.Selected);
        IReadOnlyList<OnlineCharacterOption> visibleCharacters = GetVisibleOnlineCharacterOptions();
        string characterSignature = string.Join(";", visibleCharacters.Select(item => $"{item.Key}:{item.DisplayNameZh}:{item.DisplayNameEn}:{item.CategoryId}:{item.AvatarUrl}:{item.WikiOrder}"));
        OnlineCategory? allTop = CurrentOnlineCategories() is { CharacterTop: { } top } categories && categories.CharacterTopId != OnlineRepositoryCategoryId() ? top : null;
        string signature = $"{_services.Language.Current}|{_services.Appearance.IsDark}|{_isLoadingOnlineMods}|{_services.Online.Page.CharacterFilter}|{_services.Online.Page.TotalCount}|horizontal={_useHorizontalOnlineCharacterRail}|{characterSignature}"
            + $"|allTop={allTop?.Name}:{ShowsAllOfCharacterTop()}";
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
            string.IsNullOrWhiteSpace(_services.Online.Page.CharacterFilter) && !ShowsAllOfCharacterTop() ? _services.Online.Page.TotalCount : null,
            avatarRequestVersion,
            avatarCancellationToken));

        // Issue #129: every Mod under the top-level category, including weapons, NPCs and Mods without a character.
        if (allTop is not null)
        {
            OnlineCharacterAvatarPanel.Children.Add(CreateOnlineCharacterButton(
                AllCharacterTopKey,
                T("OnlineCategories.AllOfTop", allTop.Name),
                null,
                ShowsAllOfCharacterTop() ? _services.Online.Page.TotalCount : null,
                avatarRequestVersion,
                avatarCancellationToken));
        }

        foreach (OnlineCharacterOption character in visibleCharacters)
        {
            int? characterModCount = string.Equals(_services.Online.Page.CharacterFilter, character.Key, StringComparison.OrdinalIgnoreCase)
                ? _services.Online.Page.TotalCount
                : null;
            OnlineCharacterAvatarPanel.Children.Add(CreateOnlineCharacterButton(
                character.Key,
                GetOnlineCharacterDisplayName(character),
                character.AvatarUrl,
                characterModCount,
                avatarRequestVersion,
                avatarCancellationToken));
        }

        string currentRole = string.IsNullOrWhiteSpace(_services.Online.Page.CharacterFilter)
            ? T("OnlineCharacters.All")
            : GetOnlineCharacterDisplayName(_services.Online.Page.CharacterFilter);
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
        bool isSelected = character == AllCharacterTopKey ? ShowsAllOfCharacterTop()
            : !ShowsAllOfCharacterTop() && string.Equals(character, _services.Online.Page.CharacterFilter, StringComparison.OrdinalIgnoreCase);
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
        OnlineCharacterOption? characterOption = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
            string.Equals(item.Key, character, StringComparison.OrdinalIgnoreCase));
        string tooltip = characterOption is null
            ? label
            : $"{characterOption.DisplayNameZh} / {characterOption.DisplayNameEn}";
        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(character) ? T("OnlineCharacters.ShowAllCharacters")
            : character == AllCharacterTopKey ? T("OnlineCategories.AllOfTopHint", CurrentOnlineCategories()?.CharacterTop?.Name ?? label) : tooltip);
        AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(character) ? T("OnlineCharacters.AllCharacters") : label);
        button.Click += async (_, _) => await _host.RunUiActionAsync("SelectOnlineCharacterAsync", () => SelectOnlineCharacterAsync(character));
        return character == AllCharacterTopKey ? button : WrapCharacterButtonWithFavorite(button, character);
    }

    private string GetOnlineCharacterDisplayName(EndfieldCharacterInfo character)
    {
        return IsChineseUi ? character.ChineseName : character.EnglishName;
    }

    private string GetOnlineCharacterDisplayName(string characterName)
    {
        OnlineCharacterOption? option = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
            string.Equals(item.Key, characterName, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            return GetOnlineCharacterDisplayName(option);
        }

        if (_services.OnlineSource.IsEndfield(_services.Repositories.Selected))
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

        OnlineCharacterOption? character = _services.Online.Page.CharacterOptions.FirstOrDefault(option =>
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
            EndfieldCharacterInfo? titleCharacter = EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character =>
                character.AllNames.Any(alias => OnlineCharacterCatalogMatcher.ContainsAlias(title, alias)));
            if (titleCharacter is not null)
            {
                return titleCharacter.EnglishName;
            }
        }

        return "Uncategorized";
    }

    private EndfieldCharacterInfo? FindEndfieldCharacter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string normalized = OnlineCharacterCatalogMatcher.NormalizeLookup(name);
        return EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character =>
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
            _host.ReportUiActionFailure("OnOnlineCharacterSelectionChanged", ex);
        }
    }

    private async Task SelectOnlineCharacterAsync(string character)
    {
        character ??= string.Empty;
        bool allOfTop = character == AllCharacterTopKey;
        if (allOfTop) character = string.Empty;
        if (allOfTop == ShowsAllOfCharacterTop()
            && string.Equals(character, _services.Online.Page.CharacterFilter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (allOfTop != _services.Online.Page.ShowAllOfCharacterTop)
        {
            _services.Online.Page.ShowAllOfCharacterTop = allOfTop;
            SaveOnlineCategorySelection();
            PopulateOnlineCategoryBars();
        }

        // The new character's cards fade in when they arrive; the old ones no longer fade out first,
        // which held every switch for 135 ms before anything changed (stage J6-b).
        ResetOnlineBrowsePosition();
        _services.Online.Page.CharacterFilter = character;
        _services.Online.Page.ResetPaging();
        _lastLoadedOnlineConfigKey = null;
        _activeOnlineDetailMod = null;
        _isApplyingOnlineCharacterSelection = true;
        try
        {
            string comboKey = allOfTop ? AllCharacterTopKey : character;
            OnlineCharacterComboBox.SelectedItem = OnlineCharacterComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string ?? string.Empty, comboKey, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isApplyingOnlineCharacterSelection = false;
        }

        _animateOnlineCardsOnNextRefresh = true;
        await CloseOnlineDetailPaneAsync();
        await LoadOnlineModsAsync(forceReload: false);
    }

    private void ApplyOnlineCharacterCatalog(string catalogKey, List<OnlineCharacterOption> characters)
    {
        if (!string.Equals(_services.Online.CharacterCatalogKey, catalogKey, StringComparison.Ordinal))
        {
            return;
        }

        OnlineCharacterCatalogMatcher.SortByWikiOrderThenNewest(characters);
        _services.Online.Page.CharacterOptions.Clear();
        _services.Online.Page.CharacterOptions.AddRange(characters);
        // Mod pages and the monthly Wiki catalog run independently; merge any page already on screen.
        MergeOnlineCharacterOptionsFromMods(_services.Online.Page.Mods);
        if (!string.IsNullOrWhiteSpace(_services.Online.Page.CharacterFilter)
            && !_services.Online.Page.CharacterOptions.Any(character => string.Equals(
                character.Key,
                _services.Online.Page.CharacterFilter,
                StringComparison.OrdinalIgnoreCase)))
        {
            _services.Online.Page.CharacterFilter = string.Empty;
            _services.Online.Page.CurrentPage = 1;
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
        OnlineCharacterOption? option = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
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
            ?? EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character =>
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
        OnlineCharacterOption? option = _services.Online.Page.CharacterOptions.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(mod.CategoryId) && string.Equals(item.CategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(OnlineCharacterCatalogMatcher.NormalizeLookup(item.Key), normalized, StringComparison.OrdinalIgnoreCase));
        EndfieldCharacterInfo? endfield = FindEndfieldCharacter(mod.CharacterName)
            ?? EndfieldCharacterCatalog.BuiltIn.FirstOrDefault(character => !string.IsNullOrWhiteSpace(mod.CategoryId)
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
        // Issue #129: weapons, UI, other… go to a folder named after the category, planned and created like a character's.
        if (GetOnlineCategoryFolderName(mod) is { } category)
        {
            return CategoryFolderPlanner.Plan(repositoryPath, Directory.GetDirectories(repositoryPath),
                Directory.GetFiles(repositoryPath).Select(Path.GetFileName).OfType<string>().ToArray(), category);
        }
        (string? chinese, string? english) = GetCharacterFolderNames(mod);
        return CharacterFolderPlanner.Plan(repositoryPath,
            Directory.GetDirectories(repositoryPath),
            Directory.GetFiles(repositoryPath).Select(Path.GetFileName).OfType<string>().ToArray(),
            aliases, mod.CharacterName, chinese, english, IsChineseUi);
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
