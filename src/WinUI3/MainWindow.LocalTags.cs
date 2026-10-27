using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private Dictionary<string, LocalLibraryState> _localLibraries = new(StringComparer.OrdinalIgnoreCase);
    private string _localTagFilter = "";
    private string? _localLibraryKey;
    private Button? _localFiltersButton;
    private Button? _localTagsButton;

    private LocalLibraryState? SelectedLocalLibrary()
    {
        var repo = GetSelectedRepository();
        if (repo is null || string.IsNullOrWhiteSpace(repo.SourcePath)) return null;
        string key = WorkspaceIdentity.Library(repo.Id, repo.SourcePath);
        if (_localLibraryKey != key) { _localLibraryKey = key; _localTagFilter = ""; }
        if (!_localLibraries.TryGetValue(key, out var library)) _localLibraries[key] = library = new();
        return library;
    }

    // Right after switching repositories the lists can still hold Mods of the previous one.
    // Those are not part of this repository's library, so they get null instead of a throw.
    private string? LocalModRelativePath(string path)
    {
        string? source = GetSelectedRepository()?.SourcePath;
        return string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(path)
            || !IntegratedModManager.Core.Infrastructure.PathSafety.IsStrictlyInside(path, source)
                ? null
                : WorkspaceIdentity.Relative(source, path);
    }

    private void InitializeLocalTagControls(Grid modGrid)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_repositoryModSearch!);
        _localTagsButton = new Button { Content = T("Common.Tags"), MinHeight = 36 };
        _localTagsButton.Click += async (_, _) => await RunUiActionAsync("EditLocalTagsAsync", () => EditLocalTagsAsync());
        AutomationProperties.SetName(_localTagsButton, T("LocalTags.EditLocalModTagsInBulk"));
        Grid.SetColumn(_localTagsButton, 1); row.Children.Add(_localTagsButton);
        _localFiltersButton = new Button { Content = T("Common.Filters"), MinHeight = 36 };
        _localFiltersButton.Click += (_, _) => ShowLocalFilterMenu();
        Grid.SetColumn(_localFiltersButton, 2); row.Children.Add(_localFiltersButton);
        Grid.SetRow(row, 1); modGrid.Children.Add(row);
    }

    private bool MatchesLocalLibraryFilter(SecondLevelFolderItem mod)
    {
        var library = SelectedLocalLibrary();
        string? relativePath = LocalModRelativePath(mod.Path);
        if (library is null || relativePath is null) return RepositoryWorkspacePolicy.MatchesQuery(mod.Name,
            Path.GetFileName(Path.GetDirectoryName(mod.Path)) ?? "", _repositoryModSearch!.Text);
        return library.Matches(relativePath, mod.Name + " " + Path.GetFileName(Path.GetDirectoryName(mod.Path)), _repositoryModSearch!.Text, _localTagFilter);
    }

    private string LocalModTagSummary(string path)
    {
        string? relativePath = LocalModRelativePath(path);
        string[] tags = relativePath is null ? [] : SelectedLocalLibrary()?.TagsFor(relativePath) ?? [];
        return tags.Length == 0 ? "" : "\n" + T("LocalTags.Tags") + string.Join(" · ", tags);
    }

    private async Task EditLocalTagsAsync(SecondLevelFolderItem? single = null)
    {
        var repo = GetSelectedRepository();
        var library = SelectedLocalLibrary();
        if (repo is null || library is null) return;
        string repoKey = WorkspaceIdentity.Library(repo.Id, repo.SourcePath);
        var items = single is null ? _repositoryVisibleMods.ToArray() : [single];
        if (items.Length == 0) { await ShowMessageAsync(T("LocalTags.NoModsInTheCurrentFilter"), T("Common.Tags")); return; }
        var body = new StackPanel { Spacing = 10, MinWidth = 300, MaxWidth = 580 };
        body.Children.Add(new TextBlock { Text = T("LocalTags.SelectModsToEditTagsAre"), TextWrapping = TextWrapping.Wrap });
        var mode = new ComboBox { Header = T("LocalTags.Action"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string label in new[] { T("LocalTags.AddTags"), T("LocalTags.RemoveTags"), T("LocalTags.ReplaceAllTagsLeaveBlankTo") }) mode.Items.Add(label);
        mode.SelectedIndex = 0;
        body.Children.Add(mode);
        var input = new TextBox { Header = T("LocalTags.TagsCommaSeparatedUpTo16"), MaxLength = 1024,
            PlaceholderText = T("LocalTags.ToTryFavoriteNeedsRepair") };
        body.Children.Add(input);
        var checks = new List<(SecondLevelFolderItem Mod, CheckBox Check)>();
        var selectAll = new CheckBox { Content = T("LocalTags.SelectAllListedMods") };
        body.Children.Add(selectAll);
        foreach (var mod in items)
        {
            var check = new CheckBox { Content = mod.Name + LocalModTagSummary(mod.Path), IsChecked = ReferenceEquals(mod, single ?? _repositorySelectedMod) };
            checks.Add((mod, check)); body.Children.Add(check);
        }
        selectAll.Click += (_, _) => { foreach (var item in checks) item.Check.IsChecked = selectAll.IsChecked == true; };
        var dialog = new ContentDialog { Title = T("LocalTags.EditLocalTags"), Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(180, RootGrid.ActualHeight - 280) },
            PrimaryButtonText = T("Common.Save"), CloseButtonText = T("Common.Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Insert(3, validation);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            string[] requested = input.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (requested.Any(tag => tag.Length > 32 || tag.Any(char.IsControl)) || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 16)
            { validation.Text = T("LocalTags.UseUpTo16TagsEach"); args.Cancel = true; return; }
            if (!checks.Any(item => item.Check.IsChecked == true))
            { validation.Text = T("LocalTags.SelectAtLeastOneMod"); args.Cancel = true; }
            else if (mode.SelectedIndex == 0 && checks.Where(item => item.Check.IsChecked == true).Any(item =>
                library.TagsFor(WorkspaceIdentity.Relative(repo.SourcePath, item.Mod.Path)).Concat(requested).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 16))
            { validation.Text = T("LocalTags.SomeModsWouldExceed16Tags"); args.Cancel = true; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (GetSelectedRepository() is not { } current
            || WorkspaceIdentity.Library(current.Id, current.SourcePath) != repoKey) return;
        var tags = input.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        library.EditTags(checks.Where(item => item.Check.IsChecked == true)
            .Select(item => LocalModRelativePath(item.Mod.Path)).OfType<string>(), tags, (TagEditMode)mode.SelectedIndex);
        SaveShellConfig(); RefreshRepositoryModView();
    }

    private void ShowLocalFilterMenu()
    {
        var library = SelectedLocalLibrary();
        if (library is null) return;
        var menu = new MenuFlyout();
        var clear = new MenuFlyoutItem { Text = T("LocalTags.ClearSearchAndTagFilter") };
        clear.Click += (_, _) => { _localTagFilter = ""; _repositoryModSearch!.Text = ""; RefreshRepositoryModView(); };
        menu.Items.Add(clear);
        var tags = new MenuFlyoutSubItem { Text = T("LocalTags.FilterByTag") };
        foreach (string tag in library.ModTags.Values.SelectMany(value => value).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ToggleMenuFlyoutItem { Text = tag, IsChecked = _localTagFilter.Equals(tag, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => { _localTagFilter = item.IsChecked ? tag : ""; RefreshRepositoryModView(); };
            tags.Items.Add(item);
        }
        tags.IsEnabled = tags.Items.Count > 0; menu.Items.Add(tags);
        var save = new MenuFlyoutItem { Text = T("LocalTags.SaveCurrentFilter") };
        save.Click += async (_, _) => await RunUiActionAsync("SaveLocalFilterAsync", () => SaveLocalFilterAsync()); menu.Items.Add(save);
        foreach (var filter in library.Filters)
        {
            var entry = new MenuFlyoutSubItem { Text = filter.Name };
            var apply = new MenuFlyoutItem { Text = T("Common.Apply") };
            apply.Click += async (_, _) =>
            {
                try
                {
                    var repo = GetSelectedRepository();
                    if (repo is null) return;
                    string role = Path.Combine(repo.SourcePath, filter.Character.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(role) || !IsPathInsideDirectory(role, repo.SourcePath))
                    { await ShowMessageAsync(T("LocalTags.TheOriginalCharacterFolderIsUnavailable"), T("LocalTags.CannotApplyFilter")); return; }
                    FirstLevelSearchTextBox.Text = "";
                    SelectFirstLevelByPath(role);
                    _localTagFilter = filter.Tag;
                    _repositoryModSearch!.Text = filter.Search;
                    RefreshRepositoryModView();
                }
                catch (Exception ex)
                {
                    ReportUiActionFailure("apply.Click", ex);
                }
            };
            entry.Items.Add(apply);
            var remove = new MenuFlyoutItem { Text = T("LocalTags.DeleteThisFilter") };
            remove.Click += (_, _) => { library.Filters.Remove(filter); SaveShellConfig(); };
            entry.Items.Add(remove); menu.Items.Add(entry);
        }
        menu.ShowAt(_localFiltersButton!);
    }

    private async Task SaveLocalFilterAsync()
    {
        var repo = GetSelectedRepository();
        var library = SelectedLocalLibrary();
        if (repo is null || library is null || FirstLevelListView.SelectedItem is not FirstLevelFolderItem role) return;
        var snapshot = new LocalModFilter { Character = WorkspaceIdentity.Relative(repo.SourcePath, role.Path), Search = _repositoryModSearch!.Text, Tag = _localTagFilter };
        string? name = await PromptForTextAsync(T("LocalTags.NameThisFilterUpTo64"), T("LocalTags.SaveFilter"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Trim().Length > 64) { await ShowMessageAsync(T("LocalTags.NamesCanContainUpTo64"), T("LocalTags.NameTooLong")); return; }
        snapshot.Name = name; library.SaveFilter(snapshot); SaveShellConfig();
    }
}
