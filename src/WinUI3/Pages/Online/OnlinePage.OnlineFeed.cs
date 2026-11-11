using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    internal void InitializeOnlineControls()
    {
        OnlineSearchTextBox.PlaceholderText = T("OnlineFeed.SearchThisPageCharacterModOr");
        OnlineSortComboBox.Items.Clear();
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = T("OnlineFeed.SortByHotness"), Tag = OnlineSortMode.Hotness });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = T("OnlineFeed.SortByDownloads"), Tag = OnlineSortMode.Downloads });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = T("OnlineFeed.SortByLikes"), Tag = OnlineSortMode.Likes });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = T("OnlineFeed.SortByViews"), Tag = OnlineSortMode.Views });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = T("OnlineFeed.SortByUpdatedTime"), Tag = OnlineSortMode.Updated });
        OnlineSortComboBox.SelectedItem = OnlineSortComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is OnlineSortMode sortMode && sortMode == _services.Online.Page.SortMode)
            ?? OnlineSortComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        PopulateOnlineCharacterOptions();
    }

    internal void RefreshOnlinePaneV2()
    {
        EnsureOnlineBrowseSession();
        RefreshOnlineNavigationButtonLabels();
        WorkspaceRepository? repository = _services.Repositories.Selected;
        bool hasRepository = repository is not null;
        string effectiveSource = _services.OnlineSource.Source(repository);
        string effectiveGameName = _services.OnlineSource.GameName(repository);
        string effectiveCategoryId = _services.OnlineSource.CategoryId(repository);
        bool isSupportedSource = _services.OnlineSource.IsGameBanana(effectiveSource);
        bool canLoadOnlineMods = hasRepository && isSupportedSource && !string.IsNullOrWhiteSpace(effectiveCategoryId);
        string requestCategoryId = GetOnlineRequestCategoryId(repository);
        string configKey = hasRepository
            ? $"{repository!.Id}|{effectiveSource}|{requestCategoryId}|character={_services.Online.Page.CharacterFilter}|page={_services.Online.Page.CurrentPage}{OnlineAuthorKey}"
            : string.Empty;

        if (!string.IsNullOrEmpty(_lastLoadedOnlineConfigKey)
            && !string.Equals(_lastLoadedOnlineConfigKey, configKey, StringComparison.Ordinal)
            && !_isLoadingOnlineMods)
        {
            _services.Online.Page.Mods.Clear();
        }

        RefreshOnlineButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        if (_isLoadingOnlineMods && OnlineLoadingOverlay.Visibility != Visibility.Visible) ShowOnlineLoadingPlaceholders();
        OnlineLoadingOverlay.Visibility = _isLoadingOnlineMods ? Visibility.Visible : Visibility.Collapsed;
        OnlineLoadingTextBlock.Text = T("OnlineFeed.LoadingOnlineModsPleaseWait");
        OnlineStatusTextBlock.Text = _services.Online.Page.HasStatus
            ? T(_services.Online.Page.Status!)
            : T("OnlineFeed.ClickRefreshOnlineListToFetch");
        // The status trims in narrow windows; the tooltip keeps the whole text (also after a language change).
        ToolTipService.SetToolTip(OnlineStatusTextBlock, OnlineStatusTextBlock.Text);
        bool hasCacheStatus = _services.Online.Page.HasCacheStatus;
        OnlineCacheStatusBorder.Visibility = hasCacheStatus ? Visibility.Visible : Visibility.Collapsed;
        OnlineCacheStatusTextBlock.Text = hasCacheStatus
            ? T(_services.Online.Page.CacheStatus!)
            : string.Empty;
        List<OnlineModCard> filteredMods = canLoadOnlineMods ? GetFilteredOnlineMods() : [];
        List<OnlineModCard> visibleMods = GetVisibleOnlineMods(filteredMods);
        RefreshOnlineFilterSummary(visibleMods.Count, _services.Online.Page.Mods.Count);
        OnlinePaginationTextBlock.Text = canLoadOnlineMods
            ? T("OnlineFeed.PageModsTotal", _services.Online.Page.CurrentPage, _services.Online.Page.TotalPages, _services.Online.Page.TotalCount)
            : T("OnlineFeed.WaitingForOnlineConfig");
        OnlinePrevPageButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods && _services.Online.Page.HasPreviousPage;
        OnlineNextPageButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods && _services.Online.Page.HasNextPage;
        OnlineSearchTextBox.PlaceholderText = T("OnlineFeed.SearchThisPageCharacterModOr");
        OnlineSearchTextBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineCharacterComboBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineSortComboBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineCharacterRailTitleTextBlock.Text = T("OnlineFeed.BrowseByCharacter");
        RefreshOnlineCharacterAvatarStrip();
        UpdateOnlineCardLayoutControls();

        // Status-only refreshes must not reset either virtualized view's scroll position.
        bool resultsChanged = OnlineBrowseStateAdapter.Synchronize(_services.Online.Page.VisibleMods, visibleMods);
        RefreshOnlineCardPresentation();
        RefreshOnlineCardStates();
        ToolTipService.SetToolTip(OnlineSearchTextBox, T("OnlineFeed.SearchThisPageByCharacterMod"));
        OnlineEmptyResultsContentControl.Content = null;
        OnlineEmptyResultsContentControl.Visibility = Visibility.Collapsed;

        if (!hasRepository)
        {
            ShowOnlineEmptyResults(CreateOnlineEmptyState("\uE8B7",
                T("OnlineFeed.WaitingForRepository"),
                T("OnlineFeed.SelectARepositoryFromTheLeft2")));
            AnimateOnlineCardsEntrance();
            return;
        }

        if (!isSupportedSource)
        {
            ShowOnlineEmptyResults(CreateOnlineEmptyState("\uE774",
                T("OnlineFeed.SourceNotSupportedYet"),
                T("OnlineFeed.ThisOnlinePageCurrentlySupportsGameBanana") + "\n" + effectiveSource));
            AnimateOnlineCardsEntrance();
            return;
        }

        if (_services.Online.Page.Mods.Count == 0)
        {
            ShowOnlineEmptyResults(CreateOnlineEmptyState("\uE72C",
                T("OnlineFeed.ReadyToLoadRealData"),
                T("OnlineFeed.ThisPageNowUsesTheRepository")));
        }
        else
        {
            if (visibleMods.Count == 0)
            {
                // Nothing matches the search or filters: say so and offer to clear them.
                var clear = new Button { Content = T("OnlineLibrary.ClearFilters"), Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
                clear.Click += OnOnlineEmptyClearFiltersClicked;
                ShowOnlineEmptyResults(CreateOnlineEmptyState("\uE721",
                    T("OnlineFeed.NoMatchingSkinMods"),
                    T("OnlineFeed.ThisPageShowsSkinsEntriesClear"),
                    clear));
            }
        }

        if (_services.Online.Page.CardLayoutMode == OnlineCardLayoutMode.Grid)
        {
            DispatcherQueue.TryEnqueue(() => UpdateOnlineGridCardMetrics(
                GetOnlineGridAvailableWidth(OnlineVirtualizedGridView.ActualWidth)));
        }
        DispatcherQueue.TryEnqueue(UpdateOnlineWorkspaceWidthConstraint);
        if (resultsChanged || _animateOnlineCardsOnNextRefresh) AnimateOnlineCardsEntrance();
        QueueRestoreOnlineBrowsePosition();

        if (_host.IsOnlinePageShown
            && canLoadOnlineMods
            && configKey != _lastLoadedOnlineConfigKey
            && !string.Equals(configKey, _blockedOnlineAutoLoadConfigKey, StringComparison.Ordinal)
            && !_isLoadingOnlineMods)
        {
            _ = LoadOnlineModsAsync(forceReload: false);
        }
    }

    private void ShowOnlineEmptyResults(UIElement content)
    {
        OnlineEmptyResultsContentControl.Content = content;
        OnlineEmptyResultsContentControl.Visibility = Visibility.Visible;
    }

    private static bool IsAllowedSkinCategory(string? categoryName)
    {
        return !string.IsNullOrWhiteSpace(categoryName)
            && categoryName.Contains("Skins", StringComparison.OrdinalIgnoreCase);
    }

    private List<OnlineModCard> GetFilteredOnlineMods()
    {
        // Issue #129: only the character list keeps to skins and filters by character; other categories show everything.
        bool characterList = IsOnlineCharacterView() && !ShowsAllOfCharacterTop();
        IEnumerable<OnlineModCard> query = characterList
            ? _services.Online.Page.Mods.Where(mod => IsAllowedSkinCategory(mod.RootCategoryName))
            : _services.Online.Page.Mods;

        if (characterList && !string.IsNullOrWhiteSpace(_services.Online.Page.CharacterFilter))
        {
            query = query.Where(mod => DoesOnlineModMatchCharacter(
                _services.Online.Page.CharacterFilter,
                mod.CharacterName));
        }

        if (!string.IsNullOrWhiteSpace(_services.Online.Page.SearchText))
        {
            query = query.Where(mod => MatchesOnlineSearch(mod, _services.Online.Page.SearchText));
        }

        if (_services.Online.Page.AuthorWorkFilter == 1) query = query.Where(mod => FindInstalledOnlineMod(mod) is not null);
        else if (_services.Online.Page.AuthorWorkFilter == 2) query = query.Where(IsSavedOnlineMod);

        return OnlineBrowseOrder.Arrange(query, _services.Online.Page.SortMode).ToList();
    }

    private List<OnlineModCard> GetVisibleOnlineMods(List<OnlineModCard> filteredMods)
    {
        return filteredMods
            .ToList();
    }

    private string GetOnlineRequestCategoryId(WorkspaceRepository? repository)
    {
        EnsureOnlineCharacterOptionsForRepository(repository);
        if (OnlineScopeCategoryId() is { } scope) return scope;
        OnlineCharacterOption? selectedCharacter = _services.Online.Page.CharacterOptions.FirstOrDefault(character =>
            string.Equals(character.Key, _services.Online.Page.CharacterFilter, StringComparison.OrdinalIgnoreCase));
        return selectedCharacter is not null && !string.IsNullOrWhiteSpace(selectedCharacter.CategoryId)
            ? selectedCharacter.CategoryId
            : _services.OnlineSource.CategoryId(repository);
    }

    private void SetOnlineStatus(UiMessage status)
    {
        _services.Online.Page.SetStatus(status);
        OnlineStatusTextBlock.Text = T(status);
        ToolTipService.SetToolTip(OnlineStatusTextBlock, T(status));
    }

    private void SetOnlineCacheStatus(UiMessage? status)
    {
        _services.Online.Page.SetCacheStatus(status);
        OnlineCacheStatusBorder.Visibility = status is not null ? Visibility.Visible : Visibility.Collapsed;
        OnlineCacheStatusTextBlock.Text = status is not null ? T(status) : string.Empty;
        ToolTipService.SetToolTip(OnlineCacheStatusBorder, status is not null ? T(status) : null);
        // The cache pill's width changes what is left for the status.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateOnlineHeaderLayout);
    }

    internal void CancelOnlineModListRequest()
    {
        // Invalidate even when the next load exits before creating a new request.
        _onlineModRequestVersion++;
        _isLoadingOnlineMods = false;
        CancellationTokenSource? cancellation = _onlineModListCancellation;
        _onlineModListCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request already finished and released its token.
        }
    }

    private bool TryGetOnlineRateLimitDelay(out TimeSpan remaining)
    {
        return _onlineRateLimit.TryGetRemaining(out remaining);
    }

    private TimeSpan RegisterOnlineRateLimit(TimeSpan? serverRetryAfter)
    {
        return _onlineRateLimit.Register(serverRetryAfter);
    }

    private void ResetOnlineRateLimit()
    {
        _onlineRateLimit.Reset();
    }

    private static UiMessage FormatOnlineCacheAge(DateTimeOffset? cachedAtUtc, bool stale)
    {
        if (cachedAtUtc is null)
        {
            return M(stale ? "OnlineFeed.StaleCache" : "OnlineFeed.LocalCache");
        }

        TimeSpan age = DateTimeOffset.UtcNow - cachedAtUtc.Value;
        UiMessage value = age < TimeSpan.FromMinutes(1) ? M("OnlineFeed.JustNow")
            : age < TimeSpan.FromHours(1) ? M("OnlineFeed.MinutesAgo", Math.Max(1, (int)age.TotalMinutes))
            : age < TimeSpan.FromDays(1) ? M("OnlineFeed.HoursAgo", Math.Max(1, (int)age.TotalHours))
            : M("OnlineFeed.DaysAgo", Math.Max(1, (int)age.TotalDays));
        return M(stale ? "OnlineFeed.StaleCacheAge" : "OnlineFeed.CacheAge", value);
    }

    private void SetOnlineDownloadProgress(bool isVisible, double percent, string text, bool isIndeterminate = false)
    {
        OnlineDownloadProgressPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        OnlineDownloadProgressBar.IsIndeterminate = isVisible && isIndeterminate;
        OnlineDownloadProgressBar.Value = Math.Max(0, Math.Min(100, percent));
        OnlineDownloadProgressTextBlock.Text = text;
    }

    private async void OnRefreshOnlineModsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = _services.Repositories.Selected;
            if (repository is not null)
            {
                _ = _services.CharacterCatalog.EnsureAsync(repository, forceReload: true);
            }

            await LoadOnlineModsAsync(forceReload: true);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnRefreshOnlineModsClicked", ex);
        }
    }

    internal async Task LoadOnlineModsAsync(bool forceReload)
    {
        EnsureOnlineBrowseSession();
        _host.SaveShellConfig();
        CancelOnlineModListRequest();
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null)
        {
            return;
        }

        EnsureOnlineCharacterOptionsForRepository(repository);
        string source = _services.OnlineSource.Source(repository);
        string baseCategoryId = _services.OnlineSource.CategoryId(repository);
        string categoryId = GetOnlineRequestCategoryId(repository);
        bool useEndfieldMetadata = _services.OnlineSource.IsEndfield(repository);
        if (!_services.OnlineSource.IsGameBanana(source) || string.IsNullOrWhiteSpace(baseCategoryId))
        {
            SetOnlineStatus(M("OnlineFeed.NoOnlineSource"));
            RefreshOnlinePaneV2();
            return;
        }

        _ = EnsureOnlineCategoriesAsync();
        int authorId = _services.Online.Page.AuthorId;
        // Issue #129: likes, downloads and update time come sorted from the site, so the sort is part of the request.
        string? serverSort = OnlineBrowseOrder.ServerSort(_services.Online.Page.SortMode);
        string? scopeCategoryId = OnlineScopeCategoryId();
        _requestedOnlineScope = scopeCategoryId ?? string.Empty;
        string configKey = $"{repository.Id}|{source}|{categoryId}|character={_services.Online.Page.CharacterFilter}|page={_services.Online.Page.CurrentPage}{OnlineAuthorKey}"
            + $"|scope={scopeCategoryId}|sort={serverSort}";
        if (forceReload)
        {
            _blockedOnlineAutoLoadConfigKey = null;
        }
        else if (string.Equals(configKey, _blockedOnlineAutoLoadConfigKey, StringComparison.Ordinal))
        {
            RefreshOnlinePaneV2();
            return;
        }

        if (!forceReload && string.Equals(configKey, _lastLoadedOnlineConfigKey, StringComparison.Ordinal) && _services.Online.Page.Mods.Count > 0)
        {
            RefreshOnlinePaneV2();
            return;
        }

        var requestCancellation = new CancellationTokenSource();
        _onlineModListCancellation = requestCancellation;
        int requestVersion = ++_onlineModRequestVersion;
        int downloadEnrichmentVersion = ++_onlineDownloadEnrichmentVersion;

        _isLoadingOnlineMods = true;
        RefreshOnlineButtonTextBlock.Text = T("Common.Loading");
        RefreshOnlineButton.IsEnabled = false;
        string gameName = _services.OnlineSource.GameName(repository);
        string categoryLabel = string.IsNullOrWhiteSpace(gameName)
            ? categoryId
            : $"{gameName} / {categoryId}";
        SetOnlineStatus(M("OnlineFeed.LoadingCategory", categoryLabel));
        SetOnlineCacheStatus(M("OnlineFeed.Connecting"));
        RefreshOnlinePaneV2();
        using CancellationTokenSource timeoutCancellation = new(OnlineModListRequestTimeout);
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellation.Token,
            timeoutCancellation.Token);

        try
        {
            bool needsCharacterFallback = scopeCategoryId is null && !string.IsNullOrWhiteSpace(_services.Online.Page.CharacterFilter)
                && string.Equals(categoryId, baseCategoryId, StringComparison.OrdinalIgnoreCase);
            string cacheCategoryKey = (needsCharacterFallback
                ? $"category-v3|{baseCategoryId}-character-{_services.Online.Page.CharacterFilter}"
                : $"category-v3|{categoryId}") + (authorId > 0 ? $"|author={authorId}" : string.Empty)
                + (serverSort is null ? string.Empty : $"|sort={serverSort}");
            OnlineCategoryPageResult? cachedPage = forceReload
                ? null
                : await TryReadOnlineCategoryPageCacheAsync(cacheCategoryKey, _services.Online.Page.CurrentPage);
            bool loadedFromCache = cachedPage is not null;
            bool loadedDuringRateLimit = false;
            bool loadedAfterTimeout = false;
            OnlineCategoryPageResult pageResult;
            if (cachedPage is null && TryGetOnlineRateLimitDelay(out TimeSpan cooldownRemaining))
            {
                OnlineCategoryPageResult? stalePage = await TryReadOnlineCategoryPageCacheAsync(
                    cacheCategoryKey,
                    _services.Online.Page.CurrentPage,
                    allowExpired: true);
                if (stalePage is null)
                {
                    int seconds = Math.Max(1, (int)Math.Ceiling(cooldownRemaining.TotalSeconds));
                    _blockedOnlineAutoLoadConfigKey = configKey;
                    SetOnlineStatus(M("OnlineFeed.CooldownActive", seconds));
                    SetOnlineCacheStatus(M("OnlineFeed.Cooldown", seconds));
                    return;
                }

                pageResult = stalePage;
                loadedFromCache = true;
                loadedDuringRateLimit = true;
            }
            else
            {
                try
                {
                    pageResult = cachedPage
                        ?? (needsCharacterFallback && authorId == 0
                            ? await FetchGameBananaCharacterFallbackPageAsync(
                                baseCategoryId,
                                _services.Online.Page.CharacterFilter,
                                _services.Online.Page.CurrentPage,
                                useEndfieldMetadata,
                                cacheCategoryKey,
                                linkedCancellation.Token,
                                serverSort)
                            : await FetchGameBananaCategoryPageAsync(
                                categoryId,
                                _services.Online.Page.CurrentPage,
                                useEndfieldMetadata: useEndfieldMetadata,
                                cancellationToken: linkedCancellation.Token,
                                authorId: authorId,
                                serverSort: serverSort));
                    if (!loadedFromCache)
                    {
                        ResetOnlineRateLimit();
                    }
                }
                catch (OnlineRateLimitException ex)
                {
                    RegisterOnlineRateLimit(ex.RetryAfter);
                    OnlineCategoryPageResult? stalePage = await TryReadOnlineCategoryPageCacheAsync(
                        cacheCategoryKey,
                        _services.Online.Page.CurrentPage,
                        allowExpired: true);
                    if (stalePage is null)
                    {
                        throw;
                    }

                    pageResult = stalePage;
                    loadedFromCache = true;
                    loadedDuringRateLimit = true;
                }
                catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !requestCancellation.IsCancellationRequested)
                {
                    OnlineCategoryPageResult? stalePage = await TryReadOnlineCategoryPageCacheAsync(
                        cacheCategoryKey,
                        _services.Online.Page.CurrentPage,
                        allowExpired: true);
                    if (stalePage is null)
                    {
                        throw;
                    }

                    pageResult = stalePage;
                    loadedFromCache = true;
                    loadedAfterTimeout = true;
                }
            }
            // Download counts arrive before the page is shown (one batch request, ~300 ms), so the list
            // renders once, already sorted, instead of refreshing as counts trickle in.
            int countsFilled = 0;
            if (!loadedDuringRateLimit && !loadedAfterTimeout && pageResult.Mods.Any(mod => mod.Downloads <= 0))
            {
                using CancellationTokenSource countBudget = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation.Token);
                countBudget.CancelAfter(OnlineDownloadCountInlineBudget);
                try
                {
                    countsFilled = await FillOnlineDownloadCountsAsync(pageResult.Mods, countBudget.Token);
                }
                catch (OnlineRateLimitException ex)
                {
                    RegisterOnlineRateLimit(ex.RetryAfter);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or IOException
                    || (ex is OperationCanceledException && !requestCancellation.IsCancellationRequested))
                {
                    // Show the page now; the background pass retries the counts once.
                }
            }

            if (!loadedFromCache || countsFilled > 0)
            {
                await WriteOnlineCategoryPageCacheAsync(cacheCategoryKey, pageResult);
            }

            if (requestVersion != _onlineModRequestVersion || requestCancellation.IsCancellationRequested)
            {
                return;
            }

            // A remembered author/category may have fewer pages after entries are removed.
            if (_services.Online.Page.CurrentPage > Math.Max(1, pageResult.TotalPages))
            {
                _services.Online.Page.CurrentPage = Math.Max(1, pageResult.TotalPages);
                ResetOnlineBrowsePosition();
                await LoadOnlineModsAsync(forceReload: false);
                return;
            }

            _services.Online.Page.Mods.Clear();
            _services.Online.Page.Mods.AddRange(pageResult.Mods);
            _services.Online.Page.KnownCharacters.Clear();
            foreach (string character in _services.Online.Page.Mods
                         .Select(mod => mod.CharacterName)
                         .Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                _services.Online.Page.KnownCharacters.Add(character);
            }

            MergeOnlineCharacterOptionsFromMods(_services.Online.Page.Mods);
            PopulateOnlineCharacterOptions();
            _services.Online.Page.ApplyPageResult(pageResult.TotalCount, pageResult.TotalPages);

            if (pageResult.Mods.Count == 0)
            {
                _lastLoadedOnlineConfigKey = configKey;
                SetOnlineStatus(scopeCategoryId is null ? M("OnlineFeed.NoSkinMods") : M("OnlineCategories.NoModsInCategory"));
                return;
            }

            _lastLoadedOnlineConfigKey = configKey;
            _blockedOnlineAutoLoadConfigKey = null;
            HideOnlineLoadError();

            if (!loadedDuringRateLimit && pageResult.Mods.Any(mod => mod.Downloads <= 0))
            {
                _ = EnrichMissingGameBananaDownloadsAsync(
                    pageResult,
                    cacheCategoryKey,
                    configKey,
                    downloadEnrichmentVersion);
            }

            // A character scan already cached every page; otherwise warm the next page (also after a
            // cache hit, so paging forward stays instant). Never while throttled or after a timeout.
            if (!needsCharacterFallback && !loadedDuringRateLimit && !loadedAfterTimeout && _services.Online.Page.CurrentPage < _services.Online.Page.TotalPages)
            {
                _ = PrefetchNextOnlinePageAsync(categoryId, _services.Online.Page.CurrentPage + 1, useEndfieldMetadata, authorId, cacheCategoryKey, configKey, serverSort);
            }

            OnlineCharacterOption? selectedCharacter = _services.Online.Page.CharacterOptions.FirstOrDefault(character =>
                string.Equals(character.Key, _services.Online.Page.CharacterFilter, StringComparison.OrdinalIgnoreCase));
            string roleLabel = OnlineScopeCategoryName() is { } scopeName ? scopeName
                : selectedCharacter is null
                ? T("OnlineFeed.AllCategories")
                : IsChineseUi ? selectedCharacter.DisplayNameZh : selectedCharacter.DisplayNameEn;
            SetOnlineCacheStatus(loadedFromCache
                ? FormatOnlineCacheAge(pageResult.CachedAtUtc, loadedDuringRateLimit || loadedAfterTimeout)
                : M("OnlineFeed.Live"));
            SetOnlineStatus(
                loadedAfterTimeout ? M("OnlineFeed.TimeoutCachePage", roleLabel, _services.Online.Page.CurrentPage)
                : loadedDuringRateLimit ? M("OnlineFeed.RateLimitedCachePage", roleLabel, _services.Online.Page.CurrentPage)
                : M(loadedFromCache ? "OnlineFeed.LocalCachePage" : "OnlineFeed.UpdatedPage", roleLabel, _services.Online.Page.CurrentPage, _services.Online.Page.TotalCount));
            if (loadedAfterTimeout)
            {
                ShowAppNotification(T("OnlineFeed.TimeoutShowingCache"), InfoBarSeverity.Warning);
            }
            else if (loadedDuringRateLimit)
            {
                ShowAppNotification(T("OnlineFeed.RateLimitedShowingCache"), InfoBarSeverity.Warning);
            }
            else if (forceReload)
            {
                // Paging and character switches already show their result in the status chip;
                // only an explicit refresh gets a notification.
                ShowAppNotification(T("OnlineFeed.ListRefreshed"));
            }
        }
        catch (OnlineRateLimitException)
        {
            if (requestVersion != _onlineModRequestVersion || requestCancellation.IsCancellationRequested)
            {
                return;
            }
            _blockedOnlineAutoLoadConfigKey = configKey;
            TryGetOnlineRateLimitDelay(out TimeSpan remaining);
            int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            SetOnlineStatus(M("OnlineFeed.RateLimitedPaused", seconds));
            SetOnlineCacheStatus(M("OnlineFeed.Cooldown", seconds));
            ShowAppNotification(T("OnlineFeed.RefreshPaused"), InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            if (requestVersion != _onlineModRequestVersion)
            {
                return;
            }
            _blockedOnlineAutoLoadConfigKey = configKey;
            SetOnlineStatus(M("OnlineFeed.TimeoutPreserved"));
            SetOnlineCacheStatus(M("OnlineFeed.Stopped"));
            ShowAppNotification(T("OnlineFeed.TimedOut"), InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            if (requestVersion != _onlineModRequestVersion || requestCancellation.IsCancellationRequested)
            {
                return;
            }
            _blockedOnlineAutoLoadConfigKey = configKey;
            SetOnlineStatus(M("OnlineFeed.LoadFailed"));
            SetOnlineCacheStatus(M("OnlineFeed.ConnectionFailed"));
            ShowOnlineLoadError(T("OnlineFeed.FailedToLoadTheGameBananaList") + ex.Message);
        }
        finally
        {
            if (requestVersion == _onlineModRequestVersion)
            {
                _isLoadingOnlineMods = false;
                if (ReferenceEquals(_onlineModListCancellation, requestCancellation))
                {
                    _onlineModListCancellation = null;
                }
                RefreshOnlinePaneV2();
            }
            requestCancellation.Dispose();
        }
    }

    /// <summary>Best-effort single request so "Next page" opens from cache. Never shows errors.</summary>
    private async Task PrefetchNextOnlinePageAsync(
        string categoryId, int nextPage, bool useEndfieldMetadata, int authorId, string cacheCategoryKey, string configKey, string? serverSort)
    {
        try
        {
            // Let the visible page's images and download counts go first.
            await Task.Delay(800);
            if (!string.Equals(configKey, _lastLoadedOnlineConfigKey, StringComparison.Ordinal)
                || TryGetOnlineRateLimitDelay(out _)
                || await TryReadOnlineCategoryPageCacheAsync(cacheCategoryKey, nextPage) is not null) return;
            using var timeout = new CancellationTokenSource(OnlineModListRequestTimeout);
            OnlineCategoryPageResult next = await FetchGameBananaCategoryPageAsync(
                categoryId, nextPage, useEndfieldMetadata: useEndfieldMetadata, cancellationToken: timeout.Token, authorId: authorId, serverSort: serverSort);
            if (next.Mods.Count == 0) return;
            // Cache the page complete with download counts, so opening it needs no request at all.
            try { await FillOnlineDownloadCountsAsync(next.Mods, timeout.Token); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException) { } // Cached without counts; they load when the page opens.
            await WriteOnlineCategoryPageCacheAsync(cacheCategoryKey, next);
        }
        catch (OnlineRateLimitException ex)
        {
            RegisterOnlineRateLimit(ex.RetryAfter);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // Prefetch is optional; the normal page load reports real failures.
        }
    }

    private async Task<OnlineCategoryPageResult> FetchGameBananaCharacterFallbackPageAsync(
        string categoryId,
        string characterName,
        int page,
        bool useEndfieldMetadata,
        string cacheCategoryKey,
        CancellationToken cancellationToken = default,
        string? serverSort = null)
    {
        const int scanPageSize = 50;
        // A whole-category scan used to run page by page (about 14 x 350 ms for Endfield, at the
        // 5-second limit) and was repeated for every display page. Scan concurrently, then cache
        // every display page so paging stays local.
        IReadOnlyList<OnlineModCard> all = await OnlinePageScanner.ScanAsync(async (sourcePage, token) =>
        {
            OnlineCategoryPageResult sourceResult = await FetchGameBananaCategoryPageAsync(
                categoryId, sourcePage, scanPageSize, useEndfieldMetadata: useEndfieldMetadata, cancellationToken: token, serverSort: serverSort);
            return new OnlineScanPage<OnlineModCard>(sourceResult.Mods, sourceResult.TotalPages);
        }, OnlineRawPageLimit, maxConcurrency: 3, cancellationToken);

        List<OnlineModCard> matches = all
            .Where(mod => DoesOnlineModMatchCharacter(characterName, mod.CharacterName))
            .GroupBy(mod => mod.ItemId)
            .Select(group => group.First())
            .ToList();
        int displayPages = Math.Max(1, (int)Math.Ceiling(matches.Count / (double)OnlineDisplayPageSize));
        OnlineCategoryPageResult PageOf(int number) => new(
            matches.Skip((number - 1) * OnlineDisplayPageSize).Take(OnlineDisplayPageSize).ToList(),
            matches.Count, number, OnlineDisplayPageSize);
        // The caller caches the requested page; store the others now.
        for (int other = 1; other <= displayPages; other++)
        {
            if (other != page && matches.Count > 0) await WriteOnlineCategoryPageCacheAsync(cacheCategoryKey, PageOf(other));
        }
        return PageOf(page);
    }

    private async Task<OnlineCategoryPageResult> FetchGameBananaCategoryPageAsync(
        string categoryId,
        int page,
        int pageSize = OnlineDisplayPageSize,
        bool? useEndfieldMetadata = null,
        CancellationToken cancellationToken = default,
        int authorId = 0,
        string? serverSort = null)
    {
        bool shouldUseEndfieldMetadata = useEndfieldMetadata ?? _services.OnlineSource.IsEndfieldCategory(categoryId);
        OnlineCategoryPageResult result = await GameBananaCategoryClient.GetPageAsync(_host.Http, categoryId, page, pageSize, authorId, DateTimeOffset.Now, cancellationToken, serverSort);
        foreach (OnlineModCard card in result.Mods) AdaptGameBananaCategoryMod(card, shouldUseEndfieldMetadata);
        return result;
    }

    // GameBanana names the category; the page shows the character it stands for.
    private void AdaptGameBananaCategoryMod(OnlineModCard card, bool useEndfieldMetadata)
    {
        string categoryName = card.CharacterName;
        card.CharacterName = ResolveOnlineCharacterName(categoryName, card.Title, useEndfieldMetadata);
        card.IsCharacterNameInferred = IsGenericOnlineCharacterName(categoryName)
            && !string.Equals(card.CharacterName, categoryName, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(card.Author))
        {
            card.Author = T("OnlineFeed.UnknownAuthor");
        }
    }


    internal async Task<OnlineModCard?> FetchGameBananaModCardAsyncV2(
        int itemId, bool useEndfieldMetadata = false, CancellationToken cancellationToken = default)
    {
        GameBananaModMetadata metadata = await _host.Metadata.GetModAsync(itemId, cancellationToken);
        string category = string.IsNullOrWhiteSpace(metadata.CharacterName) ? T("OnlineFeed.Uncategorized") : metadata.CharacterName;
        string character = ResolveOnlineCharacterName(category, metadata.Title, useEndfieldMetadata);
        OnlineDownloadCandidate? file = OnlineDownloadSelectionPolicy.SelectDefault(metadata.DownloadFiles);
        return new OnlineModCard
        {
            ItemId = itemId, Title = metadata.Title, CharacterName = character,
            CategoryId = metadata.CategoryId > 0 ? metadata.CategoryId.ToString(CultureInfo.InvariantCulture) : string.Empty,
            IsCharacterNameInferred = IsGenericOnlineCharacterName(category)
                && !string.Equals(character, category, StringComparison.OrdinalIgnoreCase),
            RootCategoryName = metadata.RootCategoryName,
            Author = string.IsNullOrWhiteSpace(metadata.Author) ? T("OnlineFeed.UnknownAuthor") : metadata.Author,
            AuthorId = metadata.AuthorId,
            Likes = metadata.Likes, Views = metadata.Views, Downloads = metadata.Downloads,
            HotnessScore = GameBananaListingParser.Hotness(metadata.Likes, metadata.Views, metadata.Downloads),
            PreviewUrl = metadata.PreviewUrl, ProfileUrl = metadata.ProfileUrl,
            DownloadUrl = file?.DownloadUrl ?? metadata.FallbackDownloadUrl,
            DownloadFileName = file?.FileName ?? string.Empty, DownloadFiles = metadata.DownloadFiles.ToList(),
            DownloadFileId = file?.FileId ?? string.Empty,
            FileSizeBytes = file?.FileSizeBytes ?? 0, HasUpdates = metadata.HasUpdates,
            UpdatedAt = GameBananaJson.UnixTime(metadata.UpdatedEpoch, DateTimeOffset.Now)
        };
    }

    private string GetGameBananaDownloadMetricCacheFile(int itemId)
    {
        return Path.Combine(_onlineDetailsCachePath, $"downloads-{itemId}.json");
    }

    private async Task<int?> TryReadGameBananaDownloadMetricCacheAsync(int itemId)
    {
        CachedValue<GameBananaDownloadMetricCacheEntry>? sqliteMetric = _host.DataStore.TryReadCache<GameBananaDownloadMetricCacheEntry>(
            "download-metric",
            itemId.ToString(CultureInfo.InvariantCulture),
            TimeSpan.FromHours(12));
        if (sqliteMetric is not null)
        {
            return sqliteMetric.Value.DownloadCount;
        }

        try
        {
            string cacheFile = GetGameBananaDownloadMetricCacheFile(itemId);
            if (!File.Exists(cacheFile)
                || DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) > TimeSpan.FromHours(12))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(cacheFile);
            GameBananaDownloadMetricCacheEntry? entry = JsonSerializer.Deserialize<GameBananaDownloadMetricCacheEntry>(json);
            return entry?.DownloadCount;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteGameBananaDownloadMetricCacheAsync(int itemId, int downloadCount)
    {
        try
        {
            Directory.CreateDirectory(_onlineDetailsCachePath);
            var entry = new GameBananaDownloadMetricCacheEntry
            {
                CachedAtUtc = DateTimeOffset.UtcNow,
                DownloadCount = Math.Max(0, downloadCount)
            };
            _host.DataStore.WriteCache("download-metric", itemId.ToString(CultureInfo.InvariantCulture), entry);
            await File.WriteAllTextAsync(
                GetGameBananaDownloadMetricCacheFile(itemId),
                JsonSerializer.Serialize(entry));
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Write download count cache", ex);
        }
    }

    private Task<Dictionary<int, int>> FetchGameBananaDownloadCountsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken)
        => GameBananaDownloadCounts.FetchAsync(_host.Http, itemIds, cancellationToken);

    /// <summary>
    /// Fills download counts from the 12-hour metric cache, then one batch request. Returns how many
    /// Mods changed; anything still unknown stays at 0 for a later attempt.
    /// </summary>
    private async Task<int> FillOnlineDownloadCountsAsync(IReadOnlyList<OnlineModCard> mods, CancellationToken cancellationToken)
    {
        int filled = 0;
        var unknown = new List<OnlineModCard>();
        foreach (OnlineModCard mod in mods.Where(mod => mod.Downloads <= 0))
        {
            if (await TryReadGameBananaDownloadMetricCacheAsync(mod.ItemId) is int cached) filled += ApplyDownloadCount(mod, cached);
            else unknown.Add(mod);
        }
        if (unknown.Count == 0) return filled;
        Dictionary<int, int> fetched = await FetchGameBananaDownloadCountsAsync(unknown.Select(mod => mod.ItemId).ToArray(), cancellationToken);
        foreach (OnlineModCard mod in unknown)
        {
            if (!fetched.TryGetValue(mod.ItemId, out int downloads)) continue;
            filled += ApplyDownloadCount(mod, downloads);
            await WriteGameBananaDownloadMetricCacheAsync(mod.ItemId, downloads);
        }
        return filled;

        static int ApplyDownloadCount(OnlineModCard mod, int downloads)
        {
            if (mod.Downloads == downloads) return 0;
            mod.Downloads = Math.Max(0, downloads);
            mod.HotnessScore = GameBananaListingParser.Hotness(mod.Likes, mod.Views, mod.Downloads);
            return 1;
        }
    }

    /// <summary>
    /// Fallback when the inline batch did not finish: one more batch later, written into the cards
    /// already on screen. It never rebuilds or re-sorts the list under the user.
    /// </summary>
    private async Task EnrichMissingGameBananaDownloadsAsync(
        OnlineCategoryPageResult pageResult,
        string cacheCategoryKey,
        string configKey,
        int enrichmentVersion)
    {
        try
        {
            await Task.Delay(1500);
            bool Current() => enrichmentVersion == _onlineDownloadEnrichmentVersion
                && string.Equals(configKey, _lastLoadedOnlineConfigKey, StringComparison.Ordinal);
            if (!Current() || TryGetOnlineRateLimitDelay(out _)) return;
            using var timeout = new CancellationTokenSource(OnlineModListRequestTimeout);
            int filled = await FillOnlineDownloadCountsAsync(pageResult.Mods, timeout.Token);
            if (filled == 0 || !Current()) return;
            RefreshOnlineCardMetrics();
            await WriteOnlineCategoryPageCacheAsync(cacheCategoryKey, pageResult);
        }
        catch (OnlineRateLimitException ex)
        {
            RegisterOnlineRateLimit(ex.RetryAfter);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or IOException)
        {
            // Counts are decorative; the page itself already loaded.
        }
    }

    private bool MatchesOnlineSearch(OnlineModCard mod, string searchText)
    {
        EndfieldCharacterInfo? character = _services.OnlineSource.IsEndfield(_services.Repositories.Selected)
            ? FindEndfieldCharacter(mod.CharacterName) : null;
        return OnlineBrowseStateAdapter.MatchesSearch(mod.Title, mod.CharacterName, mod.Author,
            searchText, character?.AllNames);
    }








    private async void OnOnlinePrevPageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Online.Page.CurrentPage <= 1 || _isLoadingOnlineMods)
            {
                return;
            }

            ResetOnlineBrowsePosition();
            _services.Online.Page.CurrentPage--;
            await LoadOnlineModsAsync(forceReload: false);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnOnlinePrevPageClicked", ex);
        }
    }

    private async void OnOnlineNextPageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Online.Page.CurrentPage >= _services.Online.Page.TotalPages || _isLoadingOnlineMods)
            {
                return;
            }

            ResetOnlineBrowsePosition();
            _services.Online.Page.CurrentPage++;
            await LoadOnlineModsAsync(forceReload: false);
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnOnlineNextPageClicked", ex);
        }
    }

    private void OnOnlineSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _services.Online.Page.SearchText = OnlineSearchTextBox.Text?.Trim() ?? string.Empty;
        if (_resettingOnlineFilters) return;
        ResetOnlineBrowsePosition();
        RefreshOnlinePaneV2();
        _browseSaveTimer?.Stop();
        _browseSaveTimer?.Start();
    }

    private string GetOnlineCategoryPageCacheFile(string categoryId, int page)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(categoryId))).ToLowerInvariant()[..16];
        return Path.Combine(_onlinePageCachePath, $"{key}-page-{Math.Max(1, page)}.json");
    }

    private async Task<OnlineCategoryPageResult?> TryReadOnlineCategoryPageCacheAsync(
        string categoryId,
        int page,
        bool allowExpired = false)
    {
        string sqliteKey = $"{categoryId}:{Math.Max(1, page)}:{OnlineDisplayPageSize}";
        CachedValue<OnlineCategoryPageCacheEntry>? sqliteEntry = _host.DataStore.TryReadCache<OnlineCategoryPageCacheEntry>(
            "online-page",
            sqliteKey,
            TimeSpan.FromHours(6),
            allowExpired);
        if (sqliteEntry?.Value is { Mods.Count: > 0 } stored && stored.PageSize == OnlineDisplayPageSize)
        {
            return new OnlineCategoryPageResult(stored.Mods, stored.TotalCount, stored.Page, stored.PageSize)
            {
                CachedAtUtc = sqliteEntry.CachedAtUtc,
                IsFromCache = true
            };
        }

        try
        {
            string cacheFile = GetOnlineCategoryPageCacheFile(categoryId, page);
            if (!File.Exists(cacheFile)
                || (!allowExpired && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) > TimeSpan.FromHours(6)))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(cacheFile);
            OnlineCategoryPageCacheEntry? entry = JsonSerializer.Deserialize<OnlineCategoryPageCacheEntry>(json);
            if (entry is null
                || entry.Mods.Count == 0
                || entry.PageSize != OnlineDisplayPageSize)
            {
                return null;
            }

            return new OnlineCategoryPageResult(entry.Mods, entry.TotalCount, entry.Page, entry.PageSize)
            {
                CachedAtUtc = entry.CachedAtUtc,
                IsFromCache = true
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteOnlineCategoryPageCacheAsync(string categoryId, OnlineCategoryPageResult result)
    {
        try
        {
            Directory.CreateDirectory(_onlinePageCachePath);
            var entry = new OnlineCategoryPageCacheEntry
            {
                CachedAtUtc = DateTimeOffset.UtcNow,
                Mods = result.Mods,
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize
            };
            _host.DataStore.WriteCache("online-page", $"{categoryId}:{Math.Max(1, result.Page)}:{OnlineDisplayPageSize}", entry);
            string json = JsonSerializer.Serialize(entry);
            await File.WriteAllTextAsync(GetOnlineCategoryPageCacheFile(categoryId, result.Page), json);
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Write online page cache", ex);
        }
    }
}
