using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Text;

namespace ModFolderCopier.WinUI;

// Issue #129: top-level Mod categories and their direct subcategories sit above the results. The character
// category keeps its character rail. The category choice is remembered per repository.
public sealed partial class OnlinePage
{
    // The rail's entry for every Mod under the character top-level category; never a character name.
    private const string AllCharacterTopKey = "\u0001all-top";

    private bool _applyingOnlineCategoryBars;
    private string? _onlineCategorySelectionKey;
    // The category the last list request was for ("" for the characters), to notice when the categories arrive later.
    private string _requestedOnlineScope = string.Empty;

    private string SelectedOnlineTopId(OnlineCategoryIndex index)
    {
        string topId = _services.Online.Page.CategoryTopId;
        return index.TopCategories.Any(category => category.Id == topId) ? topId : index.CharacterTopId;
    }

    private string? SelectedOnlineSubcategoryId(OnlineCategoryIndex index, string topId)
    {
        string subId = _services.Online.Page.CategorySubId;
        return subId.Length > 0 && subId != index.RepositoryCategoryId
            && index.SubcategoriesOf(topId).Any(category => category.Id == subId) ? subId : null;
    }

    /// <summary>Whether the selected category uses the character rail.</summary>
    private bool IsOnlineCharacterView()
    {
        OnlineCategoryIndex? index = _services.OnlineDownloads.CurrentOnlineCategories();
        return index?.CharacterTop is null || (SelectedOnlineTopId(index) == index.CharacterTopId
            && SelectedOnlineSubcategoryId(index, index.CharacterTopId) is null);
    }

    /// <summary>"All" of the character top-level category, characters or not; only when it is not the repository category itself.</summary>
    private bool ShowsAllOfCharacterTop()
    {
        OnlineCategoryIndex? index = _services.OnlineDownloads.CurrentOnlineCategories();
        return _services.Online.Page.ShowAllOfCharacterTop && IsOnlineCharacterView()
            && index?.CharacterTop is not null && index.CharacterTopId != _services.OnlineDownloads.OnlineRepositoryCategoryId();
    }

    /// <summary>The category the list comes from when it is not the character list; null for the characters as before.</summary>
    private string? OnlineScopeCategoryId()
    {
        OnlineCategoryIndex? index = _services.OnlineDownloads.CurrentOnlineCategories();
        if (index is null) return null;
        if (ShowsAllOfCharacterTop()) return index.CharacterTopId;
        string topId = SelectedOnlineTopId(index);
        return SelectedOnlineSubcategoryId(index, topId)
            ?? (topId.Length > 0 && topId != index.CharacterTopId ? topId : null);
    }

    /// <summary>The name of the category shown instead of characters, for the status line; null in the character list.</summary>
    private string? OnlineScopeCategoryName() => _services.OnlineDownloads.CurrentOnlineCategories()?.Find(OnlineScopeCategoryId())?.Name;

    // Called when the browse session changes (another repository): its remembered category, if it still exists.
    private void RestoreOnlineCategorySelection(string? sessionKey)
    {
        _onlineCategorySelectionKey = sessionKey;
        OnlineCategorySelection? saved = sessionKey is not null
            && _services.Online.Preferences.CategorySelections.TryGetValue(sessionKey, out OnlineCategorySelection? selection) ? selection : null;
        _services.Online.Page.CategoryTopId = saved?.TopId ?? string.Empty;
        _services.Online.Page.CategorySubId = saved?.SubId ?? string.Empty;
        _services.Online.Page.ShowAllOfCharacterTop = saved?.AllOfCharacterTop ?? false;
        PopulateOnlineCategoryBars();
    }

    private void SaveOnlineCategorySelection()
    {
        if (_onlineCategorySelectionKey is null) return;
        _services.Online.Preferences.CategorySelections[_onlineCategorySelectionKey] = new OnlineCategorySelection
        {
            TopId = _services.Online.Page.CategoryTopId,
            SubId = _services.Online.Page.CategorySubId,
            AllOfCharacterTop = _services.Online.Page.ShowAllOfCharacterTop
        };
        _host.SaveShellConfig();
    }

    /// <summary>Reads the categories in the background; the bars appear when they arrive (OnOnlineCategoriesChanged).</summary>
    private async Task EnsureOnlineCategoriesAsync(bool forceReload = false)
    {
        try
        {
            if (!_services.OnlineSource.IsGameBanana(_services.OnlineSource.Source(_services.Repositories.Selected))) return;
            string categoryId = _services.OnlineDownloads.OnlineRepositoryCategoryId();
            OnlineCategoryIndex? index = await _services.OnlineCategories.EnsureAsync(categoryId, forceReload);
            if (index is null || _services.IsClosing || categoryId != _services.OnlineDownloads.OnlineRepositoryCategoryId()) return;
            await _services.OnlineCategories.EnsureSubcategoriesAsync(categoryId, SelectedOnlineTopId(index));
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Online categories", ex);
        }
    }

    private void OnOnlineCategoriesChanged(object? sender, string repositoryCategoryId)
    {
        if (_services.IsClosing || repositoryCategoryId != _services.OnlineDownloads.OnlineRepositoryCategoryId()) return;
        PopulateOnlineCategoryBars();
        PopulateOnlineCharacterOptions();
        _onlineCharacterStripSignature = null;
        RefreshOnlineCharacterAvatarStrip();
        UpdateOnlineResponsiveLayout(OnlineDetailsHost.ActualWidth);
        // A remembered category is only known once the categories are: load its list now if the characters were asked for.
        if ((OnlineScopeCategoryId() ?? string.Empty) != _requestedOnlineScope)
        {
            _lastLoadedOnlineConfigKey = null;
            _ = LoadOnlineModsAsync(forceReload: false);
        }
    }

    // Whether the categories are known and worth showing (more than one top-level category, or subcategories).
    private bool _onlineCategoryBarAvailable;
    private bool _onlineSubcategoriesShown;
    // The top-level tabs live in the toolbar's drop-down instead (a narrow page).
    private bool _onlineCategoryFolded;

    private void PopulateOnlineCategoryBars()
    {
        OnlineCategoryIndex? index = _services.OnlineDownloads.CurrentOnlineCategories();
        // A rebuilt row keeps the keyboard on the chosen chip when it was on a chip before.
        bool chipHadFocus = XamlRoot is not null
            && FocusManager.GetFocusedElement(XamlRoot) is ToggleButton { Parent: StackPanel parent } && parent == OnlineSubcategoryChips;
        _applyingOnlineCategoryBars = true;
        try
        {
            OnlineCategorySelectorBar.Items.Clear();
            OnlineSubcategoryChips.Children.Clear();
            OnlineCategoryDropDownMenu.Items.Clear();
            _onlineCategoryBarAvailable = false;
            _onlineSubcategoriesShown = false;
            if (index?.CharacterTop is null) return;

            string selectedTop = SelectedOnlineTopId(index);
            List<OnlineCategory> children = index.SubcategoriesOf(selectedTop);
            if (index.TopCategories.Count == 1 && children.Count == 0) return;
            _onlineCategoryBarAvailable = true;
            foreach (OnlineCategory top in index.TopCategories)
            {
                bool selected = top.Id == selectedTop;
                string hint = T("OnlineCategories.ItemCount", top.Name, top.ItemCount);
                var item = new SelectorBarItem { Text = top.Name, Tag = top.Id, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
                ToolTipService.SetToolTip(item, hint);
                OnlineCategorySelectorBar.Items.Add(item);
                if (selected) OnlineCategorySelectorBar.SelectedItem = item;

                string topId = top.Id;
                var menuItem = new RadioMenuFlyoutItem { Text = top.Name, GroupName = "OnlineTopCategory", IsChecked = selected };
                ToolTipService.SetToolTip(menuItem, hint);
                menuItem.Click += async (_, _) =>
                {
                    try { await SelectOnlineTopCategoryAsync(topId); }
                    catch (Exception ex) { _host.ReportUiActionFailure("OnOnlineCategoryMenuClicked", ex); }
                };
                OnlineCategoryDropDownMenu.Items.Add(menuItem);
            }
            string topName = index.Find(selectedTop)?.Name ?? string.Empty;
            OnlineCategoryDropDownTextBlock.Text = T("OnlineFilters.Category", topName);
            SetOnlineHint(OnlineCategoryDropDownButton, T("OnlineFilters.CategoryHint", topName));
            AutomationProperties.SetName(OnlineCategoryDropDownButton, T("OnlineFilters.Category", topName));

            if (children.Count == 0) return;
            string selectedSub = SelectedOnlineSubcategoryId(index, selectedTop)
                ?? (selectedTop == index.CharacterTopId && !ShowsAllOfCharacterTop()
                    && children.Any(child => child.Id == index.RepositoryCategoryId) ? index.RepositoryCategoryId : string.Empty);
            ToggleButton all = AddOnlineSubcategoryChip(T("OnlineCategories.All"), string.Empty,
                selectedTop == index.CharacterTopId ? T("OnlineCategories.AllOfTopHint", topName) : T("OnlineCategories.AllOfTop", topName), selectedSub.Length == 0);
            ToggleButton? chosen = selectedSub.Length == 0 ? all : null;
            foreach (OnlineCategory child in children)
            {
                ToggleButton chip = AddOnlineSubcategoryChip(child.Name, child.Id, T("OnlineCategories.ItemCount", child.Name, child.ItemCount), child.Id == selectedSub);
                if (child.Id == selectedSub) chosen = chip;
            }
            _onlineSubcategoriesShown = true;
            if (chipHadFocus && chosen is not null) DispatcherQueue.TryEnqueue(() => chosen.Focus(FocusState.Keyboard));
        }
        finally
        {
            _applyingOnlineCategoryBars = false;
            OnlineSubcategoryRow.Visibility = _onlineSubcategoriesShown ? Visibility.Visible : Visibility.Collapsed;
            UpdateOnlineCategoryFold();
        }
    }

    private ToggleButton AddOnlineSubcategoryChip(string text, string id, string hint, bool selected)
    {
        var chip = new ToggleButton
        {
            Content = text,
            Tag = id,
            IsChecked = selected,
            Style = (Style)Resources["OnlineCategoryChipStyle"]
        };
        ToolTipService.SetToolTip(chip, hint);
        chip.Checked += OnOnlineSubcategoryChipChecked;
        chip.Unchecked += OnOnlineSubcategoryChipUnchecked;
        OnlineSubcategoryChips.Children.Add(chip);
        return chip;
    }

    // Tabs in the bar on a wide page; on a narrow one the toolbar's drop-down, with the subcategory chips kept below.
    private void UpdateOnlineCategoryFold()
    {
        bool folded = _onlineNarrowLayout && _onlineCategoryBarAvailable;
        bool changed = folded != _onlineCategoryFolded;
        _onlineCategoryFolded = folded;
        OnlineCategoryTabsRow.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        OnlineCategoryBarPanel.Visibility = _onlineCategoryBarAvailable && (!folded || _onlineSubcategoriesShown)
            ? Visibility.Visible : Visibility.Collapsed;
        OnlineCategoryDropDownButton.Visibility = folded ? Visibility.Visible : Visibility.Collapsed;
        if (changed && OnlineResultsHost.ActualWidth > 0) UpdateOnlineToolbarLayout();
    }

    private async void OnOnlineCategorySelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        try
        {
            if (_applyingOnlineCategoryBars || sender.SelectedItem?.Tag is not string topId) return;
            await SelectOnlineTopCategoryAsync(topId);
        }
        catch (Exception ex) { _host.ReportUiActionFailure(nameof(OnOnlineCategorySelectionChanged), ex); }
    }

    private async Task SelectOnlineTopCategoryAsync(string topId)
    {
        OnlineCategoryIndex? index = _services.OnlineDownloads.CurrentOnlineCategories();
        string newTop = topId == index?.CharacterTopId ? string.Empty : topId;
        if (newTop == _services.Online.Page.CategoryTopId && (newTop.Length > 0 || !_services.Online.Page.ShowAllOfCharacterTop)) return;
        _services.Online.Page.CategoryTopId = newTop;
        _services.Online.Page.CategorySubId = string.Empty;
        _services.Online.Page.ShowAllOfCharacterTop = false;
        if (newTop.Length > 0) _services.Online.Page.CharacterFilter = string.Empty;
        await ApplyOnlineCategoryChangeAsync();
    }

    private async void OnOnlineSubcategoryChipChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_applyingOnlineCategoryBars || sender is not ToggleButton { Tag: string subId } chip) return;
            // One chosen chip at a time, like a tab.
            _applyingOnlineCategoryBars = true;
            try
            {
                foreach (ToggleButton other in OnlineSubcategoryChips.Children.OfType<ToggleButton>())
                    if (!ReferenceEquals(other, chip)) other.IsChecked = false;
            }
            finally { _applyingOnlineCategoryBars = false; }
            await SelectOnlineSubcategoryAsync(subId);
        }
        catch (Exception ex) { _host.ReportUiActionFailure(nameof(OnOnlineSubcategoryChipChecked), ex); }
    }

    // Clicking the chosen chip again keeps it chosen.
    private void OnOnlineSubcategoryChipUnchecked(object sender, RoutedEventArgs e)
    {
        if (_applyingOnlineCategoryBars || sender is not ToggleButton chip) return;
        _applyingOnlineCategoryBars = true;
        try { chip.IsChecked = true; }
        finally { _applyingOnlineCategoryBars = false; }
    }

    private async Task SelectOnlineSubcategoryAsync(string subId)
    {
        if (_services.OnlineDownloads.CurrentOnlineCategories() is not { } index) return;
        bool allOfTop = subId.Length == 0 && SelectedOnlineTopId(index) == index.CharacterTopId
            && index.CharacterTopId != index.RepositoryCategoryId;
        string newSub = subId == index.RepositoryCategoryId ? string.Empty : subId;
        if (newSub == _services.Online.Page.CategorySubId && allOfTop == _services.Online.Page.ShowAllOfCharacterTop) return;
        _services.Online.Page.CategorySubId = newSub;
        _services.Online.Page.ShowAllOfCharacterTop = allOfTop;
        _services.Online.Page.CharacterFilter = string.Empty;
        await ApplyOnlineCategoryChangeAsync();
    }

    // ‹ › appear only while a row overflows; they, the wheel and the keyboard scroll it.
    private static void UpdateOnlineScrollArrows(ScrollViewer viewer, Button left, Button right)
    {
        bool overflow = viewer.ScrollableWidth > 1;
        left.Visibility = overflow && viewer.HorizontalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
        right.Visibility = overflow && viewer.HorizontalOffset < viewer.ScrollableWidth - 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ScrollOnlineRow(ScrollViewer viewer, int direction)
        => viewer.ChangeView(Math.Clamp(viewer.HorizontalOffset + direction * Math.Max(80, viewer.ViewportWidth * 0.7), 0, viewer.ScrollableWidth), null, null);

    private void WireOnlineScrollRow(ScrollViewer viewer, FrameworkElement content, Button left, Button right)
    {
        void Update() => UpdateOnlineScrollArrows(viewer, left, right);
        viewer.ViewChanged += (_, _) => Update();
        viewer.SizeChanged += (_, _) => Update();
        content.SizeChanged += (_, _) => Update();
        // The rows scroll sideways only; a mouse wheel moves them too.
        viewer.PointerWheelChanged += (_, e) =>
        {
            if (viewer.ScrollableWidth <= 0) return;
            int delta = e.GetCurrentPoint(viewer).Properties.MouseWheelDelta;
            viewer.ChangeView(Math.Clamp(viewer.HorizontalOffset - delta, 0, viewer.ScrollableWidth), null, null, true);
            e.Handled = true;
        };
    }

    private void OnOnlineCategoryScrollLeftClicked(object sender, RoutedEventArgs e) => ScrollOnlineRow(OnlineCategoryScrollViewer, -1);
    private void OnOnlineCategoryScrollRightClicked(object sender, RoutedEventArgs e) => ScrollOnlineRow(OnlineCategoryScrollViewer, 1);
    private void OnOnlineSubcategoryScrollLeftClicked(object sender, RoutedEventArgs e) => ScrollOnlineRow(OnlineSubcategoryScrollViewer, -1);
    private void OnOnlineSubcategoryScrollRightClicked(object sender, RoutedEventArgs e) => ScrollOnlineRow(OnlineSubcategoryScrollViewer, 1);

    // A new category: first page, the rail shown or hidden, the bars and the list again.
    private async Task ApplyOnlineCategoryChangeAsync()
    {
        ResetOnlineBrowsePosition();
        _services.Online.Page.ResetPaging();
        _services.Online.Page.Mods.Clear();
        _lastLoadedOnlineConfigKey = null;
        _activeOnlineDetailMod = null;
        SaveOnlineCategorySelection();
        PopulateOnlineCategoryBars();
        PopulateOnlineCharacterOptions();
        _onlineCharacterStripSignature = null;
        RefreshOnlineCharacterAvatarStrip();
        UpdateOnlineResponsiveLayout(OnlineDetailsHost.ActualWidth);
        _animateOnlineCardsOnNextRefresh = true;
        await CloseOnlineDetailPaneAsync();
        await LoadOnlineModsAsync(forceReload: false);
    }

    private void InitializeOnlineCategoryBars()
    {
        AutomationProperties.SetName(OnlineCategorySelectorBar, T("OnlineCategories.BarName"));
        AutomationProperties.SetName(OnlineSubcategoryScrollViewer, T("OnlineCategories.SubBarName"));
        WireOnlineScrollRow(OnlineCategoryScrollViewer, OnlineCategorySelectorBar, OnlineCategoryScrollLeftButton, OnlineCategoryScrollRightButton);
        WireOnlineScrollRow(OnlineSubcategoryScrollViewer, OnlineSubcategoryChips, OnlineSubcategoryScrollLeftButton, OnlineSubcategoryScrollRightButton);
        _services.OnlineCategories.Changed += OnOnlineCategoriesChanged;
    }
}
