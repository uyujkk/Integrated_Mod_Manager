using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    private void OnOnlineWorkFilterClicked(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        string[] names = [T("AuthorWorkspace.AllOnThisPage"), T("AuthorWorkspace.InstalledOnThisPage"), T("AuthorWorkspace.SavedOnThisPage")];
        for (int i = 0; i < names.Length; i++)
        {
            int value = i;
            var item = new ToggleMenuFlyoutItem { Text = names[i], IsChecked = _services.Online.Page.AuthorWorkFilter == i };
            item.Click += (_, _) => { _services.Online.Page.AuthorWorkFilter = value; ResetOnlineBrowsePosition(); RefreshOnlinePaneV2(); _host.SaveShellConfig(); };
            menu.Items.Add(item);
        }
        menu.ShowAt(OnlineWorkFilterButton);
    }

    private async Task ShowOnlineLibraryAsync()
    {
        UiCrashDiagnostics.RecordAction("Author and saved workspace opened");
        var repo = _services.Repositories.Selected;
        if (repo is null) return;
        string category = _services.OnlineSource.CategoryId(repo);
        string contextKey = GetOnlineDetailContextKey();
        Func<Task>? next = await AuthorWorkspaceDialog.ShowAsync(_services, new AuthorWorkspaceDialog.Host(
            _services.Online.Preferences, () => _host.SaveShellConfig(), BrowseOnlineAuthorAsync, ShowOnlineModDetailsAsync, saved => PrepareSavedBatchAsync(saved, repo)),
            repo, category, _host.Root.ActualWidth, _host.Root.ActualHeight);
        _host.SaveShellConfig();
        if (next is not null && !_services.IsClosing && contextKey == GetOnlineDetailContextKey()) await next();
    }
}
