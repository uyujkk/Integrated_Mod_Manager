using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Endfield;
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
                Content = L("全部角色", "All characters"),
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
                .FirstOrDefault(item => string.Equals(item.Tag as string ?? string.Empty, _onlineCharacterFilter, StringComparison.OrdinalIgnoreCase));

            OnlineCharacterComboBox.SelectedItem = selectedItem ?? OnlineCharacterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isApplyingOnlineCharacterSelection = false;
        }
    }

    private IReadOnlyList<OnlineCharacterOption> GetVisibleOnlineCharacterOptions()
    {
        List<OnlineCharacterOption> wikiCharacters = _onlineCharacterOptions
            .Where(character => character.IsWikiCharacter)
            .ToList();
        IEnumerable<OnlineCharacterOption> source = wikiCharacters.Count > 0 ? wikiCharacters : _onlineCharacterOptions;
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
            _onlineCharacterOptions.Clear();
            _onlineMods.Clear();
            _onlineCharacterFilter = string.Empty;
            _onlineTotalCount = 0;
            _onlineTotalPages = 1;
            _onlineCharacterStripSignature = null;
        }

        if (repository is null)
        {
            return;
        }

        if (IsEndfieldRepository(repository))
        {
            if (_onlineCharacterOptions.Count == 0)
            {
                _onlineCharacterOptions.AddRange(EndfieldCharacters.Select((character, index) => new OnlineCharacterOption
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

        MergeOnlineCharacterOptionsFromMods(_onlineMods);
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

            OnlineCharacterOption? existing = _onlineCharacterOptions.FirstOrDefault(item =>
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
                var rankedMatches = _onlineCharacterOptions
                    .Where(item => item.IsWikiCharacter)
                    .Select(item => new
                    {
                        Character = item,
                        Score = CalculateCharacterMatchScore(item, modCharacter)
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
                _onlineCharacterOptions.Add(new OnlineCharacterOption
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
            SortOnlineCharacterOptionsNewestFirst(_onlineCharacterOptions);
            _onlineCharacterStripSignature = null;
            if (!string.IsNullOrWhiteSpace(_onlineCharacterCatalogKey))
            {
                _ = WriteOnlineCharacterCatalogCacheAsync(
                    _onlineCharacterCatalogKey,
                    _onlineCharacterOptions,
                    preserveWikiCheckedAt: true);
            }
        }
    }

    private static void SortOnlineCharacterOptionsNewestFirst(List<OnlineCharacterOption> characters)
    {
        characters.Sort((left, right) =>
        {
            int wikiOrderComparison = left.WikiOrder.CompareTo(right.WikiOrder);
            if (wikiOrderComparison != 0)
            {
                return wikiOrderComparison;
            }

            int dateComparison = right.LatestModUpdatedAt.CompareTo(left.LatestModUpdatedAt);
            if (dateComparison != 0)
            {
                return dateComparison;
            }

            return StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayNameEn, right.DisplayNameEn);
        });
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
        string signature = $"{_currentLanguage}|{_isDarkTheme}|{_isLoadingOnlineMods}|{_onlineCharacterFilter}|{_onlineTotalCount}|horizontal={_useHorizontalOnlineCharacterRail}|{characterSignature}";
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
            L("全部", "All"),
            null,
            string.IsNullOrWhiteSpace(_onlineCharacterFilter) ? _onlineTotalCount : null,
            avatarRequestVersion,
            avatarCancellationToken));

        foreach (OnlineCharacterOption character in visibleCharacters)
        {
            int? characterModCount = string.Equals(_onlineCharacterFilter, character.Key, StringComparison.OrdinalIgnoreCase)
                ? _onlineTotalCount
                : null;
            OnlineCharacterAvatarPanel.Children.Add(CreateOnlineCharacterButton(
                character.Key,
                GetOnlineCharacterDisplayName(character),
                character.AvatarUrl,
                characterModCount,
                avatarRequestVersion,
                avatarCancellationToken));
        }

        string currentRole = string.IsNullOrWhiteSpace(_onlineCharacterFilter)
            ? L("全部", "All")
            : GetOnlineCharacterDisplayName(_onlineCharacterFilter);
        OnlineCharacterRailHintTextBlock.Text = L(
            $"{visibleCharacters.Count} 个分类 · 当前：{currentRole}",
            $"{visibleCharacters.Count} categories · Current: {currentRole}");
    }

    private FrameworkElement CreateOnlineCharacterButton(
        string character,
        string label,
        string? avatarUrl,
        int? modCount,
        int avatarRequestVersion,
        CancellationToken avatarCancellationToken)
    {
        bool isSelected = string.Equals(character, _onlineCharacterFilter, StringComparison.OrdinalIgnoreCase);
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
                    Text = L($"{modCount.Value} 个", $"{modCount.Value} mods"),
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
                    Text = L($"{modCount.Value} 个 Mod", $"{modCount.Value} mods"),
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
        OnlineCharacterOption? characterOption = _onlineCharacterOptions.FirstOrDefault(item =>
            string.Equals(item.Key, character, StringComparison.OrdinalIgnoreCase));
        string tooltip = characterOption is null
            ? label
            : $"{characterOption.DisplayNameZh} / {characterOption.DisplayNameEn}";
        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(character) ? L("显示全部角色", "Show all characters") : tooltip);
        AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(character) ? L("全部角色", "All characters") : label);
        button.Click += async (_, _) => await RunUiActionAsync("SelectOnlineCharacterAsync", () => SelectOnlineCharacterAsync(character));
        return WrapCharacterButtonWithFavorite(button, character);
    }

    private string GetOnlineCharacterDisplayName(EndfieldCharacterInfo character)
    {
        return _currentLanguage == AppLanguage.ZhCn ? character.ChineseName : character.EnglishName;
    }

    private string GetOnlineCharacterDisplayName(string characterName)
    {
        OnlineCharacterOption? option = _onlineCharacterOptions.FirstOrDefault(item =>
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

        OnlineCharacterOption? character = _onlineCharacterOptions.FirstOrDefault(option =>
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
        return CalculateCharacterMatchScore(character, candidate) >= 0.82;
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
                character.AllNames.Any(alias => ContainsCharacterAlias(title, alias)));
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

        string normalized = NormalizeCharacterLookup(name);
        return EndfieldCharacters.FirstOrDefault(character =>
            character.AllNames.Any(alias => string.Equals(
                NormalizeCharacterLookup(alias),
                normalized,
                StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizeCharacterLookup(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"[^\p{L}\p{N}]", string.Empty)
            .ToLowerInvariant();
    }

    private static bool ContainsCharacterAlias(string? text, string alias)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        if (alias.Any(character => character > 127))
        {
            return text.Contains(alias, StringComparison.OrdinalIgnoreCase);
        }

        string pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias).Replace("\\ ", @"\s*")}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        if (string.Equals(character, _onlineCharacterFilter, StringComparison.OrdinalIgnoreCase))
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
        _onlineCharacterFilter = character;
        _onlineCurrentPage = 1;
        _onlineTotalCount = 0;
        _onlineTotalPages = 1;
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

    private async Task<OnlineCharacterCatalogCacheSnapshot?> TryReadOnlineCharacterCatalogCacheAsync(string catalogKey)
    {
        CachedValue<OnlineCharacterCatalogCacheEntry>? sqliteEntry = _appDataStore.TryReadCache<OnlineCharacterCatalogCacheEntry>(
            "character-catalog",
            catalogKey,
            TimeSpan.FromDays(30),
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
                    DateTimeOffset.UtcNow - checkedAt <= TimeSpan.FromDays(30))
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
                catch
                {
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
        catch
        {
        }
    }

    private WikiCatalogDescriptor? GetWikiCatalogDescriptor(WorkspaceRepository repository)
    {
        string wikiUrl = repository.WikiUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(wikiUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string path = uri.AbsolutePath;
        if (path.Contains("/zzz/wiki/", StringComparison.OrdinalIgnoreCase))
        {
            return new WikiCatalogDescriptor(
                "zzz_wiki",
                43,
                "https://act-api-takumi-static.mihoyo.com/common/blackboard/zzz_wiki/v1/home/content/list",
                "https://baike.mihoyo.com/zzz/wiki/content/{0}/detail",
                "zzz",
                "8");
        }

        if (path.Contains("/ys/obc/", StringComparison.OrdinalIgnoreCase))
        {
            return new WikiCatalogDescriptor(
                "ys_obc",
                25,
                "https://act-api-takumi-static.mihoyo.com/common/blackboard/ys_obc/v1/home/content/list",
                "https://baike.mihoyo.com/ys/obc/content/{0}/detail?bbs_presentation_style=no_header&visit_device=pc",
                "genshin",
                "2");
        }

        if (path.Contains("/sr/wiki/", StringComparison.OrdinalIgnoreCase))
        {
            return new WikiCatalogDescriptor(
                "sr_wiki",
                18,
                "https://act-api-takumi-static.mihoyo.com/common/blackboard/sr_wiki/v1/home/content/list",
                "https://bbs.mihoyo.com/sr/wiki/content/{0}/detail?bbs_presentation_style=no_header",
                "hsr",
                "104");
        }

        return null;
    }

    private async Task<List<OnlineCharacterOption>> FetchWikiCharacterCatalogAsync(WorkspaceRepository repository)
    {
        if (IsEndfieldRepository(repository))
        {
            return await FetchEndfieldCharacterCatalogAsync(repository);
        }

        WikiCatalogDescriptor? descriptor = GetWikiCatalogDescriptor(repository);
        if (descriptor is null)
        {
            return [];
        }

        string requestUrl = $"{descriptor.ApiUrl}?app_sn={Uri.EscapeDataString(descriptor.AppSn)}&channel_id={descriptor.ChannelId}&lang=zh-cn";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out JsonElement data)
            || !data.TryGetProperty("list", out JsonElement channels)
            || channels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        JsonElement characterItems = default;
        foreach (JsonElement channel in channels.EnumerateArray())
        {
            if (TryGetInt32Property(channel, "id") == descriptor.ChannelId
                && channel.TryGetProperty("list", out JsonElement items)
                && items.ValueKind == JsonValueKind.Array)
            {
                characterItems = items;
                break;
            }
        }

        if (characterItems.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var records = new List<WikiCharacterRecord>();
        var knownTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in characterItems.EnumerateArray())
        {
            string title = TryGetStringProperty(item, "title")?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title) || !knownTitles.Add(NormalizeCharacterLookup(title)))
            {
                continue;
            }

            int contentId = TryGetInt32Property(item, "content_id");
            records.Add(new WikiCharacterRecord(
                title,
                TryGetStringProperty(item, "alias_name")?.Trim() ?? string.Empty,
                TryGetStringProperty(item, "icon")?.Trim() ?? string.Empty,
                contentId > 0 ? string.Format(CultureInfo.InvariantCulture, descriptor.ContentUrlTemplate, contentId) : repository.WikiUrl));
        }

        var characters = new List<OnlineCharacterOption>(records.Count);
        for (int index = 0; index < records.Count; index++)
        {
            WikiCharacterRecord record = records[index];
            characters.Add(new OnlineCharacterOption
            {
                Key = record.Title,
                DisplayNameZh = record.Title,
                DisplayNameEn = record.Title,
                AvatarUrl = record.AvatarUrl,
                WikiUrl = record.WikiUrl,
                Aliases = ParseWikiCharacterAliases(record.Title, record.AliasName),
                IsWikiCharacter = true,
                WikiOrder = index
            });
        }

        return characters;
    }

    private async Task<List<OnlineCharacterOption>> FetchEndfieldCharacterCatalogAsync(WorkspaceRepository repository)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(EndfieldOfficialOperatorCatalogUrl);
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync();
        IReadOnlyList<EndfieldOperatorCatalogEntry> officialOperators = EndfieldOperatorCatalogParser.Parse(html);
        if (officialOperators.Count == 0)
        {
            return [];
        }

        var characters = new List<OnlineCharacterOption>(officialOperators.Count);
        int endministratorVariant = 0;
        foreach (EndfieldOperatorCatalogEntry officialOperator in officialOperators)
        {
            string englishName = officialOperator.EnglishName;
            string chineseName = officialOperator.ChineseName;
            EndfieldCharacterInfo? fallbackCharacter;
            if (string.Equals(englishName, "Endministrator", StringComparison.OrdinalIgnoreCase))
            {
                bool isMaleVariant = endministratorVariant++ == 0;
                englishName = isMaleVariant ? "Endministrator (M)" : "Endministrator (F)";
                chineseName = isMaleVariant ? "管理员 (男)" : "管理员 (女)";
                fallbackCharacter = EndfieldCharacters.FirstOrDefault(character =>
                    string.Equals(character.EnglishName, englishName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                fallbackCharacter = EndfieldCharacters.FirstOrDefault(character =>
                    character.AllNames.Any(name =>
                        string.Equals(name, englishName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, chineseName, StringComparison.OrdinalIgnoreCase)));
            }

            List<string> aliases = new[]
                {
                    englishName,
                    chineseName,
                    officialOperator.EnglishName,
                    officialOperator.ChineseName
                }
                .Concat(fallbackCharacter?.AllNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            characters.Add(new OnlineCharacterOption
            {
                Key = englishName,
                DisplayNameZh = chineseName,
                DisplayNameEn = englishName,
                CategoryId = fallbackCharacter?.GameBananaCategoryId ?? string.Empty,
                AvatarUrl = string.IsNullOrWhiteSpace(officialOperator.AvatarUrl)
                    ? fallbackCharacter?.AvatarUrl
                    : officialOperator.AvatarUrl,
                WikiUrl = string.IsNullOrWhiteSpace(repository.WikiUrl)
                    ? EndfieldOfficialWikiUrl
                    : repository.WikiUrl,
                Aliases = aliases,
                IsWikiCharacter = true,
                WikiOrder = Math.Max(0, officialOperator.Order - 1)
            });
        }

        return characters;
    }

    private async Task<List<OnlineCharacterOption>> FetchGameBananaCharacterCatalogAsync(string categoryId)
    {
        string requestUrl = $"https://gamebanana.com/apiv11/Mod/Categories?_idCategoryRow={Uri.EscapeDataString(categoryId)}&_sSort=a_to_z&_bShowEmpty=true";
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        bool hasNestedCategories = document.RootElement
            .EnumerateArray()
            .Any(category => TryGetInt32Property(category, "_nCategoryCount") > 0);
        var characters = new List<OnlineCharacterOption>();

        if (!hasNestedCategories)
        {
            foreach (JsonElement category in document.RootElement.EnumerateArray())
            {
                AddGameBananaCharacterCategory(characters, category);
            }
        }
        else
        {
            foreach (JsonElement category in document.RootElement.EnumerateArray())
            {
                int nestedCount = TryGetInt32Property(category, "_nCategoryCount");
                int nestedCategoryId = TryGetInt32Property(category, "_idRow");
                if (nestedCount <= 0 || nestedCategoryId <= 0)
                {
                    continue;
                }

                string nestedUrl = $"https://gamebanana.com/apiv11/ModCategory/{nestedCategoryId}/SubCategories";
                using HttpResponseMessage nestedResponse = await _httpClient.GetAsync(nestedUrl);
                nestedResponse.EnsureSuccessStatusCode();
                string nestedJson = await nestedResponse.Content.ReadAsStringAsync();
                using JsonDocument nestedDocument = JsonDocument.Parse(nestedJson);
                if (nestedDocument.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement nestedCategory in nestedDocument.RootElement.EnumerateArray())
                {
                    AddGameBananaCharacterCategory(characters, nestedCategory);
                }
            }
        }

        return characters
            .Where(character => !string.IsNullOrWhiteSpace(character.CategoryId))
            .GroupBy(character => character.CategoryId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddGameBananaCharacterCategory(
        List<OnlineCharacterOption> characters,
        JsonElement category)
    {
        string name = TryGetStringProperty(category, "_sName")?.Trim() ?? string.Empty;
        string categoryUrl = TryGetStringProperty(category, "_sUrl")?.Trim() ?? string.Empty;
        int numericCategoryId = TryGetInt32Property(category, "_idRow");
        string resolvedCategoryId = numericCategoryId > 0
            ? numericCategoryId.ToString(CultureInfo.InvariantCulture)
            : ExtractTrailingNumericId(categoryUrl);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(resolvedCategoryId))
        {
            return;
        }

        characters.Add(new OnlineCharacterOption
        {
            Key = name,
            DisplayNameZh = name,
            DisplayNameEn = name,
            CategoryId = resolvedCategoryId,
            AvatarUrl = TryGetStringProperty(category, "_sIconUrl")?.Trim() ?? string.Empty,
            Aliases = [name]
        });
    }

    private static List<string> ParseWikiCharacterAliases(string title, string aliasName)
    {
        return new[] { title, aliasName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => Regex.Split(value, @"[,，、;/；|（）()]+"))
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
            List<HoYoWikiLocalizedCharacter> chineseCharacters = await FetchHoYoWikiCharacterNamesAsync(
                descriptor,
                "zh-cn");
            List<HoYoWikiLocalizedCharacter> englishCharacters = await FetchHoYoWikiCharacterNamesAsync(
                descriptor,
                "en-us");

            Dictionary<string, string> englishByEntryId = englishCharacters
                .Where(item => !string.IsNullOrWhiteSpace(item.EntryPageId)
                    && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.EntryPageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
            var englishByChineseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (HoYoWikiLocalizedCharacter chineseCharacter in chineseCharacters)
            {
                if (!englishByEntryId.TryGetValue(chineseCharacter.EntryPageId, out string? englishName))
                {
                    continue;
                }

                string normalizedChineseName = NormalizeCharacterLookup(chineseCharacter.Name);
                if (!string.IsNullOrWhiteSpace(normalizedChineseName))
                {
                    englishByChineseName[normalizedChineseName] = englishName;
                }
            }

            foreach (OnlineCharacterOption character in characters)
            {
                string? officialEnglishName = new[] { character.DisplayNameZh, character.Key }
                    .Concat(character.Aliases)
                    .Select(NormalizeCharacterLookup)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => englishByChineseName.TryGetValue(name, out string? englishName)
                        ? englishName
                        : null)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
                if (string.IsNullOrWhiteSpace(officialEnglishName))
                {
                    continue;
                }

                character.DisplayNameEn = officialEnglishName.Trim();
                if (!character.Aliases.Contains(character.DisplayNameEn, StringComparer.OrdinalIgnoreCase))
                {
                    character.Aliases.Add(character.DisplayNameEn);
                }
            }
        }
        catch
        {
            // The existing Chinese catalog and translation fallback remain usable when HoYoWiki is unavailable.
        }
    }

    private async Task<List<HoYoWikiLocalizedCharacter>> FetchHoYoWikiCharacterNamesAsync(
        WikiCatalogDescriptor descriptor,
        string language)
    {
        const int pageSize = 50;
        const int maxPages = 10;
        var characters = new List<HoYoWikiLocalizedCharacter>();
        var knownEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int page = 1; page <= maxPages; page++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{HoYoWikiApiBaseUrl}/get_entry_page_list");
            request.Headers.TryAddWithoutValidation("x-rpc-language", language);
            request.Headers.TryAddWithoutValidation("x-rpc-wiki_app", descriptor.HoYoWikiApp);
            request.Headers.Referrer = new Uri("https://wiki.hoyolab.com/");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    filters = Array.Empty<object>(),
                    menu_id = descriptor.HoYoWikiCharacterMenuId,
                    page_num = page,
                    page_size = pageSize,
                    use_es = true
                }),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out JsonElement data)
                || !data.TryGetProperty("list", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            int returnedCount = 0;
            foreach (JsonElement item in list.EnumerateArray())
            {
                returnedCount++;
                string entryPageId = TryGetStringProperty(item, "entry_page_id")?.Trim() ?? string.Empty;
                string name = TryGetStringProperty(item, "name")?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(entryPageId)
                    && !string.IsNullOrWhiteSpace(name)
                    && knownEntryIds.Add(entryPageId))
                {
                    characters.Add(new HoYoWikiLocalizedCharacter(entryPageId, name));
                }
            }

            int totalCount = 0;
            if (data.TryGetProperty("total", out JsonElement total))
            {
                if (total.ValueKind == JsonValueKind.Number)
                {
                    total.TryGetInt32(out totalCount);
                }
                else if (total.ValueKind == JsonValueKind.String)
                {
                    int.TryParse(total.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out totalCount);
                }
            }

            if (returnedCount == 0
                || (totalCount > 0 && characters.Count >= totalCount)
                || (totalCount == 0 && returnedCount < pageSize))
            {
                break;
            }
        }

        return characters;
    }

    private async Task TranslateWikiCharacterOptionsAsync(List<OnlineCharacterOption> characters)
    {
        List<OnlineCharacterOption> unresolvedCharacters = characters
            .Where(character => string.IsNullOrWhiteSpace(character.DisplayNameEn)
                || string.Equals(
                    NormalizeCharacterLookup(character.DisplayNameEn),
                    NormalizeCharacterLookup(character.DisplayNameZh),
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

    private static List<OnlineCharacterOption> MergeWikiAndGameBananaCharacters(
        List<OnlineCharacterOption> wikiCharacters,
        List<OnlineCharacterOption> gameBananaCharacters,
        IReadOnlyDictionary<string, string> manualMappings)
    {
        if (wikiCharacters.Count == 0)
        {
            return gameBananaCharacters;
        }

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OnlineCharacterOption wikiCharacter in wikiCharacters)
        {
            string mappingKey = GetCharacterMappingKey(wikiCharacter);
            if (manualMappings.TryGetValue(mappingKey, out string? manualCategoryId)
                && !string.IsNullOrWhiteSpace(manualCategoryId))
            {
                OnlineCharacterOption? manualMatch = gameBananaCharacters.FirstOrDefault(candidate =>
                    string.Equals(candidate.CategoryId, manualCategoryId, StringComparison.OrdinalIgnoreCase));
                ApplyCharacterCategoryMatch(wikiCharacter, manualMatch, manualCategoryId);
                if (manualMatch is not null)
                {
                    usedKeys.Add(manualMatch.Key);
                }
                continue;
            }

            OnlineCharacterOption? bestMatch = null;
            double bestScore = 0;
            double secondBestScore = 0;
            foreach (OnlineCharacterOption candidate in gameBananaCharacters)
            {
                if (usedKeys.Contains(candidate.Key))
                {
                    continue;
                }

                double score = CalculateCharacterMatchScore(wikiCharacter, candidate);
                if (score > bestScore)
                {
                    secondBestScore = bestScore;
                    bestScore = score;
                    bestMatch = candidate;
                }
                else if (score > secondBestScore)
                {
                    secondBestScore = score;
                }
            }

            bool isConfidentMatch = bestScore >= 0.74
                && (bestScore >= 0.94 || bestScore - secondBestScore >= 0.08);
            if (bestMatch is null || !isConfidentMatch)
            {
                continue;
            }

            usedKeys.Add(bestMatch.Key);
            ApplyCharacterCategoryMatch(wikiCharacter, bestMatch, bestMatch.CategoryId);
        }

        int fallbackOrder = wikiCharacters.Count;
        foreach (OnlineCharacterOption character in gameBananaCharacters)
        {
            if (usedKeys.Add(character.Key))
            {
                character.WikiOrder = fallbackOrder++;
                wikiCharacters.Add(character);
            }
        }

        return wikiCharacters;
    }

    private static void ApplyCharacterCategoryMatch(
        OnlineCharacterOption wikiCharacter,
        OnlineCharacterOption? gameBananaCharacter,
        string categoryId)
    {
        wikiCharacter.CategoryId = categoryId.Trim();
        if (gameBananaCharacter is null)
        {
            return;
        }

        foreach (string alias in new[]
                 {
                     gameBananaCharacter.Key,
                     gameBananaCharacter.DisplayNameZh,
                     gameBananaCharacter.DisplayNameEn
                 }.Concat(gameBananaCharacter.Aliases))
        {
            if (!string.IsNullOrWhiteSpace(alias)
                && !wikiCharacter.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                wikiCharacter.Aliases.Add(alias);
            }
        }

        wikiCharacter.LatestModUpdatedAt = gameBananaCharacter.LatestModUpdatedAt;
    }

    private static string GetCharacterMappingKey(OnlineCharacterOption character)
    {
        return !string.IsNullOrWhiteSpace(character.WikiUrl)
            ? character.WikiUrl.Trim().ToLowerInvariant()
            : NormalizeCharacterLookup(character.DisplayNameZh);
    }

    private static double CalculateCharacterMatchScore(
        OnlineCharacterOption wikiCharacter,
        OnlineCharacterOption gameBananaCharacter)
    {
        IEnumerable<string> wikiNames = new[]
            {
                wikiCharacter.DisplayNameZh,
                wikiCharacter.DisplayNameEn,
                wikiCharacter.Key
            }
            .Concat(wikiCharacter.Aliases);
        IEnumerable<string> gameBananaNames = new[]
            {
                gameBananaCharacter.DisplayNameZh,
                gameBananaCharacter.DisplayNameEn,
                gameBananaCharacter.Key
            }
            .Concat(gameBananaCharacter.Aliases);

        double bestScore = 0;
        foreach (string wikiName in wikiNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string gameBananaName in gameBananaNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bestScore = Math.Max(bestScore, CalculateCharacterNameSimilarity(wikiName, gameBananaName));
            }
        }

        return bestScore;
    }

    private static double CalculateCharacterNameSimilarity(string left, string right)
    {
        string normalizedLeft = NormalizeCharacterLookup(left);
        string normalizedRight = NormalizeCharacterLookup(right);
        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
        {
            return 0;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return 1;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal)
            || normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
        {
            double containmentRatio = (double)Math.Min(normalizedLeft.Length, normalizedRight.Length)
                / Math.Max(normalizedLeft.Length, normalizedRight.Length);
            return 0.78 + containmentRatio * 0.2;
        }

        int[] previous = Enumerable.Range(0, normalizedRight.Length + 1).ToArray();
        int[] current = new int[normalizedRight.Length + 1];
        for (int leftIndex = 1; leftIndex <= normalizedLeft.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= normalizedRight.Length; rightIndex++)
            {
                int substitutionCost = normalizedLeft[leftIndex - 1] == normalizedRight[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return 1d - (double)previous[normalizedRight.Length] / Math.Max(normalizedLeft.Length, normalizedRight.Length);
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
                        ApplyManualCharacterMappings(repository, cachedCatalog.Characters);
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
                ApplyManualCharacterMappings(repository, wikiCharacters);
                ApplyOnlineCharacterCatalog(catalogKey, wikiCharacters);
                await ApplyOfficialHoYoWikiEnglishNamesAsync(repository, wikiCharacters);
                await TranslateWikiCharacterOptionsAsync(wikiCharacters);

                List<OnlineCharacterOption> gameBananaCharacters = [];
                try
                {
                    gameBananaCharacters = await FetchGameBananaCharacterCatalogAsync(
                        GetEffectiveOnlineCategoryId(repository));
                }
                catch
                {
                    // Wiki names remain usable if the optional category mapping request fails.
                }

                List<OnlineCharacterOption> mergedCharacters = MergeWikiAndGameBananaCharacters(
                    wikiCharacters,
                    gameBananaCharacters,
                    repository.CharacterCategoryMappings);
                ApplyManualCharacterMappings(repository, mergedCharacters);
                await WriteOnlineCharacterCatalogCacheAsync(catalogKey, mergedCharacters);
                ApplyOnlineCharacterCatalog(catalogKey, mergedCharacters);
            }
        }
        catch
        {
            // The current page still supplies a usable partial character list.
        }
        finally
        {
            _loadingOnlineCharacterCatalogKeys.Remove(catalogKey);
        }
    }

    private static void ApplyManualCharacterMappings(
        WorkspaceRepository repository,
        IEnumerable<OnlineCharacterOption> characters)
    {
        foreach (OnlineCharacterOption character in characters.Where(character => character.IsWikiCharacter))
        {
            if (repository.CharacterCategoryMappings.TryGetValue(GetCharacterMappingKey(character), out string? categoryId)
                && !string.IsNullOrWhiteSpace(categoryId))
            {
                character.CategoryId = categoryId.Trim();
            }
        }
    }

    private void ApplyOnlineCharacterCatalog(string catalogKey, List<OnlineCharacterOption> characters)
    {
        if (!string.Equals(_onlineCharacterCatalogKey, catalogKey, StringComparison.Ordinal))
        {
            return;
        }

        SortOnlineCharacterOptionsNewestFirst(characters);
        _onlineCharacterOptions.Clear();
        _onlineCharacterOptions.AddRange(characters);
        // Mod pages and the monthly Wiki catalog run independently; merge any page already on screen.
        MergeOnlineCharacterOptionsFromMods(_onlineMods);
        if (!string.IsNullOrWhiteSpace(_onlineCharacterFilter)
            && !_onlineCharacterOptions.Any(character => string.Equals(
                character.Key,
                _onlineCharacterFilter,
                StringComparison.OrdinalIgnoreCase)))
        {
            _onlineCharacterFilter = string.Empty;
            _onlineCurrentPage = 1;
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

        string normalizedCharacterName = NormalizeCharacterLookup(mod.CharacterName);
        OnlineCharacterOption? option = _onlineCharacterOptions.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(mod.CategoryId)
                && string.Equals(item.CategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(NormalizeCharacterLookup(item.Key), normalizedCharacterName, StringComparison.OrdinalIgnoreCase)
            || item.Aliases.Any(alias => string.Equals(
                NormalizeCharacterLookup(alias),
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
        string normalized = NormalizeCharacterLookup(mod.CharacterName);
        OnlineCharacterOption? option = _onlineCharacterOptions.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(mod.CategoryId) && string.Equals(item.CategoryId, mod.CategoryId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(NormalizeCharacterLookup(item.Key), normalized, StringComparison.OrdinalIgnoreCase));
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
        CharacterFolderReason.Ambiguous => L($"有多个文件夹都可能属于“{character}”，请手动选择。", $"Several folders may belong to “{character}”; choose one."),
        CharacterFolderReason.UnknownCharacter => L("无法确定这个 Mod 属于哪个角色，请手动选择位置。", "The Mod's character is unknown; choose a destination."),
        CharacterFolderReason.MultipleCharacters => L($"“{character}”包含多个角色，请手动选择位置。", $"“{character}” covers several characters; choose a destination."),
        CharacterFolderReason.MixedNamingLanguage => L("仓库中同时有中文和英文命名的文件夹，无法确定新文件夹用哪种语言，请手动选择。", "The repository mixes Chinese and English folder names, so a new folder name cannot be chosen automatically."),
        CharacterFolderReason.NoNameInLanguage => L($"没有“{character}”在仓库命名语言中的名称，请手动选择。", $"No name for “{character}” in the repository's naming language; choose a destination."),
        CharacterFolderReason.InvalidName => L($"“{character}”不能直接用作文件夹名，请手动选择。", $"“{character}” cannot be used as a folder name; choose a destination."),
        CharacterFolderReason.NameConflict => L("仓库中已有同名文件，不能新建角色文件夹，请手动选择。", "A file with that name already exists; choose a destination."),
        _ => string.Empty
    };
}
