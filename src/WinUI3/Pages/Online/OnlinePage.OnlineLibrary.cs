using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private bool _resettingOnlineFilters;

    private string OnlineAuthorKey => _services.Online.Page.AuthorId > 0 ? $"|author={_services.Online.Page.AuthorId}" : string.Empty;

    private async Task BrowseOnlineAuthorAsync(int id, string name)
    {
        UiCrashDiagnostics.RecordAction("Author browse");
        if (id <= 0) { OnlineSearchTextBox.Text = $"author:{name}"; return; }
        string category = _services.OnlineSource.CategoryId(_services.Repositories.Selected);
        _services.Online.Preferences.RecentAuthors.RemoveAll(item => item.Id == id && item.CategoryId == category);
        _services.Online.Preferences.RecentAuthors.Insert(0, new OnlineAuthorVisit(id, name, category, DateTimeOffset.UtcNow));
        _services.Online.Preferences.RecentAuthors = _services.Online.Preferences.RecentAuthors.Take(100).ToList();
        ResetOnlineBrowsePosition();
        CancelOnlineModListRequest();
        _services.Online.Page.AuthorId = id;
        _services.Online.Page.AuthorName = name;
        _services.Online.Page.CurrentPage = 1;
        _lastLoadedOnlineConfigKey = null;
        _services.Online.Page.Mods.Clear();
        _resettingOnlineFilters = true;
        try { OnlineSearchTextBox.Text = string.Empty; _services.Online.Page.SearchText = string.Empty; }
        finally { _resettingOnlineFilters = false; }
        await CloseOnlineDetailPaneAsync();
        await LoadOnlineModsAsync(forceReload: false);
    }

    private async void OnClearOnlineFiltersClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            UiCrashDiagnostics.RecordAction("Clear online filters");
            ResetOnlineBrowsePosition();
            _services.Online.Page.AuthorWorkFilter = 0;
            bool reload = _services.Online.Page.AuthorId > 0 || _services.Online.Page.CharacterFilter.Length > 0;
            _services.Online.Page.AuthorId = 0;
            _services.Online.Page.AuthorName = string.Empty;
            _services.Online.Page.CharacterFilter = string.Empty;
            _onlineCharacterStripSignature = null;
            PopulateOnlineCharacterOptions();
            _resettingOnlineFilters = true;
            try { OnlineSearchTextBox.Text = string.Empty; _services.Online.Page.SearchText = string.Empty; }
            finally { _resettingOnlineFilters = false; }
            if (reload)
            {
                _services.Online.Page.CurrentPage = 1;
                _lastLoadedOnlineConfigKey = null;
                _services.Online.Page.Mods.Clear();
                await LoadOnlineModsAsync(forceReload: false);
            }
            else RefreshOnlinePaneV2();
        }
        catch (Exception ex)
        {
            _host.ReportUiActionFailure("OnClearOnlineFiltersClicked", ex);
        }
    }

    private void RefreshOnlineFilterSummary(int matches, int loaded)
    {
        OnlineFilterSummaryTextBlock.Text = T("OnlineLibrary.ThisPageMatches", matches, loaded)
            + (_services.Online.Page.AuthorId > 0 ? T("OnlineLibrary.AuthorAuthorPagesTextFiltersThis", _services.Online.Page.AuthorName)
                : T("OnlineLibrary.SearchFiltersThisPageOnlyClick"));
        AutomationProperties.SetName(OnlineSearchTextBox, T("OnlineLibrary.SearchThisPageByCharacterMod"));
        ToolTipService.SetToolTip(OnlineFilterSummaryTextBlock, OnlineFilterSummaryTextBlock.Text);
        ClearOnlineFiltersButton.Content = T("OnlineLibrary.ClearFilters");
        ClearOnlineFiltersButton.IsEnabled = _services.Online.Page.AuthorId > 0 || _services.Online.Page.SearchText.Length > 0 || _services.Online.Page.CharacterFilter.Length > 0 || _services.Online.Page.AuthorWorkFilter > 0;
        OnlineWorkFilterButton.Content = _services.Online.Page.AuthorWorkFilter switch { 1 => T("OnlineLibrary.InstalledOnPage"), 2 => T("OnlineLibrary.SavedOnPage"), _ => T("OnlineLibrary.AllOnPage") };
        ToolTipService.SetToolTip(OnlineSavedItemsButton, T("OnlineLibrary.AuthorsAndSavedModsFindAuthors"));
        AutomationProperties.SetName(OnlineSavedItemsButton, T("OnlineLibrary.AuthorsAndSavedMods"));
        ToolTipService.SetToolTip(OnlineDownloadOptionsButton, T("Common.DownloadOptions"));
        AutomationProperties.SetName(OnlineDownloadOptionsButton, T("Common.DownloadOptions"));
    }

    private bool IsSavedOnlineMod(OnlineModCard mod) => _services.Online.Preferences.SavedMods.Any(item =>
        item.RepositoryId == _services.Repositories.Selected?.Id
        && string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), _services.Repositories.Selected?.SourcePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
        && GetOnlineDownloadIdentity(item.Mod) == GetOnlineDownloadIdentity(mod));

    private void ToggleSavedOnlineMod(OnlineModCard mod)
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null) return;
        bool remove = IsSavedOnlineMod(mod);
        _services.Online.Preferences.SavedMods.RemoveAll(item => item.RepositoryId == repository.Id
            && string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), repository.SourcePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            && GetOnlineDownloadIdentity(item.Mod) == GetOnlineDownloadIdentity(mod));
        if (!remove) _services.Online.Preferences.SavedMods.Insert(0, new OnlineSavedMod(repository.Id, repository.SourcePath, mod));
        _services.Online.Preferences.SavedMods = _services.Online.Preferences.SavedMods.Take(100).ToList();
        _host.SaveShellConfig();
        ShowAppNotification(remove ? T("OnlineLibrary.RemovedFromSaved") : T("OnlineLibrary.Saved"));
    }

    private MenuFlyout CreateOnlineCardMenu(OnlineModCard mod)
    {
        var menu = new MenuFlyout();
        var save = new MenuFlyoutItem { Text = IsSavedOnlineMod(mod) ? T("OnlineLibrary.RemoveFromSaved") : T("OnlineLibrary.SaveForLater") };
        save.Click += (_, _) => ToggleSavedOnlineMod(mod);
        menu.Items.Add(save);
        var author = new MenuFlyoutItem { Text = T("OnlineLibrary.BrowseAuthorAllPages"), IsEnabled = mod.AuthorId > 0 };
        author.Click += async (_, _) => await _host.RunUiActionAsync("BrowseOnlineAuthorAsync", () => BrowseOnlineAuthorAsync(mod.AuthorId, mod.Author));
        menu.Items.Add(author);
        bool savedAuthor = _services.Online.Preferences.Authors.Any(item => item.Id == mod.AuthorId
            && item.CategoryId == _services.OnlineSource.CategoryId(_services.Repositories.Selected));
        var favorite = new MenuFlyoutItem { Text = savedAuthor ? T("OnlineLibrary.UnsaveAuthor") : T("OnlineLibrary.SaveAuthor"), IsEnabled = mod.AuthorId > 0 };
        favorite.Click += (_, _) =>
        {
            string category = _services.OnlineSource.CategoryId(_services.Repositories.Selected);
            _services.Online.Preferences.Authors.RemoveAll(item => item.Id == mod.AuthorId && item.CategoryId == category);
            if (!savedAuthor) _services.Online.Preferences.Authors.Insert(0, new OnlineAuthorBookmark(mod.AuthorId, mod.Author, category));
            _services.Online.Preferences.Authors = _services.Online.Preferences.Authors.Take(100).ToList();
            _host.SaveShellConfig();
        };
        menu.Items.Add(favorite);
        menu.Opening += (_, _) =>
        {
            save.Text = IsSavedOnlineMod(mod) ? T("OnlineLibrary.RemoveFromSaved") : T("OnlineLibrary.SaveForLater");
            savedAuthor = _services.Online.Preferences.Authors.Any(item => item.Id == mod.AuthorId
                && item.CategoryId == _services.OnlineSource.CategoryId(_services.Repositories.Selected));
            favorite.Text = savedAuthor ? T("OnlineLibrary.UnsaveAuthor") : T("OnlineLibrary.SaveAuthor");
        };
        return menu;
    }

    private async void OnOnlineSavedItemsClicked(object sender, RoutedEventArgs e)
    {
        try { await ShowOnlineLibraryAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Author and saved workspace", ex);
            await ReportOnlineActionErrorAsync(ex.Message, T("OnlineLibrary.CannotCompleteAction"), ex);
        }
    }
}

