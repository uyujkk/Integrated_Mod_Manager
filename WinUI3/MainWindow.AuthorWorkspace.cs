using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
            var item = new ToggleMenuFlyoutItem { Text = names[i], IsChecked = _onlineAuthorWorkFilter == i };
            item.Click += (_, _) => { _onlineAuthorWorkFilter = value; ResetOnlineBrowsePosition(); RefreshOnlinePaneV2(); SaveShellConfig(); };
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
        var pivot = new Pivot { MinWidth = 320, MaxWidth = 660 };
        var authors = new StackPanel { Spacing = 10 };
        var savedMods = new StackPanel { Spacing = 10 };
        var dialog = new ContentDialog { Title = T("AuthorWorkspace.AuthorsAndSavedMods"), CloseButtonText = T("Common.Close"),
            Content = pivot, XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Min(740, Math.Max(400, RootGrid.ActualWidth - 80));
        Func<Task>? next = null;
        double height = Math.Max(180, RootGrid.ActualHeight - 330);
        pivot.Items.Add(new PivotItem { Header = T("AuthorWorkspace.Authors"), Content = new ScrollViewer { Content = authors, MaxHeight = height } });
        pivot.Items.Add(new PivotItem { Header = T("AuthorWorkspace.SavedMods"), Content = new ScrollViewer { Content = savedMods, MaxHeight = height } });

        var search = new TextBox { PlaceholderText = T("AuthorWorkspace.SearchSavedOrRecentAuthors"), MaxLength = 256 };
        AutomationProperties.SetName(search, search.PlaceholderText);
        authors.Children.Add(search);
        var scope = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string name in new[] { T("AuthorWorkspace.SavedAndRecent"), T("AuthorWorkspace.SavedAuthors"), T("AuthorWorkspace.RecentlyVisited") }) scope.Items.Add(name);
        scope.SelectedIndex = 0; authors.Children.Add(scope);
        authors.Children.Add(new TextBlock { Text = T("AuthorWorkspace.SearchesLocalAuthorRecordsForThis"), TextWrapping = TextWrapping.Wrap });
        var authorRows = new StackPanel { Spacing = 8 }; authors.Children.Add(authorRows);
        void RefreshAuthors()
        {
            authorRows.Children.Clear();
            var bookmarked = _onlinePreferences.Authors.Where(item => item.CategoryId == category).ToArray();
            var recent = _onlinePreferences.RecentAuthors.Where(item => item.CategoryId == category).ToArray();
            var records = scope.SelectedIndex switch
            {
                1 => bookmarked.Select(item => (item.Id, item.Name)),
                2 => recent.Select(item => (item.Id, item.Name)),
                _ => bookmarked.Select(item => (item.Id, item.Name)).Concat(recent.Select(item => (item.Id, item.Name)))
            };
            var matches = records.DistinctBy(item => item.Id).Where(item => item.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var item in matches)
            {
                bool isSaved = bookmarked.Any(entry => entry.Id == item.Id);
                var visit = recent.FirstOrDefault(entry => entry.Id == item.Id);
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var open = new Button { Content = new TextBlock { Text = item.Name + (visit is null ? "" : "\n" + T("AuthorWorkspace.LastVisit") + visit.VisitedAt.ToLocalTime().ToString("g")), TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
                open.Click += (_, _) => { next = () => BrowseOnlineAuthorAsync(item.Id, item.Name); dialog.Hide(); };
                row.Children.Add(open);
                var toggle = new Button { Content = isSaved ? T("AuthorWorkspace.Unsave") : T("AuthorWorkspace.Save") };
                toggle.Click += (_, _) =>
                {
                    _onlinePreferences.Authors.RemoveAll(entry => entry.CategoryId == category && entry.Id == item.Id);
                    if (!isSaved) _onlinePreferences.Authors.Insert(0, new OnlineAuthorBookmark(item.Id, item.Name, category));
                    _onlinePreferences.Authors = _onlinePreferences.Authors.Take(100).ToList();
                    SaveShellConfig(); RefreshAuthors();
                };
                Grid.SetColumn(toggle, 1); row.Children.Add(toggle); authorRows.Children.Add(row);
            }
            if (matches.Length == 0) authorRows.Children.Add(new TextBlock { Text = T("AuthorWorkspace.NoMatchingAuthorsClickAMod"), TextWrapping = TextWrapping.Wrap });
        }
        search.TextChanged += (_, _) => RefreshAuthors();
        scope.SelectionChanged += (_, _) => RefreshAuthors();
        RefreshAuthors();

        savedMods.Children.Add(new TextBlock { Text = repo.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var savedSearch = new TextBox { PlaceholderText = T("AuthorWorkspace.SearchTitleOrAuthor") };
        savedMods.Children.Add(savedSearch);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var selectAll = new CheckBox { Content = T("AuthorWorkspace.SelectVisible") };
        var batch = new Button { Content = T("AuthorWorkspace.DownloadSelected"), IsEnabled = false };
        actions.Children.Add(selectAll); actions.Children.Add(batch); savedMods.Children.Add(actions);
        var savedRows = new StackPanel { Spacing = 8 }; savedMods.Children.Add(savedRows);
        var selected = new HashSet<OnlineSavedMod>();
        var checks = new List<(OnlineSavedMod Item, CheckBox Check)>();
        void RefreshSaved()
        {
            savedRows.Children.Clear(); checks.Clear();
            foreach (var item in _onlinePreferences.SavedMods.Where(item => item.RepositoryId == repo.Id
                && string.Equals(item.RepositoryPath.TrimEnd('\\', '/'), repo.SourcePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                && (item.Mod.Title.Contains(savedSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    || item.Mod.Author.Contains(savedSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray())
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var check = new CheckBox { IsChecked = selected.Contains(item) };
                AutomationProperties.SetName(check, T("AuthorWorkspace.SelectDownload") + item.Mod.Title);
                checks.Add((item, check)); row.Children.Add(check);
                check.Click += (_, _) => { if (check.IsChecked == true) selected.Add(item); else selected.Remove(item); batch.IsEnabled = selected.Count > 0; };
                var open = new Button { Content = new TextBlock { Text = item.Mod.Title + "\n" + item.Mod.Author, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
                open.Click += (_, _) => { next = () => ShowOnlineModDetailsAsync(item.Mod); dialog.Hide(); };
                Grid.SetColumn(open, 1); row.Children.Add(open);
                var remove = new Button { Content = T("AuthorWorkspace.Remove") };
                remove.Click += (_, _) => { selected.Remove(item); _onlinePreferences.SavedMods.Remove(item); SaveShellConfig(); RefreshSaved(); };
                Grid.SetColumn(remove, 2); row.Children.Add(remove); savedRows.Children.Add(row);
            }
            if (checks.Count == 0) savedRows.Children.Add(new TextBlock { Text = T("AuthorWorkspace.NoMatchingSavedModsRightClick"), TextWrapping = TextWrapping.Wrap });
            batch.IsEnabled = selected.Count > 0;
        }
        savedSearch.TextChanged += (_, _) => RefreshSaved();
        selectAll.Click += (_, _) =>
        {
            foreach (var item in checks)
            { item.Check.IsChecked = selectAll.IsChecked; if (selectAll.IsChecked == true) selected.Add(item.Item); else selected.Remove(item.Item); }
            batch.IsEnabled = selected.Count > 0;
        };
        batch.Click += (_, _) => { var snapshot = selected.ToArray(); next = () => PrepareSavedBatchAsync(snapshot, repo); dialog.Hide(); };
        RefreshSaved();
        await dialog.ShowAsync();
        SaveShellConfig();
        if (next is not null && !_onlineWindowClosed && contextKey == GetOnlineDetailContextKey()) await next();
    }
}
