using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Saved and recent authors and the saved Mods of the repository (moved from the window in stage J8). Opening an author
/// or a Mod, or downloading saved Mods, closes the dialog and returns what to do next.
/// </summary>
internal static class AuthorWorkspaceDialog
{
    /// <summary>What the dialog uses from the window.</summary>
    public sealed record Host(OnlineWorkspacePreferences Preferences, Action Save, Func<int, string, Task> OpenAuthor,
        Func<OnlineModCard, Task> OpenMod, Func<OnlineSavedMod[], Task> DownloadSaved);

    /// <summary>The next step the user chose, or null when the dialog was just closed.</summary>
    public static async Task<Func<Task>?> ShowAsync(AppServices services, Host host, WorkspaceRepository repo, string category, double windowWidth, double windowHeight)
    {
        string T(string key) => services.Language.T(key);
        var pivot = new Pivot { MinWidth = 320, MaxWidth = 660 };
        var authors = new StackPanel { Spacing = 10 };
        var savedMods = new StackPanel { Spacing = 10 };
        var dialog = new ContentDialog { Title = T("AuthorWorkspace.AuthorsAndSavedMods"), CloseButtonText = T("Common.Close"),
            Content = pivot };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Min(740, Math.Max(400, windowWidth - 80));
        Func<Task>? next = null;
        double height = Math.Max(180, windowHeight - 330);
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
            var bookmarked = host.Preferences.Authors.Where(item => item.CategoryId == category).ToArray();
            var recent = host.Preferences.RecentAuthors.Where(item => item.CategoryId == category).ToArray();
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
                open.Click += (_, _) => { next = () => host.OpenAuthor(item.Id, item.Name); dialog.Hide(); };
                row.Children.Add(open);
                var toggle = new Button { Content = isSaved ? T("AuthorWorkspace.Unsave") : T("AuthorWorkspace.Save") };
                toggle.Click += (_, _) =>
                {
                    host.Preferences.Authors.RemoveAll(entry => entry.CategoryId == category && entry.Id == item.Id);
                    if (!isSaved) host.Preferences.Authors.Insert(0, new OnlineAuthorBookmark(item.Id, item.Name, category));
                    host.Preferences.Authors = host.Preferences.Authors.Take(100).ToList();
                    host.Save(); RefreshAuthors();
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
            foreach (var item in host.Preferences.SavedMods.Where(item => item.RepositoryId == repo.Id
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
                open.Click += (_, _) => { next = () => host.OpenMod(item.Mod); dialog.Hide(); };
                Grid.SetColumn(open, 1); row.Children.Add(open);
                var remove = new Button { Content = T("AuthorWorkspace.Remove") };
                remove.Click += (_, _) => { selected.Remove(item); host.Preferences.SavedMods.Remove(item); host.Save(); RefreshSaved(); };
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
        batch.Click += (_, _) => { var snapshot = selected.ToArray(); next = () => host.DownloadSaved(snapshot); dialog.Hide(); };
        RefreshSaved();
        await services.Dialogs.ShowAsync(dialog);
        return next;
    }
}
