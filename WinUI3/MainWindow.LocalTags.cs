using IntegratedModManager.Core;
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

    private string LocalModRelativePath(string path) => WorkspaceIdentity.Relative(GetSelectedRepository()!.SourcePath, path);

    private void InitializeLocalTagControls(Grid modGrid)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_repositoryModSearch!);
        _localTagsButton = new Button { Content = L("标签", "Tags"), MinHeight = 36 };
        _localTagsButton.Click += async (_, _) => await RunUiActionAsync("EditLocalTagsAsync", () => EditLocalTagsAsync());
        AutomationProperties.SetName(_localTagsButton, L("批量编辑本地 Mod 标签", "Edit local Mod tags in bulk"));
        Grid.SetColumn(_localTagsButton, 1); row.Children.Add(_localTagsButton);
        _localFiltersButton = new Button { Content = L("筛选", "Filters"), MinHeight = 36 };
        _localFiltersButton.Click += (_, _) => ShowLocalFilterMenu();
        Grid.SetColumn(_localFiltersButton, 2); row.Children.Add(_localFiltersButton);
        Grid.SetRow(row, 1); modGrid.Children.Add(row);
    }

    private bool MatchesLocalLibraryFilter(SecondLevelFolderItem mod)
    {
        var library = SelectedLocalLibrary();
        if (library is null) return RepositoryWorkspacePolicy.MatchesQuery(mod.Name,
            Path.GetFileName(Path.GetDirectoryName(mod.Path)) ?? "", _repositoryModSearch!.Text);
        return library.Matches(LocalModRelativePath(mod.Path), mod.Name + " " + Path.GetFileName(Path.GetDirectoryName(mod.Path)), _repositoryModSearch!.Text, _localTagFilter);
    }

    private string LocalModTagSummary(string path)
    {
        string[] tags = SelectedLocalLibrary()?.TagsFor(LocalModRelativePath(path)) ?? [];
        return tags.Length == 0 ? "" : "\n" + L("标签：", "Tags: ") + string.Join(" · ", tags);
    }

    private async Task EditLocalTagsAsync(SecondLevelFolderItem? single = null)
    {
        var repo = GetSelectedRepository();
        var library = SelectedLocalLibrary();
        if (repo is null || library is null) return;
        string repoKey = WorkspaceIdentity.Library(repo.Id, repo.SourcePath);
        var items = single is null ? _repositoryVisibleMods.ToArray() : [single];
        if (items.Length == 0) { await ShowMessageAsync(L("当前筛选下没有 Mod。", "No Mods in the current filter."), L("标签", "Tags")); return; }
        var body = new StackPanel { Spacing = 10, MinWidth = 300, MaxWidth = 580 };
        body.Children.Add(new TextBlock { Text = L("勾选要修改的 Mod。标签仅保存到管理器，不修改 Mod 文件。", "Select Mods to edit. Tags are stored by the manager without changing Mod files."), TextWrapping = TextWrapping.Wrap });
        var mode = new ComboBox { Header = L("操作", "Action"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string label in new[] { L("添加标签", "Add tags"), L("移除标签", "Remove tags"), L("替换全部标签（留空可清空）", "Replace all tags (leave blank to clear)") }) mode.Items.Add(label);
        mode.SelectedIndex = 0;
        body.Children.Add(mode);
        var input = new TextBox { Header = L("标签（用逗号分隔，每个最多 32 字，最多 16 个）", "Tags (comma separated, up to 16 tags of 32 characters)"), MaxLength = 1024,
            PlaceholderText = L("待体验, 常用, 需要修复", "To try, Favorite, Needs repair") };
        body.Children.Add(input);
        var checks = new List<(SecondLevelFolderItem Mod, CheckBox Check)>();
        var selectAll = new CheckBox { Content = L("全选当前列表", "Select all listed Mods") };
        body.Children.Add(selectAll);
        foreach (var mod in items)
        {
            var check = new CheckBox { Content = mod.Name + LocalModTagSummary(mod.Path), IsChecked = ReferenceEquals(mod, single ?? _repositorySelectedMod) };
            checks.Add((mod, check)); body.Children.Add(check);
        }
        selectAll.Click += (_, _) => { foreach (var item in checks) item.Check.IsChecked = selectAll.IsChecked == true; };
        var dialog = new ContentDialog { Title = L("编辑本地标签", "Edit local tags"), Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(180, RootGrid.ActualHeight - 280) },
            PrimaryButtonText = L("保存", "Save"), CloseButtonText = L("取消", "Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme };
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Insert(3, validation);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            string[] requested = input.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (requested.Any(tag => tag.Length > 32 || tag.Any(char.IsControl)) || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 16)
            { validation.Text = L("每个标签最多 32 字，一次最多 16 个。", "Use up to 16 tags, each at most 32 characters."); args.Cancel = true; return; }
            if (!checks.Any(item => item.Check.IsChecked == true))
            { validation.Text = L("请至少选择一个 Mod。", "Select at least one Mod."); args.Cancel = true; }
            else if (mode.SelectedIndex == 0 && checks.Where(item => item.Check.IsChecked == true).Any(item =>
                library.TagsFor(WorkspaceIdentity.Relative(repo.SourcePath, item.Mod.Path)).Concat(requested).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 16))
            { validation.Text = L("添加后有 Mod 会超过 16 个标签，请减少标签或改用替换。", "Some Mods would exceed 16 tags. Reduce the tags or use Replace."); args.Cancel = true; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (GetSelectedRepository() is not { } current
            || WorkspaceIdentity.Library(current.Id, current.SourcePath) != repoKey) return;
        var tags = input.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        library.EditTags(checks.Where(item => item.Check.IsChecked == true).Select(item => LocalModRelativePath(item.Mod.Path)), tags, (TagEditMode)mode.SelectedIndex);
        SaveShellConfig(); RefreshRepositoryModView();
    }

    private void ShowLocalFilterMenu()
    {
        var library = SelectedLocalLibrary();
        if (library is null) return;
        var menu = new MenuFlyout();
        var clear = new MenuFlyoutItem { Text = L("清空搜索与标签筛选", "Clear search and tag filter") };
        clear.Click += (_, _) => { _localTagFilter = ""; _repositoryModSearch!.Text = ""; RefreshRepositoryModView(); };
        menu.Items.Add(clear);
        var tags = new MenuFlyoutSubItem { Text = L("按标签筛选", "Filter by tag") };
        foreach (string tag in library.ModTags.Values.SelectMany(value => value).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ToggleMenuFlyoutItem { Text = tag, IsChecked = _localTagFilter.Equals(tag, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => { _localTagFilter = item.IsChecked ? tag : ""; RefreshRepositoryModView(); };
            tags.Items.Add(item);
        }
        tags.IsEnabled = tags.Items.Count > 0; menu.Items.Add(tags);
        var save = new MenuFlyoutItem { Text = L("保存当前筛选…", "Save current filter…") };
        save.Click += async (_, _) => await RunUiActionAsync("SaveLocalFilterAsync", () => SaveLocalFilterAsync()); menu.Items.Add(save);
        foreach (var filter in library.Filters)
        {
            var entry = new MenuFlyoutSubItem { Text = filter.Name };
            var apply = new MenuFlyoutItem { Text = L("应用", "Apply") };
            apply.Click += async (_, _) =>
            {
                try
                {
                    var repo = GetSelectedRepository();
                    if (repo is null) return;
                    string role = Path.Combine(repo.SourcePath, filter.Character.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(role) || !IsPathInsideDirectory(role, repo.SourcePath))
                    { await ShowMessageAsync(L("原角色目录已不存在，请重新保存筛选。", "The original character folder is unavailable. Save the filter again."), L("筛选无法应用", "Cannot apply filter")); return; }
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
            var remove = new MenuFlyoutItem { Text = L("删除此筛选", "Delete this filter") };
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
        string? name = await PromptForTextAsync(L("输入筛选名称；同名筛选会被更新（最多 64 字）。", "Name this filter (up to 64 characters). An existing name will be updated."), L("保存筛选", "Save filter"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Trim().Length > 64) { await ShowMessageAsync(L("名称最多 64 字。", "Names can contain up to 64 characters."), L("名称过长", "Name too long")); return; }
        snapshot.Name = name; library.SaveFilter(snapshot); SaveShellConfig();
    }
}
