using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// What narrows the Online list, shown above the results: one removable chip per filter (category, search, character,
// author, the Show filter and a sort other than the default) and "Clear all", which resets every one of them.
public sealed partial class OnlinePage
{
    private const OnlineSortMode DefaultOnlineSortMode = OnlineSortMode.Hotness;

    private sealed record OnlineFilterChip(string Kind, string Label);

    private string? _onlineFilterChipSignature;

    // A tooltip, also given to screen readers as the element's help text.
    private static void SetOnlineHint(DependencyObject element, string hint)
    {
        ToolTipService.SetToolTip(element, hint);
        AutomationProperties.SetHelpText(element, hint);
    }

    private List<OnlineFilterChip> CurrentOnlineFilterChips()
    {
        var chips = new List<OnlineFilterChip>();
        if (OnlineCategoryChipLabel() is { } category) chips.Add(new("Category", T("OnlineFilters.Category", category)));
        if (_services.Online.Page.CharacterFilter.Length > 0)
            chips.Add(new("Character", T("OnlineFilters.Character", OnlineCharacterLabel(_services.Online.Page.CharacterFilter))));
        if (_services.Online.Page.AuthorId > 0) chips.Add(new("Author", T("OnlineFilters.Author", _services.Online.Page.AuthorName)));
        if (_services.Online.Page.SearchText.Length > 0) chips.Add(new("Search", T("OnlineFilters.Search", _services.Online.Page.SearchText)));
        if (_services.Online.Page.AuthorWorkFilter > 0) chips.Add(new("Show", T("OnlineFilters.ShowOnly", OnlineWorkFilterName(_services.Online.Page.AuthorWorkFilter))));
        if (_services.Online.Page.SortMode != DefaultOnlineSortMode) chips.Add(new("Sort", T("OnlineFilters.Sort", OnlineSortName(_services.Online.Page.SortMode))));
        return chips;
    }

    /// <summary>"Top › Sub" when the list is not the default character view; null when it is.</summary>
    private string? OnlineCategoryChipLabel()
    {
        if (OnlineScopeCategoryId() is null || _services.OnlineDownloads.CurrentOnlineCategories() is not { } index) return null;
        string topId = SelectedOnlineTopId(index);
        string top = index.Find(topId)?.Name ?? string.Empty;
        string? sub = SelectedOnlineSubcategoryId(index, topId) is { } subId ? index.Find(subId)?.Name
            : ShowsAllOfCharacterTop() ? T("OnlineCategories.All") : null;
        return sub is null ? top : T("OnlineFilters.CategoryPath", top, sub);
    }

    private string OnlineCharacterLabel(string key) => OnlineCharacterComboBox.Items.OfType<ComboBoxItem>()
        .FirstOrDefault(item => string.Equals(item.Tag as string, key, StringComparison.OrdinalIgnoreCase))?.Content as string ?? key;

    private string OnlineWorkFilterName(int filter) => filter switch
    {
        1 => T("OnlineLibrary.InstalledOnPage"),
        2 => T("OnlineLibrary.SavedOnPage"),
        _ => T("OnlineLibrary.AllOnPage")
    };

    private string OnlineSortName(OnlineSortMode mode) => OnlineSortComboBox.Items.OfType<ComboBoxItem>()
        .FirstOrDefault(item => item.Tag is OnlineSortMode sort && sort == mode)?.Content as string ?? mode.ToString();

    private void RefreshOnlineFilterSummary(int matches, int loaded)
    {
        List<OnlineFilterChip> chips = CurrentOnlineFilterChips();
        OnlineFilterSummaryTextBlock.Text = T("OnlineLibrary.ThisPageMatches", matches, loaded);
        // The count's hint says what the search covers: this page only, and an author's Mods across pages.
        SetOnlineHint(OnlineFilterSummaryTextBlock, OnlineFilterSummaryTextBlock.Text
            + (_services.Online.Page.AuthorId > 0 ? T("OnlineLibrary.AuthorAuthorPagesTextFiltersThis", _services.Online.Page.AuthorName)
                : T("OnlineLibrary.SearchFiltersThisPageOnlyClick")));
        string signature = string.Join("\n", chips.Select(chip => chip.Kind + "\t" + chip.Label));
        if (signature != _onlineFilterChipSignature)
        {
            _onlineFilterChipSignature = signature;
            // The chips go before "Clear all", which stays the last child.
            for (int i = OnlineFilterChipsPanel.Children.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(OnlineFilterChipsPanel.Children[i], ClearOnlineFiltersButton)) OnlineFilterChipsPanel.Children.RemoveAt(i);
            for (int i = 0; i < chips.Count; i++) OnlineFilterChipsPanel.Children.Insert(i, CreateOnlineFilterChip(chips[i]));
        }
        OnlineActiveFiltersPanel.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearOnlineFiltersButton.IsEnabled = chips.Count > 0;
        ApplyOnlineToolbarTexts();
    }

    private Button CreateOnlineFilterChip(OnlineFilterChip chip)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new TextBlock { Text = chip.Label, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new FontIcon { Glyph = "", FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Tag = chip.Kind, Style = (Style)Resources["OnlineFilterChipStyle"] };
        string name = T("OnlineFilters.Remove", chip.Label);
        AutomationProperties.SetAutomationId(button, "OnlineFilterChip_" + chip.Kind);
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += OnOnlineFilterChipClicked;
        return button;
    }

    private async void OnOnlineFilterChipClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string kind }) return;
            UiCrashDiagnostics.RecordAction("Remove online filter " + kind);
            await RemoveOnlineFilterAsync(kind);
            // The chip is gone; the keyboard goes on to the next one, or back to the search box.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (OnlineFilterChipsPanel.Children.OfType<Button>().FirstOrDefault(child => !ReferenceEquals(child, ClearOnlineFiltersButton)) is { } next
                    && OnlineActiveFiltersPanel.Visibility == Visibility.Visible) next.Focus(FocusState.Programmatic);
                else OnlineSearchTextBox.Focus(FocusState.Programmatic);
            });
        }
        catch (Exception ex) { _host.ReportUiActionFailure(nameof(OnOnlineFilterChipClicked), ex); }
    }

    // Each chip undoes only its own filter, the way its control would.
    private async Task RemoveOnlineFilterAsync(string kind)
    {
        switch (kind)
        {
            case "Category":
                await RemoveOnlineCategoryFilterAsync();
                break;
            case "Character":
                await SelectOnlineCharacterAsync(string.Empty);
                break;
            case "Author":
                await ClearOnlineAuthorAsync();
                break;
            case "Search":
                OnlineSearchTextBox.Text = string.Empty;
                break;
            case "Show":
                _services.Online.Page.AuthorWorkFilter = 0;
                ResetOnlineBrowsePosition();
                RefreshOnlinePaneV2();
                _host.SaveShellConfig();
                break;
            case "Sort":
                OnlineSortComboBox.SelectedItem = OnlineSortComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag is OnlineSortMode sort && sort == DefaultOnlineSortMode);
                break;
        }
    }

    // A subcategory of another top-level category goes back to that category's "All"; anything else goes back to the
    // default character view.
    private async Task RemoveOnlineCategoryFilterAsync()
    {
        if (_services.OnlineDownloads.CurrentOnlineCategories() is { } index)
        {
            string topId = SelectedOnlineTopId(index);
            if (topId != index.CharacterTopId && SelectedOnlineSubcategoryId(index, topId) is not null)
            {
                await SelectOnlineSubcategoryAsync(string.Empty);
                return;
            }
        }
        _services.Online.Page.CategoryTopId = string.Empty;
        _services.Online.Page.CategorySubId = string.Empty;
        _services.Online.Page.ShowAllOfCharacterTop = false;
        await ApplyOnlineCategoryChangeAsync();
    }

    private async Task ClearOnlineAuthorAsync()
    {
        ResetOnlineBrowsePosition();
        CancelOnlineModListRequest();
        _services.Online.Page.AuthorId = 0;
        _services.Online.Page.AuthorName = string.Empty;
        _services.Online.Page.CurrentPage = 1;
        _lastLoadedOnlineConfigKey = null;
        _services.Online.Page.Mods.Clear();
        await CloseOnlineDetailPaneAsync();
        await LoadOnlineModsAsync(forceReload: false);
    }

    private async void OnClearOnlineFiltersClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await ClearAllOnlineFiltersAsync();
            DispatcherQueue.TryEnqueue(() => OnlineSearchTextBox.Focus(FocusState.Programmatic));
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnClearOnlineFiltersClicked", ex);
        }
    }

    // Everything a chip shows: search, Show filter, author, character, category and sort; one list request at most.
    private async Task ClearAllOnlineFiltersAsync()
    {
        UiCrashDiagnostics.RecordAction("Clear online filters");
        OnlinePageViewModel page = _services.Online.Page;
        ResetOnlineBrowsePosition();
        bool categoryChanged = page.CategoryTopId.Length > 0 || page.CategorySubId.Length > 0 || page.ShowAllOfCharacterTop;
        bool reload = categoryChanged || page.AuthorId > 0 || page.CharacterFilter.Length > 0
            || OnlineBrowseOrder.ServerSort(page.SortMode) != OnlineBrowseOrder.ServerSort(DefaultOnlineSortMode);
        page.AuthorWorkFilter = 0;
        page.AuthorId = 0;
        page.AuthorName = string.Empty;
        page.CharacterFilter = string.Empty;
        page.CategoryTopId = string.Empty;
        page.CategorySubId = string.Empty;
        page.ShowAllOfCharacterTop = false;
        page.SortMode = DefaultOnlineSortMode;
        _restoringBrowseControls = _resettingOnlineFilters = true;
        try
        {
            OnlineSearchTextBox.Text = string.Empty;
            page.SearchText = string.Empty;
            OnlineSortComboBox.SelectedItem = OnlineSortComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is OnlineSortMode sort && sort == DefaultOnlineSortMode);
        }
        finally { _restoringBrowseControls = _resettingOnlineFilters = false; }

        if (categoryChanged) SaveOnlineCategorySelection();
        PopulateOnlineCategoryBars();
        _onlineCharacterStripSignature = null;
        PopulateOnlineCharacterOptions();
        RefreshOnlineCharacterAvatarStrip();
        UpdateOnlineResponsiveLayout(OnlineDetailsHost.ActualWidth);
        _host.SaveShellConfig();
        if (!reload)
        {
            RefreshOnlinePaneV2();
            return;
        }
        CancelOnlineModListRequest();
        page.ResetPaging();
        _lastLoadedOnlineConfigKey = null;
        page.Mods.Clear();
        _activeOnlineDetailMod = null;
        _animateOnlineCardsOnNextRefresh = true;
        await CloseOnlineDetailPaneAsync();
        await LoadOnlineModsAsync(forceReload: false);
    }

    // Labels, names and tooltips that say what each toolbar button does and, for the filters, what is in use now.
    private void ApplyOnlineToolbarTexts()
    {
        OnlinePageViewModel page = _services.Online.Page;
        string show = OnlineWorkFilterName(page.AuthorWorkFilter);
        OnlineWorkFilterTextBlock.Text = T("OnlineFilters.ShowLabel", show);
        AutomationProperties.SetName(OnlineWorkFilterButton, T("OnlineFilters.ShowLabel", show));
        SetOnlineHint(OnlineWorkFilterButton, T("OnlineFilters.ShowHint", show));
        Style workStyle = (Style)Application.Current.Resources[page.AuthorWorkFilter > 0 ? "PrimaryButtonStyle" : "SecondaryButtonStyle"];
        if (!ReferenceEquals(OnlineWorkFilterButton.Style, workStyle)) OnlineWorkFilterButton.Style = workStyle;

        OnlineSavedItemsTextBlock.Text = T("OnlineFilters.SavedLabel");
        AutomationProperties.SetName(OnlineSavedItemsButton, T("OnlineLibrary.AuthorsAndSavedMods"));
        SetOnlineHint(OnlineSavedItemsButton, T("OnlineLibrary.AuthorsAndSavedModsFindAuthors"));
        OnlineDownloadOptionsTextBlock.Text = T("Common.DownloadOptions");
        AutomationProperties.SetName(OnlineDownloadOptionsButton, T("Common.DownloadOptions"));
        SetOnlineHint(OnlineDownloadOptionsButton, T("OnlineFilters.DownloadOptionsHint"));

        string sort = OnlineSortName(page.SortMode);
        SetOnlineHint(OnlineSortComboBox, T("OnlineFilters.SortHint", sort));
        OnlineSortChangedDot.Visibility = page.SortMode != DefaultOnlineSortMode ? Visibility.Visible : Visibility.Collapsed;

        ClearOnlineFiltersButton.Content = T("OnlineFilters.ClearAll");
        AutomationProperties.SetName(ClearOnlineFiltersButton, T("OnlineFilters.ClearAll"));
        SetOnlineHint(ClearOnlineFiltersButton, T("OnlineFilters.ClearAllHint"));
        AutomationProperties.SetName(OnlineActiveFiltersPanel, T("OnlineFilters.ActiveName"));
        AutomationProperties.SetName(OnlineSearchTextBox, T("OnlineLibrary.SearchThisPageByCharacterMod"));

        SetOnlineHint(OnlinePrevPageButton, page.HasPreviousPage
            ? T("OnlineFilters.PreviousPageHint", page.CurrentPage - 1, page.TotalPages) : T("Localization.PreviousPage"));
        SetOnlineHint(OnlineNextPageButton, page.HasNextPage
            ? T("OnlineFilters.NextPageHint", page.CurrentPage + 1, page.TotalPages) : T("Localization.NextPage"));
        SetOnlineHint(RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        SetOnlineHint(OnlineListLayoutButton, T("OnlineCards.ListView"));
        SetOnlineHint(OnlineGridLayoutButton, T("OnlineCards.GridView"));

        foreach ((Button left, Button right) in new[]
                 {
                     (OnlineCategoryScrollLeftButton, OnlineCategoryScrollRightButton),
                     (OnlineSubcategoryScrollLeftButton, OnlineSubcategoryScrollRightButton)
                 })
        {
            AutomationProperties.SetName(left, T("OnlineFilters.ScrollLeft"));
            ToolTipService.SetToolTip(left, T("OnlineFilters.ScrollLeft"));
            AutomationProperties.SetName(right, T("OnlineFilters.ScrollRight"));
            ToolTipService.SetToolTip(right, T("OnlineFilters.ScrollRight"));
        }
    }
}
