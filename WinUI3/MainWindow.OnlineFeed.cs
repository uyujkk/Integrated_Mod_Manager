using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"IntegratedModManager/{AppVersion.TrimStart('v', 'V')}");
        return client;
    }

    private void InitializeOnlineControls()
    {
        OnlineSearchTextBox.PlaceholderText = L("搜索当前页：角色、Mod 或作者", "Search this page: character, mod or author");
        OnlineSortComboBox.Items.Clear();
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按热度排序", "Sort by hotness"), Tag = OnlineSortMode.Hotness });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按下载量排序", "Sort by downloads"), Tag = OnlineSortMode.Downloads });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按点赞数排序", "Sort by likes"), Tag = OnlineSortMode.Likes });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按查看数排序", "Sort by views"), Tag = OnlineSortMode.Views });
        OnlineSortComboBox.Items.Add(new ComboBoxItem { Content = L("按更新时间排序", "Sort by updated time"), Tag = OnlineSortMode.Updated });
        OnlineSortComboBox.SelectedItem = OnlineSortComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is OnlineSortMode sortMode && sortMode == _onlineSortMode)
            ?? OnlineSortComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        PopulateOnlineCharacterOptions();
    }

    private void RefreshOnlinePaneV2()
    {
        EnsureOnlineBrowseSession();
        RefreshOnlineNavigationButtonLabels();
        WorkspaceRepository? repository = GetSelectedRepository();
        bool hasRepository = repository is not null;
        string effectiveSource = GetEffectiveOnlineSource(repository);
        string effectiveGameName = GetEffectiveOnlineGameName(repository);
        string effectiveCategoryId = GetEffectiveOnlineCategoryId(repository);
        bool isSupportedSource = IsGameBananaSource(effectiveSource);
        bool canLoadOnlineMods = hasRepository && isSupportedSource && !string.IsNullOrWhiteSpace(effectiveCategoryId);
        string requestCategoryId = GetOnlineRequestCategoryId(repository);
        string configKey = hasRepository
            ? $"{repository!.Id}|{effectiveSource}|{requestCategoryId}|character={_onlineCharacterFilter}|page={_onlineCurrentPage}{OnlineAuthorKey}"
            : string.Empty;

        if (!string.IsNullOrEmpty(_lastLoadedOnlineConfigKey)
            && !string.Equals(_lastLoadedOnlineConfigKey, configKey, StringComparison.Ordinal)
            && !_isLoadingOnlineMods)
        {
            _onlineMods.Clear();
        }

        OnlineRepositoryValueTextBlock.Text = hasRepository ? repository!.Name : L("未选择", "Not selected");
        OnlineSourceValueTextBlock.Text = hasRepository ? effectiveSource : L("未选择", "Not selected");
        OnlineReadyValueTextBlock.Text = canLoadOnlineMods
            ? (_isLoadingOnlineMods ? L("加载中", "Loading") : L("就绪", "Ready"))
            : L("待配置", "Needs setup");
        OnlineCategoryValueTextBlock.Text = hasRepository
            ? (string.IsNullOrWhiteSpace(effectiveGameName)
                ? effectiveCategoryId
                : $"{effectiveCategoryId} · {effectiveGameName}")
            : L("先从左侧选择一个仓库。", "Select a repository from the left first.");
        OnlineNotesValueTextBlock.Text = hasRepository
            ? (string.IsNullOrWhiteSpace(repository!.Notes)
                ? (string.IsNullOrWhiteSpace(effectiveGameName)
                    ? L("当前仓库还没有备注，可用来记录目标游戏、来源说明或安装规则。", "No notes yet. Use this area to record the target game, source notes, or install rules.")
                    : L($"当前目标游戏：{effectiveGameName}", $"Current game: {effectiveGameName}"))
                : NormalizeUiText(repository.Notes))
            : L("在线页面会按当前仓库的来源配置加载内容。", "The online page will load content based on the selected repository configuration.");

        RefreshOnlineButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineLoadingOverlay.Visibility = _isLoadingOnlineMods ? Visibility.Visible : Visibility.Collapsed;
        OnlineLoadingTextBlock.Text = L("正在读取在线 Mod，请稍候...", "Loading online mods, please wait...");
        OnlineStatusTextBlock.Text = string.IsNullOrWhiteSpace(_onlineStatusZh) || string.IsNullOrWhiteSpace(_onlineStatusEn)
            ? L("点击刷新在线列表后，会从 GameBanana 拉取当前分类下的最新 Mod。", "Click Refresh Online List to fetch the latest mods for the current gameid from GameBanana.")
            : L(_onlineStatusZh!, _onlineStatusEn!);
        bool hasCacheStatus = !string.IsNullOrWhiteSpace(_onlineCacheStatusZh)
            && !string.IsNullOrWhiteSpace(_onlineCacheStatusEn);
        OnlineCacheStatusBorder.Visibility = hasCacheStatus ? Visibility.Visible : Visibility.Collapsed;
        OnlineCacheStatusTextBlock.Text = hasCacheStatus
            ? L(_onlineCacheStatusZh!, _onlineCacheStatusEn!)
            : string.Empty;
        List<OnlineModCard> filteredMods = canLoadOnlineMods ? GetFilteredOnlineMods() : [];
        List<OnlineModCard> visibleMods = GetVisibleOnlineMods(filteredMods);
        RefreshOnlineFilterSummary(visibleMods.Count, _onlineMods.Count);
        OnlinePaginationTextBlock.Text = canLoadOnlineMods
            ? L(
                $"第 {_onlineCurrentPage} / {_onlineTotalPages} 页，共 {_onlineTotalCount} 个 Mod",
                $"Page {_onlineCurrentPage} / {_onlineTotalPages}, {_onlineTotalCount} mods total")
            : L("等待在线配置", "Waiting for online config");
        OnlinePrevPageButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods && _onlineCurrentPage > 1;
        OnlineNextPageButton.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods && _onlineCurrentPage < _onlineTotalPages;
        OnlineSearchTextBox.PlaceholderText = L("搜索当前页：角色、Mod 或作者", "Search this page: character, mod or author");
        OnlineSearchTextBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineCharacterComboBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineSortComboBox.IsEnabled = canLoadOnlineMods && !_isLoadingOnlineMods;
        OnlineCharacterRailTitleTextBlock.Text = L("按角色浏览", "Browse by character");
        RefreshOnlineCharacterAvatarStrip();
        UpdateOnlineCardLayoutControls();

        // Status-only refreshes must not reset either virtualized view's scroll position.
        bool resultsChanged = OnlineBrowseStateAdapter.Synchronize(_onlineVisibleMods, visibleMods);
        RefreshOnlineCardPresentation();
        RefreshOnlineCardStates();
        ToolTipService.SetToolTip(OnlineSearchTextBox, L(
            "搜索当前页的角色、Mod 名称或作者；输入 author:作者名 可仅按作者筛选。清空搜索可恢复当前页。",
            "Search this page by character, mod title or author. Use author:name for author-only search. Clear to show this page again."));
        OnlineEmptyResultsContentControl.Content = null;
        OnlineEmptyResultsContentControl.Visibility = Visibility.Collapsed;

        if (!hasRepository)
        {
            ShowOnlineEmptyResults(CreateOnlinePreviewCard(
                L("等待选择仓库", "Waiting for repository"),
                L("请先在左侧选择一个仓库，在线资源库会根据这个仓库的来源配置继续加载。", "Select a repository from the left first. The online page will continue from that repository's source settings."),
                L("未开始", "Not started")));
            AnimateOnlineCardsEntrance();
            return;
        }

        if (!isSupportedSource)
        {
            ShowOnlineEmptyResults(CreateOnlinePreviewCard(
                L("来源暂不支持", "Source not supported yet"),
                L("当前在线页面优先支持 GameBanana。你可以把在线来源改成 GameBanana，或继续保留这个字段等待后续站点接入。", "This online page currently supports GameBanana first. Change the source to GameBanana or keep the field for later site integrations."),
                effectiveSource));
            AnimateOnlineCardsEntrance();
            return;
        }

        if (_onlineMods.Count == 0)
        {
            ShowOnlineEmptyResults(CreateOnlinePreviewCard(
                L("准备加载在线列表", "Ready to load real data"),
                L("这里会根据当前仓库里的 GameBanana 配置拉取真实 Mod 列表，并提供查看详情、打开页面和下载解压入口。", "This page now uses the repository's GameBanana config to load real mods. The first pass shows title, author, updated time, page link, and download entry points."),
                _isLoadingOnlineMods ? L("加载中", "Loading") : L("可以刷新", "Ready to refresh")));
        }
        else
        {
            if (visibleMods.Count == 0)
            {
                ShowOnlineEmptyResults(CreateOnlinePreviewCard(
                    L("没有匹配的皮肤 Mod", "No matching skin mods"),
                    L("当前页只显示 Skins 分类。可清空搜索、切换角色或翻页；支持搜索角色、Mod 名称和作者。", "This page shows Skins entries. Clear your search, change character or try another page. Search supports characters, mod titles and authors."),
                    L("筛选结果为空", "No results")));
            }
        }

        if (_onlineCardLayoutMode == OnlineCardLayoutMode.Grid)
        {
            DispatcherQueue.TryEnqueue(() => UpdateOnlineGridCardMetrics(
                GetOnlineGridAvailableWidth(OnlineVirtualizedGridView.ActualWidth)));
        }
        DispatcherQueue.TryEnqueue(UpdateOnlineWorkspaceWidthConstraint);
        if (resultsChanged || _animateOnlineCardsOnNextRefresh) AnimateOnlineCardsEntrance();
        QueueRestoreOnlineBrowsePosition();

        if (_currentPrimarySection == PrimarySection.Online
            && canLoadOnlineMods
            && configKey != _lastLoadedOnlineConfigKey
            && !string.Equals(configKey, _blockedOnlineAutoLoadConfigKey, StringComparison.Ordinal)
            && !_isLoadingOnlineMods)
        {
            _ = LoadOnlineModsAsync(forceReload: false);
        }
    }

    private bool IsGameBananaSource(string? source)
    {
        return string.IsNullOrWhiteSpace(source)
            || source.Contains("gamebanana", StringComparison.OrdinalIgnoreCase);
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
        IEnumerable<OnlineModCard> query = _onlineMods
            .Where(mod => IsAllowedSkinCategory(mod.RootCategoryName));

        if (!string.IsNullOrWhiteSpace(_onlineCharacterFilter))
        {
            query = query.Where(mod => DoesOnlineModMatchCharacter(
                _onlineCharacterFilter,
                mod.CharacterName));
        }

        if (!string.IsNullOrWhiteSpace(_onlineSearchText))
        {
            query = query.Where(mod => MatchesOnlineSearch(mod, _onlineSearchText));
        }

        if (_onlineAuthorWorkFilter == 1) query = query.Where(mod => FindInstalledOnlineMod(mod) is not null);
        else if (_onlineAuthorWorkFilter == 2) query = query.Where(IsSavedOnlineMod);

        query = _onlineSortMode switch
        {
            OnlineSortMode.Downloads => query.OrderByDescending(mod => mod.Downloads).ThenByDescending(mod => mod.HotnessScore),
            OnlineSortMode.Likes => query.OrderByDescending(mod => mod.Likes).ThenByDescending(mod => mod.HotnessScore),
            OnlineSortMode.Views => query.OrderByDescending(mod => mod.Views).ThenByDescending(mod => mod.HotnessScore),
            OnlineSortMode.Updated => query.OrderByDescending(mod => mod.UpdatedAt).ThenByDescending(mod => mod.HotnessScore),
            _ => query.OrderByDescending(mod => mod.HotnessScore).ThenByDescending(mod => mod.Downloads)
        };

        return query.ToList();
    }

    private List<OnlineModCard> GetVisibleOnlineMods(List<OnlineModCard> filteredMods)
    {
        return filteredMods
            .ToList();
    }

    private string GetEffectiveOnlineSource(WorkspaceRepository? repository)
    {
        if (repository is null)
        {
            return L("未选择", "Not selected");
        }

        return string.IsNullOrWhiteSpace(repository.OnlineSourceSite)
            ? DefaultOnlineSourceSite
            : repository.OnlineSourceSite.Trim();
    }

    private string GetEffectiveOnlineGameName(WorkspaceRepository? repository)
    {
        if (repository is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(repository.OnlineGameName)
            ? DefaultOnlineGameName
            : repository.OnlineGameName.Trim();
    }

    private string GetEffectiveOnlineCategoryId(WorkspaceRepository? repository)
    {
        if (repository is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(repository.OnlineCategoryId)
            ? DefaultOnlineCategoryId
            : repository.OnlineCategoryId.Trim();
    }

    private string GetOnlineRequestCategoryId(WorkspaceRepository? repository)
    {
        EnsureOnlineCharacterOptionsForRepository(repository);
        OnlineCharacterOption? selectedCharacter = _onlineCharacterOptions.FirstOrDefault(character =>
            string.Equals(character.Key, _onlineCharacterFilter, StringComparison.OrdinalIgnoreCase));
        return selectedCharacter is not null && !string.IsNullOrWhiteSpace(selectedCharacter.CategoryId)
            ? selectedCharacter.CategoryId
            : GetEffectiveOnlineCategoryId(repository);
    }

    private void SetOnlineStatus(string zh, string en)
    {
        _onlineStatusZh = zh;
        _onlineStatusEn = en;
        OnlineStatusTextBlock.Text = L(zh, en);
        ToolTipService.SetToolTip(OnlineStatusTextBlock, L(zh, en));
    }

    private void SetOnlineCacheStatus(string? zh, string? en)
    {
        _onlineCacheStatusZh = zh;
        _onlineCacheStatusEn = en;
        bool hasStatus = !string.IsNullOrWhiteSpace(zh) && !string.IsNullOrWhiteSpace(en);
        OnlineCacheStatusBorder.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;
        OnlineCacheStatusTextBlock.Text = hasStatus ? L(zh!, en!) : string.Empty;
        ToolTipService.SetToolTip(OnlineCacheStatusBorder, hasStatus ? L(zh!, en!) : null);
    }

    private void CancelOnlineModListRequest()
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

    private (string Zh, string En) FormatOnlineCacheAge(DateTimeOffset? cachedAtUtc, bool stale)
    {
        if (cachedAtUtc is null)
        {
            return stale ? ("离线缓存", "Stale cache") : ("本地缓存", "Local cache");
        }

        TimeSpan age = DateTimeOffset.UtcNow - cachedAtUtc.Value;
        string valueZh;
        string valueEn;
        if (age < TimeSpan.FromMinutes(1))
        {
            valueZh = "刚刚";
            valueEn = "just now";
        }
        else if (age < TimeSpan.FromHours(1))
        {
            int minutes = Math.Max(1, (int)age.TotalMinutes);
            valueZh = $"{minutes} 分钟前";
            valueEn = $"{minutes} min ago";
        }
        else if (age < TimeSpan.FromDays(1))
        {
            int hours = Math.Max(1, (int)age.TotalHours);
            valueZh = $"{hours} 小时前";
            valueEn = $"{hours} h ago";
        }
        else
        {
            int days = Math.Max(1, (int)age.TotalDays);
            valueZh = $"{days} 天前";
            valueEn = $"{days} d ago";
        }

        return stale
            ? ($"离线缓存 · {valueZh}", $"Stale cache · {valueEn}")
            : ($"缓存 · {valueZh}", $"Cache · {valueEn}");
    }

    private void SetOnlineDownloadProgress(bool isVisible, double percent, string zh, string en, bool isIndeterminate = false)
    {
        OnlineDownloadProgressPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        OnlineDownloadProgressBar.IsIndeterminate = isVisible && isIndeterminate;
        OnlineDownloadProgressBar.Value = Math.Max(0, Math.Min(100, percent));
        OnlineDownloadProgressTextBlock.Text = L(zh, en);
    }

    private async void OnRefreshOnlineModsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            WorkspaceRepository? repository = GetSelectedRepository();
            if (repository is not null)
            {
                _ = EnsureOnlineCharacterCatalogAsync(repository, forceReload: true);
            }

            await LoadOnlineModsAsync(forceReload: true);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnRefreshOnlineModsClicked", ex);
        }
    }

    private async Task LoadOnlineModsAsync(bool forceReload)
    {
        EnsureOnlineBrowseSession();
        SaveShellConfig();
        CancelOnlineModListRequest();
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        EnsureOnlineCharacterOptionsForRepository(repository);
        string source = GetEffectiveOnlineSource(repository);
        string baseCategoryId = GetEffectiveOnlineCategoryId(repository);
        string categoryId = GetOnlineRequestCategoryId(repository);
        bool useEndfieldMetadata = IsEndfieldRepository(repository);
        if (!IsGameBananaSource(source) || string.IsNullOrWhiteSpace(baseCategoryId))
        {
            SetOnlineStatus("当前仓库还没有可用的在线来源配置。", "The current repository does not have a usable online source configuration yet.");
            RefreshOnlinePaneV2();
            return;
        }

        int authorId = _onlineAuthorId;
        string configKey = $"{repository.Id}|{source}|{categoryId}|character={_onlineCharacterFilter}|page={_onlineCurrentPage}{OnlineAuthorKey}";
        if (forceReload)
        {
            _blockedOnlineAutoLoadConfigKey = null;
        }
        else if (string.Equals(configKey, _blockedOnlineAutoLoadConfigKey, StringComparison.Ordinal))
        {
            RefreshOnlinePaneV2();
            return;
        }

        if (!forceReload && string.Equals(configKey, _lastLoadedOnlineConfigKey, StringComparison.Ordinal) && _onlineMods.Count > 0)
        {
            RefreshOnlinePaneV2();
            return;
        }

        var requestCancellation = new CancellationTokenSource();
        _onlineModListCancellation = requestCancellation;
        int requestVersion = ++_onlineModRequestVersion;
        int downloadEnrichmentVersion = ++_onlineDownloadEnrichmentVersion;

        _isLoadingOnlineMods = true;
        RefreshOnlineButton.Content = L("加载中...", "Loading...");
        RefreshOnlineButton.IsEnabled = false;
        string gameName = GetEffectiveOnlineGameName(repository);
        string categoryLabel = string.IsNullOrWhiteSpace(gameName)
            ? categoryId
            : $"{gameName} / {categoryId}";
        SetOnlineStatus($"正在从 GameBanana 读取分类 {categoryLabel} 的在线 Mod 列表...", $"Loading online mods from GameBanana for category {categoryLabel}...");
        SetOnlineCacheStatus("正在连接", "Connecting");
        RefreshOnlinePaneV2();
        using CancellationTokenSource timeoutCancellation = new(OnlineModListRequestTimeout);
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellation.Token,
            timeoutCancellation.Token);

        try
        {
            bool needsCharacterFallback = !string.IsNullOrWhiteSpace(_onlineCharacterFilter)
                && string.Equals(categoryId, baseCategoryId, StringComparison.OrdinalIgnoreCase);
            string cacheCategoryKey = (needsCharacterFallback
                ? $"category-v3|{baseCategoryId}-character-{_onlineCharacterFilter}"
                : $"category-v3|{categoryId}") + (authorId > 0 ? $"|author={authorId}" : string.Empty);
            OnlineCategoryPageResult? cachedPage = forceReload
                ? null
                : await TryReadOnlineCategoryPageCacheAsync(cacheCategoryKey, _onlineCurrentPage);
            bool loadedFromCache = cachedPage is not null;
            bool loadedDuringRateLimit = false;
            bool loadedAfterTimeout = false;
            OnlineCategoryPageResult pageResult;
            if (cachedPage is null && TryGetOnlineRateLimitDelay(out TimeSpan cooldownRemaining))
            {
                OnlineCategoryPageResult? stalePage = await TryReadOnlineCategoryPageCacheAsync(
                    cacheCategoryKey,
                    _onlineCurrentPage,
                    allowExpired: true);
                if (stalePage is null)
                {
                    int seconds = Math.Max(1, (int)Math.Ceiling(cooldownRemaining.TotalSeconds));
                    _blockedOnlineAutoLoadConfigKey = configKey;
                    SetOnlineStatus(
                        $"访问频率受限，{seconds} 秒后可再次刷新；当前内容保持不变。",
                        $"Rate limit cooldown is active. Try again in {seconds} seconds; current content is preserved.");
                    SetOnlineCacheStatus($"冷却 {seconds} 秒", $"Cooldown {seconds}s");
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
                                _onlineCharacterFilter,
                                _onlineCurrentPage,
                                useEndfieldMetadata,
                                cacheCategoryKey,
                                linkedCancellation.Token)
                            : await FetchGameBananaCategoryPageAsync(
                                categoryId,
                                _onlineCurrentPage,
                                useEndfieldMetadata: useEndfieldMetadata,
                                cancellationToken: linkedCancellation.Token,
                                authorId: authorId));
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
                        _onlineCurrentPage,
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
                        _onlineCurrentPage,
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
            if (_onlineCurrentPage > Math.Max(1, pageResult.TotalPages))
            {
                _onlineCurrentPage = Math.Max(1, pageResult.TotalPages);
                ResetOnlineBrowsePosition();
                await LoadOnlineModsAsync(forceReload: false);
                return;
            }

            _onlineMods.Clear();
            _onlineMods.AddRange(pageResult.Mods);
            _onlineKnownCharacters.Clear();
            foreach (string character in _onlineMods
                         .Select(mod => mod.CharacterName)
                         .Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                _onlineKnownCharacters.Add(character);
            }

            MergeOnlineCharacterOptionsFromMods(_onlineMods);
            PopulateOnlineCharacterOptions();
            _onlineTotalCount = pageResult.TotalCount;
            _onlineTotalPages = Math.Max(1, pageResult.TotalPages);

            if (pageResult.Mods.Count == 0)
            {
                _lastLoadedOnlineConfigKey = configKey;
                SetOnlineStatus("GameBanana 已连接，但当前分类没有返回可用的皮肤 Mod。", "GameBanana responded, but the current category returned no skin mods.");
                return;
            }

            _lastLoadedOnlineConfigKey = configKey;
            _blockedOnlineAutoLoadConfigKey = null;

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
            if (!needsCharacterFallback && !loadedDuringRateLimit && !loadedAfterTimeout && _onlineCurrentPage < _onlineTotalPages)
            {
                _ = PrefetchNextOnlinePageAsync(categoryId, _onlineCurrentPage + 1, useEndfieldMetadata, authorId, cacheCategoryKey, configKey);
            }

            OnlineCharacterOption? selectedCharacter = _onlineCharacterOptions.FirstOrDefault(character =>
                string.Equals(character.Key, _onlineCharacterFilter, StringComparison.OrdinalIgnoreCase));
            string roleLabelZh = selectedCharacter?.DisplayNameZh ?? "全部分类";
            string roleLabelEn = selectedCharacter?.DisplayNameEn ?? "all categories";
            (string cacheZh, string cacheEn) = loadedFromCache
                ? FormatOnlineCacheAge(pageResult.CachedAtUtc, loadedDuringRateLimit || loadedAfterTimeout)
                : ("实时数据", "Live");
            SetOnlineCacheStatus(cacheZh, cacheEn);
            SetOnlineStatus(
                loadedAfterTimeout
                    ? $"连接超过 5 秒，已停止访问并显示本地缓存 · {roleLabelZh} · 第 {_onlineCurrentPage} 页"
                    : loadedDuringRateLimit
                    ? $"请求频率受限，已显示本地缓存 · {roleLabelZh} · 第 {_onlineCurrentPage} 页"
                    : loadedFromCache
                    ? $"本地缓存 · {roleLabelZh} · 第 {_onlineCurrentPage} 页 · {_onlineTotalCount} 个 Mod"
                    : $"已更新 · {roleLabelZh} · 第 {_onlineCurrentPage} 页 · {_onlineTotalCount} 个 Mod",
                loadedAfterTimeout
                    ? $"Connection exceeded 5 seconds; stopped and showing local cache · {roleLabelEn} · Page {_onlineCurrentPage}"
                    : loadedDuringRateLimit
                    ? $"Rate limited; showing local cache · {roleLabelEn} · Page {_onlineCurrentPage}"
                    : loadedFromCache
                    ? $"Local cache · {roleLabelEn} · Page {_onlineCurrentPage} · {_onlineTotalCount} mods"
                    : $"Updated · {roleLabelEn} · Page {_onlineCurrentPage} · {_onlineTotalCount} mods");
            if (loadedAfterTimeout)
            {
                ShowAppNotification(
                    "在线访问超过 5 秒，已停止并显示本地缓存。",
                    "Online access exceeded 5 seconds; the request was stopped and local cache is shown.",
                    InfoBarSeverity.Warning);
            }
            else if (loadedDuringRateLimit)
            {
                ShowAppNotification(
                    "GameBanana 正在限流，已继续显示本地缓存。",
                    "GameBanana is rate limiting requests; local cache is being shown.",
                    InfoBarSeverity.Warning);
            }
            else
            {
                ShowAppNotification("在线 Mod 列表已更新。", "The online mod list has been refreshed.");
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
            SetOnlineStatus(
                $"GameBanana 请求较频繁，已暂停访问；{seconds} 秒后可再次刷新。",
                $"GameBanana is rate limiting requests. Access is paused; try again in {seconds} seconds.");
            SetOnlineCacheStatus($"冷却 {seconds} 秒", $"Cooldown {seconds}s");
            ShowAppNotification(
                "请求过于频繁，已暂停在线刷新；当前内容会保留。",
                "Online refresh paused due to rate limiting; current content is preserved.",
                InfoBarSeverity.Warning);
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
            SetOnlineStatus(
                "连接超过 5 秒，已停止访问；当前内容保持不变。",
                "Connection exceeded 5 seconds. The request was stopped and current content was preserved.");
            SetOnlineCacheStatus("访问已停止", "Stopped");
            ShowAppNotification(
                "在线 Mod 访问超时，已在 5 秒后停止。",
                "Online mod access timed out and was stopped after 5 seconds.",
                InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            if (requestVersion != _onlineModRequestVersion || requestCancellation.IsCancellationRequested)
            {
                return;
            }
            _blockedOnlineAutoLoadConfigKey = configKey;
            SetOnlineStatus("读取在线 Mod 失败，请稍后重试。", "Failed to load online mods. Please try again later.");
            SetOnlineCacheStatus("连接失败", "Connection failed");
            await ShowMessageAsync(
                L("读取 GameBanana 列表失败：", "Failed to load the GameBanana list: ") + ex.Message,
                L("在线加载失败", "Online load failed"));
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
        string categoryId, int nextPage, bool useEndfieldMetadata, int authorId, string cacheCategoryKey, string configKey)
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
                categoryId, nextPage, useEndfieldMetadata: useEndfieldMetadata, cancellationToken: timeout.Token, authorId: authorId);
            if (next.Mods.Count == 0) return;
            // Cache the page complete with download counts, so opening it needs no request at all.
            try { await FillOnlineDownloadCountsAsync(next.Mods, timeout.Token); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException) { }
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
        CancellationToken cancellationToken = default)
    {
        const int scanPageSize = 50;
        // A whole-category scan used to run page by page (about 14 x 350 ms for Endfield, at the
        // 5-second limit) and was repeated for every display page. Scan concurrently, then cache
        // every display page so paging stays local.
        IReadOnlyList<OnlineModCard> all = await OnlinePageScanner.ScanAsync(async (sourcePage, token) =>
        {
            OnlineCategoryPageResult sourceResult = await FetchGameBananaCategoryPageAsync(
                categoryId, sourcePage, scanPageSize, useEndfieldMetadata: useEndfieldMetadata, cancellationToken: token);
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
        int authorId = 0)
    {
        bool shouldUseEndfieldMetadata = useEndfieldMetadata ?? IsEndfieldCategoryId(categoryId);
        pageSize = Math.Clamp(pageSize, 1, 50);
        string requestUrl = OnlineBrowsePolicy.CategoryPageUrl(categoryId, page, pageSize, authorId);
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new OnlineRateLimitException(response.Headers.RetryAfter?.Delta);
        }
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        List<OnlineModCard> mods = [];
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new OnlineCategoryPageResult(mods, 0, page, pageSize);
        }

        JsonElement metadata = root.TryGetProperty("_aMetadata", out JsonElement metadataElement) ? metadataElement : default;
        int totalCount = TryGetInt32Property(metadata, "_nRecordCount");
        int responsePageSize = Math.Max(1, TryGetInt32Property(metadata, "_nPerpage"));

        if (!root.TryGetProperty("_aRecords", out JsonElement recordsElement) || recordsElement.ValueKind != JsonValueKind.Array)
        {
            return new OnlineCategoryPageResult(mods, totalCount, page, responsePageSize);
        }

        foreach (JsonElement record in recordsElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnlineModCard? mod = ParseGameBananaCategoryMod(record, shouldUseEndfieldMetadata);
            if (mod is not null)
            {
                mods.Add(mod);
            }
        }

        return new OnlineCategoryPageResult(mods, totalCount, page, responsePageSize);
    }

    private OnlineModCard? ParseGameBananaCategoryMod(JsonElement record, bool useEndfieldMetadata)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        int itemId = TryGetInt32Property(record, "_idRow");
        if (itemId <= 0)
        {
            return null;
        }

        string title = TryGetStringProperty(record, "_sName") ?? $"Mod {itemId}";
        string profileUrl = TryGetStringProperty(record, "_sProfileUrl") ?? $"https://gamebanana.com/mods/{itemId}";
        string author = string.Empty;
        int authorId = 0;
        if (record.TryGetProperty("_aSubmitter", out JsonElement submitterElement))
        {
            author = TryGetStringProperty(submitterElement, "_sName") ?? string.Empty;
            authorId = TryGetInt32Property(submitterElement, "_idRow");
        }

        string rootCategoryName = string.Empty;
        if (record.TryGetProperty("_aRootCategory", out JsonElement rootCategoryElement))
        {
            rootCategoryName = TryGetStringProperty(rootCategoryElement, "_sName") ?? string.Empty;
        }

        string characterName = string.Empty;
        string categoryId = string.Empty;
        if (record.TryGetProperty("_aSubCategory", out JsonElement subCategoryElement))
        {
            characterName = TryGetStringProperty(subCategoryElement, "_sName") ?? string.Empty;
            string? categoryUrl = TryGetStringProperty(subCategoryElement, "_sProfileUrl");
            categoryId = ExtractTrailingNumericId(categoryUrl);
        }

        string? previewUrl = null;
        if (record.TryGetProperty("_aPreviewMedia", out JsonElement previewMediaElement)
            && previewMediaElement.ValueKind == JsonValueKind.Object
            && previewMediaElement.TryGetProperty("_aImages", out JsonElement imagesElement)
            && imagesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement imageElement in imagesElement.EnumerateArray())
            {
                string? baseUrl = TryGetStringProperty(imageElement, "_sBaseUrl");
                string? fileName = TryGetStringProperty(imageElement, "_sFile220")
                    ?? TryGetStringProperty(imageElement, "_sFile530")
                    ?? TryGetStringProperty(imageElement, "_sFile100")
                    ?? TryGetStringProperty(imageElement, "_sFile");
                if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(fileName))
                {
                    previewUrl = $"{baseUrl}/{fileName}";
                    break;
                }
            }
        }

        long updatedEpoch = TryGetInt64Property(record, "_tsDateModified");
        if (updatedEpoch <= 0)
        {
            updatedEpoch = TryGetInt64Property(record, "_tsDateUpdated");
        }
        if (updatedEpoch <= 0)
        {
            updatedEpoch = TryGetInt64Property(record, "_tsDateAdded");
        }

        int likes = TryGetInt32Property(record, "_nLikeCount");
        int views = TryGetInt32Property(record, "_nViewCount");
        int downloads = TryGetInt32Property(record, "_nDownloadCount");
        DateTimeOffset updatedAt = FromGameBananaUnixTime(updatedEpoch);

        string resolvedCharacterName = ResolveOnlineCharacterName(characterName, title, useEndfieldMetadata);
        bool isCharacterNameInferred = IsGenericOnlineCharacterName(characterName)
            && !string.Equals(resolvedCharacterName, characterName, StringComparison.OrdinalIgnoreCase);
        return new OnlineModCard
        {
            ItemId = itemId,
            Title = title,
            CharacterName = resolvedCharacterName,
            CategoryId = categoryId,
            IsCharacterNameInferred = isCharacterNameInferred,
            RootCategoryName = string.IsNullOrWhiteSpace(rootCategoryName) ? "Skins" : rootCategoryName,
            Author = string.IsNullOrWhiteSpace(author) ? L("未知作者", "Unknown author") : author,
            AuthorId = authorId,
            Likes = likes,
            Views = views,
            Downloads = downloads,
            HotnessScore = CalculateOnlineHotness(likes, views, downloads),
            PreviewUrl = previewUrl,
            ProfileUrl = profileUrl,
            DownloadUrl = $"https://gamebanana.com/mods/download/{itemId}",
            FileSizeBytes = 0,
            HasUpdates = false,
            UpdatedAt = updatedAt
        };
    }

    private async Task<OnlineModCard?> FetchGameBananaModCardAsyncV2(
        int itemId, bool useEndfieldMetadata = false, CancellationToken cancellationToken = default)
    {
        GameBananaModMetadata metadata = await _gameBananaMetadataService.GetModAsync(itemId, cancellationToken);
        string category = string.IsNullOrWhiteSpace(metadata.CharacterName) ? L("未分类角色", "Uncategorized") : metadata.CharacterName;
        string character = ResolveOnlineCharacterName(category, metadata.Title, useEndfieldMetadata);
        OnlineDownloadCandidate? file = OnlineDownloadSelectionPolicy.SelectDefault(metadata.DownloadFiles);
        return new OnlineModCard
        {
            ItemId = itemId, Title = metadata.Title, CharacterName = character,
            CategoryId = metadata.CategoryId > 0 ? metadata.CategoryId.ToString(CultureInfo.InvariantCulture) : string.Empty,
            IsCharacterNameInferred = IsGenericOnlineCharacterName(category)
                && !string.Equals(character, category, StringComparison.OrdinalIgnoreCase),
            RootCategoryName = metadata.RootCategoryName,
            Author = string.IsNullOrWhiteSpace(metadata.Author) ? L("未知作者", "Unknown author") : metadata.Author,
            AuthorId = metadata.AuthorId,
            Likes = metadata.Likes, Views = metadata.Views, Downloads = metadata.Downloads,
            HotnessScore = CalculateOnlineHotness(metadata.Likes, metadata.Views, metadata.Downloads),
            PreviewUrl = metadata.PreviewUrl, ProfileUrl = metadata.ProfileUrl,
            DownloadUrl = file?.DownloadUrl ?? metadata.FallbackDownloadUrl,
            DownloadFileName = file?.FileName ?? string.Empty, DownloadFiles = metadata.DownloadFiles.ToList(),
            DownloadFileId = file?.FileId ?? string.Empty,
            FileSizeBytes = file?.FileSizeBytes ?? 0, HasUpdates = metadata.HasUpdates,
            UpdatedAt = FromGameBananaUnixTime(metadata.UpdatedEpoch)
        };
    }

    private string GetGameBananaDownloadMetricCacheFile(int itemId)
    {
        return Path.Combine(_onlineDetailsCachePath, $"downloads-{itemId}.json");
    }

    private async Task<int?> TryReadGameBananaDownloadMetricCacheAsync(int itemId)
    {
        CachedValue<GameBananaDownloadMetricCacheEntry>? sqliteMetric = _appDataStore.TryReadCache<GameBananaDownloadMetricCacheEntry>(
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
            _appDataStore.WriteCache("download-metric", itemId.ToString(CultureInfo.InvariantCulture), entry);
            await File.WriteAllTextAsync(
                GetGameBananaDownloadMetricCacheFile(itemId),
                JsonSerializer.Serialize(entry));
        }
        catch
        {
        }
    }

    /// <summary>One request per 40 Mods (the list API has no download counts at all).</summary>
    private async Task<Dictionary<int, int>> FetchGameBananaDownloadCountsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<int, int>();
        foreach (int[] batch in itemIds.Distinct().Chunk(GameBananaDownloadCounts.MaxBatch))
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(GameBananaDownloadCounts.BatchUrl(batch), cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new OnlineRateLimitException(response.Headers.RetryAfter?.Delta);
            response.EnsureSuccessStatusCode();
            foreach (var pair in GameBananaDownloadCounts.Parse(await response.Content.ReadAsStringAsync(cancellationToken), batch))
                counts[pair.Key] = pair.Value;
        }
        return counts;
    }

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
            mod.HotnessScore = CalculateOnlineHotness(mod.Likes, mod.Views, mod.Downloads);
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
        EndfieldCharacterInfo? character = IsEndfieldRepository(GetSelectedRepository())
            ? FindEndfieldCharacter(mod.CharacterName) : null;
        return OnlineBrowseStateAdapter.MatchesSearch(mod.Title, mod.CharacterName, mod.Author,
            searchText, character?.AllNames);
    }

    private static double CalculateOnlineHotness(int likes, int views, int downloads)
    {
        double downloadPart = Math.Log10(downloads + 1) * 0.55d;
        double likePart = Math.Log10(likes + 1) * 0.30d;
        double viewPart = Math.Log10(views + 1) * 0.15d;
        double weighted = downloadPart + likePart + viewPart;
        double normalized = Math.Min(10d, weighted * 2d);
        return Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
    }

    private static DateTimeOffset FromGameBananaUnixTime(long value)
    {
        if (value <= 0)
        {
            return DateTimeOffset.Now;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.Now;
        }
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return propertyElement.ValueKind switch
            {
                JsonValueKind.String => propertyElement.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => propertyElement.ToString(),
                _ => null
            };
        }

        return null;
    }

    private static long TryGetInt64Property(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return 0;
        }

        return TryReadInt64(propertyElement);
    }

    private static int TryGetInt32Property(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return 0;
        }

        long value = TryReadInt64(propertyElement);
        if (value > int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value < int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }

    private static long TryReadInt64(JsonElement element)
    {
        if (element.TryGetInt64(out long int64Value))
        {
            return int64Value;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double doubleValue))
        {
            return (long)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string? text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                string normalized = text.Replace(",", string.Empty).Trim();
                if (long.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out long parsedLong))
                {
                    return parsedLong;
                }

                if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedDouble))
                {
                    return (long)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);
                }
            }
        }

        return 0;
    }

    private static string ExtractTrailingNumericId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        Match match = Regex.Match(url, @"(?<id>\d+)/?$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : string.Empty;
    }

    private async void OnOnlinePrevPageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_onlineCurrentPage <= 1 || _isLoadingOnlineMods)
            {
                return;
            }

            ResetOnlineBrowsePosition();
            _onlineCurrentPage--;
            await LoadOnlineModsAsync(forceReload: false);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnOnlinePrevPageClicked", ex);
        }
    }

    private async void OnOnlineNextPageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_onlineCurrentPage >= _onlineTotalPages || _isLoadingOnlineMods)
            {
                return;
            }

            ResetOnlineBrowsePosition();
            _onlineCurrentPage++;
            await LoadOnlineModsAsync(forceReload: false);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnOnlineNextPageClicked", ex);
        }
    }

    private void OnOnlineSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _onlineSearchText = OnlineSearchTextBox.Text?.Trim() ?? string.Empty;
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
        CachedValue<OnlineCategoryPageCacheEntry>? sqliteEntry = _appDataStore.TryReadCache<OnlineCategoryPageCacheEntry>(
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
            _appDataStore.WriteCache("online-page", $"{categoryId}:{Math.Max(1, result.Page)}:{OnlineDisplayPageSize}", entry);
            string json = JsonSerializer.Serialize(entry);
            await File.WriteAllTextAsync(GetOnlineCategoryPageCacheFile(categoryId, result.Page), json);
        }
        catch
        {
        }
    }
}
