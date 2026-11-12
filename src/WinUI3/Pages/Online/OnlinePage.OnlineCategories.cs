using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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

    private string OnlineRepositoryCategoryId() => _services.OnlineSource.CategoryId(_services.Repositories.Selected);

    private OnlineCategoryIndex? CurrentOnlineCategories() => _services.OnlineCategories.Current(OnlineRepositoryCategoryId());

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
        OnlineCategoryIndex? index = CurrentOnlineCategories();
        return index?.CharacterTop is null || (SelectedOnlineTopId(index) == index.CharacterTopId
            && SelectedOnlineSubcategoryId(index, index.CharacterTopId) is null);
    }

    /// <summary>"All" of the character top-level category, characters or not; only when it is not the repository category itself.</summary>
    private bool ShowsAllOfCharacterTop()
    {
        OnlineCategoryIndex? index = CurrentOnlineCategories();
        return _services.Online.Page.ShowAllOfCharacterTop && IsOnlineCharacterView()
            && index?.CharacterTop is not null && index.CharacterTopId != OnlineRepositoryCategoryId();
    }

    /// <summary>The category the list comes from when it is not the character list; null for the characters as before.</summary>
    private string? OnlineScopeCategoryId()
    {
        OnlineCategoryIndex? index = CurrentOnlineCategories();
        if (index is null) return null;
        if (ShowsAllOfCharacterTop()) return index.CharacterTopId;
        string topId = SelectedOnlineTopId(index);
        return SelectedOnlineSubcategoryId(index, topId)
            ?? (topId.Length > 0 && topId != index.CharacterTopId ? topId : null);
    }

    /// <summary>The name of the category shown instead of characters, for the status line; null in the character list.</summary>
    private string? OnlineScopeCategoryName() => CurrentOnlineCategories()?.Find(OnlineScopeCategoryId())?.Name;

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
            string categoryId = OnlineRepositoryCategoryId();
            OnlineCategoryIndex? index = await _services.OnlineCategories.EnsureAsync(categoryId, forceReload);
            if (index is null || _services.IsClosing || categoryId != OnlineRepositoryCategoryId()) return;
            await _services.OnlineCategories.EnsureSubcategoriesAsync(categoryId, SelectedOnlineTopId(index));
        }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Online categories", ex);
        }
    }

    private void OnOnlineCategoriesChanged(object? sender, string repositoryCategoryId)
    {
        if (_services.IsClosing || repositoryCategoryId != OnlineRepositoryCategoryId()) return;
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

    private void PopulateOnlineCategoryBars()
    {
        OnlineCategoryIndex? index = CurrentOnlineCategories();
        _applyingOnlineCategoryBars = true;
        try
        {
            OnlineCategorySelectorBar.Items.Clear();
            OnlineSubcategorySelectorBar.Items.Clear();
            if (index?.CharacterTop is null)
            {
                OnlineCategoryBarPanel.Visibility = Visibility.Collapsed;
                return;
            }

            string selectedTop = SelectedOnlineTopId(index);
            List<OnlineCategory> children = index.SubcategoriesOf(selectedTop);
            if (index.TopCategories.Count == 1 && children.Count == 0)
            {
                OnlineCategoryBarPanel.Visibility = Visibility.Collapsed;
                return;
            }
            foreach (OnlineCategory top in index.TopCategories)
            {
                var item = new SelectorBarItem { Text = top.Name, Tag = top.Id };
                ToolTipService.SetToolTip(item, T("OnlineCategories.ItemCount", top.Name, top.ItemCount));
                OnlineCategorySelectorBar.Items.Add(item);
                if (top.Id == selectedTop) OnlineCategorySelectorBar.SelectedItem = item;
            }
            OnlineCategoryBarPanel.Visibility = Visibility.Visible;

            if (children.Count == 0)
            {
                OnlineSubcategoryScrollViewer.Visibility = Visibility.Collapsed;
                return;
            }
            var all = new SelectorBarItem { Text = T("OnlineCategories.All"), Tag = string.Empty };
            OnlineSubcategorySelectorBar.Items.Add(all);
            string selectedSub = SelectedOnlineSubcategoryId(index, selectedTop)
                ?? (selectedTop == index.CharacterTopId && !ShowsAllOfCharacterTop()
                    && children.Any(child => child.Id == index.RepositoryCategoryId) ? index.RepositoryCategoryId : string.Empty);
            SelectorBarItem selectedItem = all;
            foreach (OnlineCategory child in children)
            {
                var item = new SelectorBarItem { Text = child.Name, Tag = child.Id };
                ToolTipService.SetToolTip(item, T("OnlineCategories.ItemCount", child.Name, child.ItemCount));
                OnlineSubcategorySelectorBar.Items.Add(item);
                if (child.Id == selectedSub) selectedItem = item;
            }
            OnlineSubcategorySelectorBar.SelectedItem = selectedItem;
            OnlineSubcategoryScrollViewer.Visibility = Visibility.Visible;
        }
        finally { _applyingOnlineCategoryBars = false; }
    }

    private async void OnOnlineCategorySelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        try
        {
            if (_applyingOnlineCategoryBars || sender.SelectedItem?.Tag is not string topId) return;
            OnlineCategoryIndex? index = CurrentOnlineCategories();
            string newTop = topId == index?.CharacterTopId ? string.Empty : topId;
            if (newTop == _services.Online.Page.CategoryTopId && (newTop.Length > 0 || !_services.Online.Page.ShowAllOfCharacterTop)) return;
            _services.Online.Page.CategoryTopId = newTop;
            _services.Online.Page.CategorySubId = string.Empty;
            _services.Online.Page.ShowAllOfCharacterTop = false;
            if (newTop.Length > 0) _services.Online.Page.CharacterFilter = string.Empty;
            await ApplyOnlineCategoryChangeAsync();
        }
        catch (Exception ex) { _host.ReportUiActionFailure(nameof(OnOnlineCategorySelectionChanged), ex); }
    }

    private async void OnOnlineSubcategorySelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        try
        {
            if (_applyingOnlineCategoryBars || sender.SelectedItem?.Tag is not string subId || CurrentOnlineCategories() is not { } index) return;
            bool allOfTop = subId.Length == 0 && SelectedOnlineTopId(index) == index.CharacterTopId
                && index.CharacterTopId != index.RepositoryCategoryId;
            string newSub = subId == index.RepositoryCategoryId ? string.Empty : subId;
            if (newSub == _services.Online.Page.CategorySubId && allOfTop == _services.Online.Page.ShowAllOfCharacterTop) return;
            _services.Online.Page.CategorySubId = newSub;
            _services.Online.Page.ShowAllOfCharacterTop = allOfTop;
            _services.Online.Page.CharacterFilter = string.Empty;
            await ApplyOnlineCategoryChangeAsync();
        }
        catch (Exception ex) { _host.ReportUiActionFailure(nameof(OnOnlineSubcategorySelectionChanged), ex); }
    }

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

    /// <summary>The category a Mod outside the characters goes to (its folder name before it is made safe); null for a character Mod.</summary>
    private string? GetOnlineCategoryFolderName(OnlineModCard mod)
        => OnlineCategoryPlacement.CategoryName(CurrentOnlineCategories(), mod.RootCategoryName, mod.CategoryId, mod.CharacterName);

    private string CategoryFolderHint(CharacterFolderPlan plan, string category) => plan.Action switch
    {
        CharacterFolderAction.UseExisting => T("OnlineCategories.FolderRecognized", category, Path.GetFileName(plan.Path)),
        CharacterFolderAction.Create => T("OnlineCategories.FolderCreate", category, plan.Path),
        _ => T("OnlineCategories.FolderAsk", category)
    };

    private void InitializeOnlineCategoryBars()
    {
        AutomationProperties.SetName(OnlineCategorySelectorBar, T("OnlineCategories.BarName"));
        AutomationProperties.SetName(OnlineSubcategorySelectorBar, T("OnlineCategories.SubBarName"));
        _services.OnlineCategories.Changed += OnOnlineCategoriesChanged;
    }
}
