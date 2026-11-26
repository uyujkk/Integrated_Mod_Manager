using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
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

    private bool IsSavedOnlineMod(OnlineModCard mod) => _services.Online.Preferences.SavedMods.Any(item =>
        item.RepositoryId == _services.Repositories.Selected?.Id
        && string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), _services.Repositories.Selected?.SourcePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
        && OnlineDownloadService.GetOnlineDownloadIdentity(item.Mod) == OnlineDownloadService.GetOnlineDownloadIdentity(mod));

    private void ToggleSavedOnlineMod(OnlineModCard mod)
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        if (repository is null) return;
        bool remove = IsSavedOnlineMod(mod);
        _services.Online.Preferences.SavedMods.RemoveAll(item => item.RepositoryId == repository.Id
            && string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), repository.SourcePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            && OnlineDownloadService.GetOnlineDownloadIdentity(item.Mod) == OnlineDownloadService.GetOnlineDownloadIdentity(mod));
        if (!remove) _services.Online.Preferences.SavedMods.Insert(0, new OnlineSavedMod(repository.Id, repository.SourcePath, mod));
        _services.Online.Preferences.SavedMods = _services.Online.Preferences.SavedMods.Take(100).ToList();
        _host.SaveShellConfig();
        ShowAppNotification(remove ? T("OnlineLibrary.RemovedFromSaved") : T("OnlineLibrary.Saved"));
        RefreshOnlineCardStates(refreshInstalled: false);
        UpdateOnlineDetailSavedButton();
    }

    // The card's "save for later" button: a clock while not saved, a check mark once saved; the tooltip says what a
    // click does.
    private void ApplyOnlineSavedState(OnlineModTileViewModel model)
    {
        bool saved = IsSavedOnlineMod(model.Mod);
        model.SavedGlyph = saved ? "\uE73E" : "\uE823";
        model.SavedToolTip = saved ? T("OnlineLibrary.RemoveFromSaved") : T("OnlineLibrary.SaveForLater");
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
        var favorite = new MenuFlyoutItem { Text = IsSavedAuthor(mod) ? T("OnlineLibrary.UnsaveAuthor") : T("OnlineLibrary.SaveAuthor"), IsEnabled = mod.AuthorId > 0 };
        favorite.Click += (_, _) => ToggleSavedAuthor(mod);
        menu.Items.Add(favorite);
        menu.Opening += (_, _) =>
        {
            save.Text = IsSavedOnlineMod(mod) ? T("OnlineLibrary.RemoveFromSaved") : T("OnlineLibrary.SaveForLater");
            favorite.Text = IsSavedAuthor(mod) ? T("OnlineLibrary.UnsaveAuthor") : T("OnlineLibrary.SaveAuthor");
        };
        return menu;
    }

    // Saved authors belong to the current repository's category.
    private bool IsSavedAuthor(OnlineModCard mod) => mod.AuthorId > 0 && _services.Online.Preferences.Authors.Any(item => item.Id == mod.AuthorId
        && item.CategoryId == _services.OnlineSource.CategoryId(_services.Repositories.Selected));

    private void ToggleSavedAuthor(OnlineModCard mod)
    {
        if (mod.AuthorId <= 0) return;
        bool saved = IsSavedAuthor(mod);
        string category = _services.OnlineSource.CategoryId(_services.Repositories.Selected);
        _services.Online.Preferences.Authors.RemoveAll(item => item.Id == mod.AuthorId && item.CategoryId == category);
        if (!saved) _services.Online.Preferences.Authors.Insert(0, new OnlineAuthorBookmark(mod.AuthorId, mod.Author, category));
        _services.Online.Preferences.Authors = _services.Online.Preferences.Authors.Take(100).ToList();
        _host.SaveShellConfig();
        UpdateOnlineDetailAuthorButtons();
    }

    private async void OnOnlineSavedItemsClicked(object sender, RoutedEventArgs e)
    {
        // Items removed in the dialog update the cards' buttons when it closes.
        try { await ShowOnlineLibraryAsync(); RefreshOnlineCardStates(refreshInstalled: false); UpdateOnlineDetailSavedButton(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _host.LogApplicationIssue("Author and saved workspace", ex);
            await _services.OnlineDownloads.ReportOnlineActionErrorAsync(ex.Message, T("OnlineLibrary.CannotCompleteAction"), ex);
        }
    }
}

