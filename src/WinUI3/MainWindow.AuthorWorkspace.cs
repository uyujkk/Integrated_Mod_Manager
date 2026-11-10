using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void OnOnlineWorkFilterClicked(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        string[] names = [T("AuthorWorkspace.AllOnThisPage"), T("AuthorWorkspace.InstalledOnThisPage"), T("AuthorWorkspace.SavedOnThisPage")];
        for (int i = 0; i < names.Length; i++)
        {
            int value = i;
            var item = new ToggleMenuFlyoutItem { Text = names[i], IsChecked = _onlinePage.AuthorWorkFilter == i };
            item.Click += (_, _) => { _onlinePage.AuthorWorkFilter = value; ResetOnlineBrowsePosition(); RefreshOnlinePaneV2(); SaveShellConfig(); };
            menu.Items.Add(item);
        }
        menu.ShowAt(OnlineWorkFilterButton);
    }

    private async Task ShowOnlineLibraryAsync()
    {
        UiCrashDiagnostics.RecordAction("Author and saved workspace opened");
        var repo = GetSelectedRepository();
        if (repo is null) return;
        string category = GetEffectiveOnlineCategoryId(repo);
        string contextKey = GetOnlineDetailContextKey();
        Func<Task>? next = await AuthorWorkspaceDialog.ShowAsync(_services, new AuthorWorkspaceDialog.Host(
            _onlinePreferences, () => SaveShellConfig(), BrowseOnlineAuthorAsync, ShowOnlineModDetailsAsync, saved => PrepareSavedBatchAsync(saved, repo)),
            repo, category, RootGrid.ActualWidth, RootGrid.ActualHeight);
        SaveShellConfig();
        if (next is not null && !_onlineWindowClosed && contextKey == GetOnlineDetailContextKey()) await next();
    }
}
