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
            if (_services.OnlineDownloads.CurrentOnlineCategories() is { CharacterTop: { } characterTop } index && index.CharacterTopId != _services.OnlineDownloads.OnlineRepositoryCategoryId())
                OnlineCharacterComboBox.Items.Add(new ComboBoxItem { Content = T("OnlineCategories.AllOfTop", characterTop.Name), Tag = AllCharacterTopKey });

            foreach (OnlineCharacterOption character in visibleCharacters)
            {
                OnlineCharacterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = _services.OnlineDownloads.GetOnlineCharacterDisplayName(character),
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
            .ThenBy(character => _services.OnlineDownloads.GetOnlineCharacterDisplayName(character), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal void RefreshOnlineCharacterAvatarStrip()
    {
        EnsureOnlineCharacterOptionsForRepository(_services.Repositories.Selected);
        IReadOnlyList<OnlineCharacterOption> visibleCharacters = GetVisibleOnlineCharacterOptions();
        string characterSignature = string.Join(";", visibleCharacters.Select(item => $"{item.Key}:{item.DisplayNameZh}:{item.DisplayNameEn}:{item.CategoryId}:{item.AvatarUrl}:{item.WikiOrder}"));
        OnlineCategory? allTop = _services.OnlineDownloads.CurrentOnlineCategories() is { CharacterTop: { } top } categories && categories.CharacterTopId != _services.OnlineDownloads.OnlineRepositoryCategoryId() ? top : null;
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
                _services.OnlineDownloads.GetOnlineCharacterDisplayName(character),
                character.AvatarUrl,
                characterModCount,
                avatarRequestVersion,
                avatarCancellationToken));
        }

        string currentRole = string.IsNullOrWhiteSpace(_services.Online.Page.CharacterFilter)
            ? T("OnlineCharacters.All")
            : _services.OnlineDownloads.GetOnlineCharacterDisplayName(_services.Online.Page.CharacterFilter);
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
            : character == AllCharacterTopKey ? T("OnlineCategories.AllOfTopHint", _services.OnlineDownloads.CurrentOnlineCategories()?.CharacterTop?.Name ?? label) : tooltip);
        AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(character) ? T("OnlineCharacters.AllCharacters") : label);
        button.Click += async (_, _) => await _host.RunUiActionAsync("SelectOnlineCharacterAsync", () => SelectOnlineCharacterAsync(character));
        return character == AllCharacterTopKey ? button : WrapCharacterButtonWithFavorite(button, character);
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

    // The list itself is in OnlineCharacterOptionsService (stage J finish, step 5b); the page redraws its strip.
    internal void EnsureOnlineCharacterOptionsForRepository(WorkspaceRepository? repository)
    {
        if (_services.OnlineCharacters.Ensure(repository)) _onlineCharacterStripSignature = null;
    }

    private void MergeOnlineCharacterOptionsFromMods(IEnumerable<OnlineModCard> mods)
    {
        if (_services.OnlineCharacters.MergeFromMods(mods)) _onlineCharacterStripSignature = null;
    }

    private void OnOnlineCharacterCatalogApplied(object? sender, bool filterCleared)
    {
        if (filterCleared) _lastLoadedOnlineConfigKey = null;
        _onlineCharacterStripSignature = null;
        PopulateOnlineCharacterOptions();
        RefreshOnlineCharacterAvatarStrip();
        RefreshOnlinePaneV2();
    }
}
