using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private OnlineWorkspacePreferences _onlinePreferences = new();
    private int _onlineAuthorId;
    private string _onlineAuthorName = string.Empty;
    private bool _resettingOnlineFilters;

    private string OnlineAuthorKey => _onlineAuthorId > 0 ? $"|author={_onlineAuthorId}" : string.Empty;

    private async Task BrowseOnlineAuthorAsync(int id, string name)
    {
        UiCrashDiagnostics.RecordAction("Author browse");
        if (id <= 0) { OnlineSearchTextBox.Text = $"author:{name}"; return; }
        string category = GetEffectiveOnlineCategoryId(GetSelectedRepository());
        _onlinePreferences.RecentAuthors.RemoveAll(item => item.Id == id && item.CategoryId == category);
        _onlinePreferences.RecentAuthors.Insert(0, new OnlineAuthorVisit(id, name, category, DateTimeOffset.UtcNow));
        _onlinePreferences.RecentAuthors = _onlinePreferences.RecentAuthors.Take(100).ToList();
        ResetOnlineBrowsePosition();
        CancelOnlineModListRequest();
        _onlineAuthorId = id;
        _onlineAuthorName = name;
        _onlineCurrentPage = 1;
        _lastLoadedOnlineConfigKey = null;
        _onlineMods.Clear();
        _resettingOnlineFilters = true;
        try { OnlineSearchTextBox.Text = string.Empty; _onlineSearchText = string.Empty; }
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
            _onlineAuthorWorkFilter = 0;
            bool reload = _onlineAuthorId > 0 || _onlineCharacterFilter.Length > 0;
            _onlineAuthorId = 0;
            _onlineAuthorName = string.Empty;
            _onlineCharacterFilter = string.Empty;
            _onlineCharacterStripSignature = null;
            PopulateOnlineCharacterOptions();
            _resettingOnlineFilters = true;
            try { OnlineSearchTextBox.Text = string.Empty; _onlineSearchText = string.Empty; }
            finally { _resettingOnlineFilters = false; }
            if (reload)
            {
                _onlineCurrentPage = 1;
                _lastLoadedOnlineConfigKey = null;
                _onlineMods.Clear();
                await LoadOnlineModsAsync(forceReload: false);
            }
            else RefreshOnlinePaneV2();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnClearOnlineFiltersClicked", ex);
        }
    }

    private void RefreshOnlineFilterSummary(int matches, int loaded)
    {
        OnlineFilterSummaryTextBlock.Text = T("OnlineLibrary.ThisPageMatches", matches, loaded)
            + (_onlineAuthorId > 0 ? T("OnlineLibrary.AuthorAuthorPagesTextFiltersThis", _onlineAuthorName)
                : T("OnlineLibrary.SearchFiltersThisPageOnlyClick"));
        AutomationProperties.SetName(OnlineSearchTextBox, T("OnlineLibrary.SearchThisPageByCharacterMod"));
        ToolTipService.SetToolTip(OnlineFilterSummaryTextBlock, OnlineFilterSummaryTextBlock.Text);
        ClearOnlineFiltersButton.Content = T("OnlineLibrary.ClearFilters");
        ClearOnlineFiltersButton.IsEnabled = _onlineAuthorId > 0 || _onlineSearchText.Length > 0 || _onlineCharacterFilter.Length > 0 || _onlineAuthorWorkFilter > 0;
        OnlineWorkFilterButton.Content = _onlineAuthorWorkFilter switch { 1 => T("OnlineLibrary.InstalledOnPage"), 2 => T("OnlineLibrary.SavedOnPage"), _ => T("OnlineLibrary.AllOnPage") };
        ToolTipService.SetToolTip(OnlineSavedItemsButton, T("OnlineLibrary.AuthorsAndSavedModsFindAuthors"));
        AutomationProperties.SetName(OnlineSavedItemsButton, T("OnlineLibrary.AuthorsAndSavedMods"));
        ToolTipService.SetToolTip(OnlineDownloadOptionsButton, T("Common.DownloadOptions"));
        AutomationProperties.SetName(OnlineDownloadOptionsButton, T("Common.DownloadOptions"));
    }

    private bool IsSavedOnlineMod(OnlineModCard mod) => _onlinePreferences.SavedMods.Any(item =>
        item.RepositoryId == GetSelectedRepository()?.Id
        && string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), GetSelectedRepository()?.SourcePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
        && GetOnlineDownloadIdentity(item.Mod) == GetOnlineDownloadIdentity(mod));

    private void ToggleSavedOnlineMod(OnlineModCard mod)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null) return;
        bool remove = IsSavedOnlineMod(mod);
        _onlinePreferences.SavedMods.RemoveAll(item => item.RepositoryId == repository.Id
            && string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), repository.SourcePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            && GetOnlineDownloadIdentity(item.Mod) == GetOnlineDownloadIdentity(mod));
        if (!remove) _onlinePreferences.SavedMods.Insert(0, new OnlineSavedMod(repository.Id, repository.SourcePath, mod));
        _onlinePreferences.SavedMods = _onlinePreferences.SavedMods.Take(100).ToList();
        SaveShellConfig();
        ShowAppNotification(remove ? "已移出稍后下载。" : "已加入稍后下载。",
            remove ? "Removed from saved items." : "Saved for later.");
    }

    private MenuFlyout CreateOnlineCardMenu(OnlineModCard mod)
    {
        var menu = new MenuFlyout();
        var save = new MenuFlyoutItem { Text = IsSavedOnlineMod(mod) ? T("OnlineLibrary.RemoveFromSaved") : T("OnlineLibrary.SaveForLater") };
        save.Click += (_, _) => ToggleSavedOnlineMod(mod);
        menu.Items.Add(save);
        var author = new MenuFlyoutItem { Text = T("OnlineLibrary.BrowseAuthorAllPages"), IsEnabled = mod.AuthorId > 0 };
        author.Click += async (_, _) => await RunUiActionAsync("BrowseOnlineAuthorAsync", () => BrowseOnlineAuthorAsync(mod.AuthorId, mod.Author));
        menu.Items.Add(author);
        bool savedAuthor = _onlinePreferences.Authors.Any(item => item.Id == mod.AuthorId
            && item.CategoryId == GetEffectiveOnlineCategoryId(GetSelectedRepository()));
        var favorite = new MenuFlyoutItem { Text = savedAuthor ? T("OnlineLibrary.UnsaveAuthor") : T("OnlineLibrary.SaveAuthor"), IsEnabled = mod.AuthorId > 0 };
        favorite.Click += (_, _) =>
        {
            string category = GetEffectiveOnlineCategoryId(GetSelectedRepository());
            _onlinePreferences.Authors.RemoveAll(item => item.Id == mod.AuthorId && item.CategoryId == category);
            if (!savedAuthor) _onlinePreferences.Authors.Insert(0, new OnlineAuthorBookmark(mod.AuthorId, mod.Author, category));
            _onlinePreferences.Authors = _onlinePreferences.Authors.Take(100).ToList();
            SaveShellConfig();
        };
        menu.Items.Add(favorite);
        menu.Opening += (_, _) =>
        {
            save.Text = IsSavedOnlineMod(mod) ? T("OnlineLibrary.RemoveFromSaved") : T("OnlineLibrary.SaveForLater");
            savedAuthor = _onlinePreferences.Authors.Any(item => item.Id == mod.AuthorId
                && item.CategoryId == GetEffectiveOnlineCategoryId(GetSelectedRepository()));
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
            LogApplicationIssue("Author and saved workspace", ex);
            await ReportOnlineActionErrorAsync(ex.Message, T("OnlineLibrary.CannotCompleteAction"));
        }
    }
}

public sealed record OnlineAuthorBookmark(int Id, string Name, string CategoryId);
public sealed record OnlineSavedMod(string RepositoryId, string RepositoryPath, OnlineModCard Mod);
public sealed class OnlineWorkspacePreferences
{
    public Dictionary<string, OnlineBrowseSession> BrowseSessions { get; set; } = [];
    public List<OnlineAuthorVisit> RecentAuthors { get; set; } = [];
    public int ConcurrentDownloads { get; set; } = 2;
    public bool RememberDownloadFolders { get; set; }
    public Dictionary<string, string> DownloadFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<OnlineAuthorBookmark> Authors { get; set; } = [];
    public List<OnlineSavedMod> SavedMods { get; set; } = [];
    public string? LastImportManifest { get; set; }
}
