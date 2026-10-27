using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // Folder counts per repository id from the last background scan. Cards show these
    // (or "counting") at once; a newer scan replaces them without blocking the page.
    private readonly Dictionary<string, RepositoryContentSummary> _repositorySummaries = new(StringComparer.Ordinal);
    private int _repositoryScanGeneration;
    private RepositoryCardActions? _repositoryCardActions;

    private void RefreshDashboard()
    {
        if (DashboardRepositoryComboBox is null)
        {
            return;
        }

        if (_selectedRepositoryId is null && _repositories.Count > 0)
        {
            _selectedRepositoryId = _repositories[0].Id;
        }

        PopulateDashboardRepositorySwitcher();
        RefreshNewRepositoryMenu();

        var labels = new RepositoryCardLabels(
            T("Dashboard.SwitchToThisRepository"),
            T("Dashboard.Manage"),
            T("Dashboard.MoreActions"),
            T("Dashboard.EditRepository"),
            T("Dashboard.OpenGameWiki"),
            T("Dashboard.DeleteRepository"),
            T("Dashboard.Current"));
        _repositoryCardActions ??= new RepositoryCardActions(
            id => SwitchRepository(id, openManager: false),
            id => SwitchRepository(id, openManager: true),
            EditRepositoryFromCardAsync,
            id =>
            {
                if (FindRepository(id) is { } repository) OpenRepositoryWiki(repository);
            },
            DeleteRepositoryFromCardAsync);

        var ids = new List<string>(_repositories.Count);
        for (int index = 0; index < _repositories.Count; index++)
        {
            WorkspaceRepository repository = _repositories[index];
            ids.Add(repository.Id);
            RepositoryCardViewModel card = Dashboard.GetOrAdd(repository.Id, index,
                () => new RepositoryCardViewModel(repository.Id, _repositoryCardActions));
            card.Name = repository.Name;
            card.Subtitle = DescribeRepositorySource(repository);
            card.IsCurrent = string.Equals(repository.Id, _selectedRepositoryId, StringComparison.Ordinal);
            card.CanDelete = _repositories.Count > 1;
            card.HasWiki = Uri.TryCreate(repository.WikiUrl, UriKind.Absolute, out _);
            card.Labels = labels;
            ApplyRepositorySummary(card, repository);
        }
        Dashboard.RemoveExcept(ids);

        DashboardOpenRepositoryButton.IsEnabled = GetSelectedRepository() is not null;
        RefreshDashboardCurrentStatus();
        RefreshRefinedDashboardPathHeader();
        UpdatePathStatusIcons();
        RefreshTrayStatus();
        _ = ScanRepositorySummariesAsync();
    }

    // One icon inside each path box shows whether the folder (or the launcher file) exists, so
    // "needs setup" points at the box to fix. The launcher is optional and shows nothing when empty.
    private (TextBox Box, FontIcon Icon, bool ExpectFile)[] _pathStatusIcons = [];

    private void InitializePathStatusIcons()
    {
        _pathStatusIcons =
        [
            (SourceTextBox, AttachPathStatusIcon(SourceTextBox), false),
            (TargetTextBox, AttachPathStatusIcon(TargetTextBox), false),
            (LauncherTextBox, AttachPathStatusIcon(LauncherTextBox), true)
        ];
        foreach ((TextBox box, _, _) in _pathStatusIcons)
        {
            box.TextChanged += (_, _) => UpdatePathStatusIcons();
        }
        UpdatePathStatusIcons();
    }

    // The box sits in a Border of its path row. Parent is not reliable before the page is
    // loaded, so the Border is found among the row's children.
    private FontIcon AttachPathStatusIcon(TextBox box)
    {
        var icon = new FontIcon { FontSize = 14, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        Border? border = _workspacePathRows
            .SelectMany(row => row.Children.OfType<Border>())
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Child, box));
        if (border is not null)
        {
            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            border.Child = null;
            host.Children.Add(box);
            Grid.SetColumn(icon, 1);
            host.Children.Add(icon);
            border.Child = host;
        }
        return icon;
    }

    private void UpdatePathStatusIcons()
    {
        foreach ((TextBox box, FontIcon icon, bool expectFile) in _pathStatusIcons)
        {
            PathFieldStatus status = PathFieldStatuses.Evaluate(box.Text, expectFile);
            bool ready = status == PathFieldStatus.Ready;
            icon.Visibility = status == PathFieldStatus.Empty && expectFile ? Visibility.Collapsed : Visibility.Visible;
            icon.Glyph = ready ? "" : "";
            icon.Foreground = GetAppThemeBrush(ready ? "AppSuccessBrush" : "AppDangerBrush");
            string tip = status switch
            {
                PathFieldStatus.Ready => T("Dashboard.PathFound"),
                PathFieldStatus.Missing => T("Dashboard.PathNotFound"),
                _ => T("Dashboard.PathNotSet")
            };
            ToolTipService.SetToolTip(icon, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(icon, tip);
            if (expectFile)
            {
                DashboardRunLauncherButton.IsEnabled = ready;
            }
        }
    }

    private WorkspaceRepository? FindRepository(string id)
        => _repositories.FirstOrDefault(repository => string.Equals(repository.Id, id, StringComparison.Ordinal));

    private string DescribeRepositorySource(WorkspaceRepository repository)
    {
        // The effective values include the built-in defaults the online page also falls back to.
        string game = GetEffectiveOnlineGameName(repository).Trim();
        string category = GetEffectiveOnlineCategoryId(repository).Trim();
        string source = string.IsNullOrWhiteSpace(category)
            ? string.Empty
            : $"{GetEffectiveOnlineSource(repository)} {category}";
        string[] parts = [.. new[] { game, source }.Where(part => !string.IsNullOrWhiteSpace(part))];
        return parts.Length == 0 ? T("Dashboard.NoOnlineCategory") : string.Join(" · ", parts);
    }

    // Until the background scan has counted a repository, readiness comes from a quick
    // existence check and the counts read "counting".
    private RepositoryContentSummary? GetKnownRepositorySummary(WorkspaceRepository repository)
        => _repositorySummaries.TryGetValue(repository.Id, out RepositoryContentSummary summary) ? summary : null;

    private bool IsRepositoryReady(WorkspaceRepository repository)
        => GetKnownRepositorySummary(repository)?.IsReady
            ?? (PathFieldStatuses.Evaluate(repository.SourcePath, expectFile: false) == PathFieldStatus.Ready
                && PathFieldStatuses.Evaluate(repository.TargetPath, expectFile: false) == PathFieldStatus.Ready);

    private string DescribeRepositoryCounts(WorkspaceRepository repository)
        => GetKnownRepositorySummary(repository) is { } summary
            ? T("Dashboard.CharactersAndMods", summary.CategoryCount, summary.ModCount)
            : T("Dashboard.Counting");

    private void ApplyRepositorySummary(RepositoryCardViewModel card, WorkspaceRepository repository)
    {
        card.IsReady = IsRepositoryReady(repository);
        card.StatusText = card.IsReady ? T("Dashboard.Ready") : T("Dashboard.SetupNeeded");
        card.Summary = DescribeRepositoryCounts(repository);
    }

    private void RefreshDashboardCurrentStatus()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        Dashboard.CurrentIsReady = repository is not null && IsRepositoryReady(repository);
        Dashboard.CurrentStatusText = Dashboard.CurrentIsReady ? T("Dashboard.Ready") : T("Dashboard.SetupNeeded");
        Dashboard.CurrentSummary = repository is null ? string.Empty : DescribeRepositoryCounts(repository);
        RefreshDashboardCharacters(repository);
    }

    // The overview is rebuilt only when its content changes, so refreshes do not flicker it.
    private string? _dashboardCharactersKey;

    private void RefreshDashboardCharacters(WorkspaceRepository? repository)
    {
        RepositoryContentSummary? summary = repository is null ? null : GetKnownRepositorySummary(repository);
        string emptyText = repository is null ? string.Empty
            : summary is null ? T("Dashboard.Counting")
            : !summary.Value.SourceReady ? T("Dashboard.SetSourceToSeeCharacters")
            : T("Dashboard.NoCharacters");
        IReadOnlyList<RepositoryCategorySummary> categories = summary?.Categories ?? [];
        string key = $"{_currentLanguage}|{repository?.Id}|{emptyText}|"
            + string.Join("/", categories.Select(category => category.Path + ":" + category.ModCount));
        if (key == _dashboardCharactersKey)
        {
            return;
        }
        _dashboardCharactersKey = key;
        Dashboard.SetCharacters(
            categories.Select(category => new CharacterChipViewModel(
                category.Name, category.Path, T("Dashboard.CharacterMods", category.ModCount), OpenCharacterFromDashboardAsync)
            {
                Avatar = FindLoadedCharacter(category.Path)?.AvatarImageSource
            }),
            emptyText);
    }

    private FirstLevelFolderItem? FindLoadedCharacter(string path)
        => _allFirstLevelItems.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

    // Avatars load after the workspace lists; a chip already on the dashboard picks its image up here.
    private void ApplyDashboardCharacterAvatar(FirstLevelFolderItem item)
    {
        foreach (CharacterChipViewModel chip in Dashboard.Characters)
        {
            if (string.Equals(chip.Path, item.Path, StringComparison.OrdinalIgnoreCase))
            {
                chip.Avatar = item.AvatarImageSource;
            }
        }
    }

    // Opens the Mod workspace on the clicked character; the lists are reloaded first because the
    // dashboard can switch repositories without loading the new one's folders.
    private async Task OpenCharacterFromDashboardAsync(string path)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Repository;
        ApplyShellState(refreshRepository: false);
        FirstLevelSearchTextBox.Text = string.Empty;
        await RefreshListsAsync();
        SelectFirstLevelByPath(path);
    }

    private void OnDashboardCharacterClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterChipViewModel character)
        {
            character.OpenCommand.Execute(null);
        }
    }

    private void OnDashboardBrowseOnlineClicked(object sender, RoutedEventArgs e) => OnOnlineNavClicked(sender, e);

    private void OnDashboardCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        NavigateToPrimarySection(PrimarySection.Updates);
        OnCheckModUpdatesClicked(sender, e);
    }

    private async Task ScanRepositorySummariesAsync()
    {
        int generation = ++_repositoryScanGeneration;
        (string Id, string? Source, string? Target)[] targets =
            [.. _repositories.Select(repository => (repository.Id, (string?)repository.SourcePath, (string?)repository.TargetPath))];
        try
        {
            Dictionary<string, RepositoryContentSummary> results = await Task.Run(() => targets.ToDictionary(
                target => target.Id,
                target => RepositoryContentSummary.Scan(target.Source, target.Target),
                StringComparer.Ordinal));
            if (generation != _repositoryScanGeneration)
            {
                return;
            }

            _repositorySummaries.Clear();
            foreach ((string id, RepositoryContentSummary summary) in results)
            {
                _repositorySummaries[id] = summary;
            }
            foreach (RepositoryCardViewModel card in Dashboard.Repositories)
            {
                if (FindRepository(card.Id) is { } repository)
                {
                    ApplyRepositorySummary(card, repository);
                }
            }
            RefreshDashboardCurrentStatus();
            RefreshTrayStatus();
        }
        catch (Exception ex)
        {
            LogApplicationIssue("ScanRepositorySummaries", ex);
        }
    }

    private void PopulateDashboardRepositorySwitcher()
    {
        _isApplyingDashboardRepositorySelection = true;
        try
        {
            DashboardRepositoryComboBox.Items.Clear();
            foreach (WorkspaceRepository repository in _repositories)
            {
                DashboardRepositoryComboBox.Items.Add(new ComboBoxItem
                {
                    Content = repository.Name,
                    Tag = repository.Id
                });
            }

            DashboardRepositoryComboBox.SelectedItem = DashboardRepositoryComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _selectedRepositoryId, StringComparison.Ordinal));
        }
        finally
        {
            _isApplyingDashboardRepositorySelection = false;
        }
    }

    // "New repository" offers a blank repository and the built-in game presets; a preset that
    // already has a repository is marked and switches to it instead of creating a second one.
    private void RefreshNewRepositoryMenu()
    {
        NewRepositoryMenu.Items.Clear();
        var blank = new MenuFlyoutItem { Text = T("Dashboard.BlankRepository"), Icon = new FontIcon { Glyph = "" } };
        blank.Click += OnAddRepositoryDetailsClicked;
        NewRepositoryMenu.Items.Add(blank);
        NewRepositoryMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (RepositoryPresetInfo preset in BuiltInRepositoryPresets)
        {
            string name = _currentLanguage == AppLanguage.ZhCn ? preset.DisplayNameZh : preset.DisplayNameEn;
            WorkspaceRepository? existing = FindRepositoryForPreset(preset);
            var item = new MenuFlyoutItem
            {
                Text = existing is null ? name : T("Dashboard.PresetAlreadyAdded", name),
                Tag = preset.Id,
                Icon = new FontIcon { Glyph = existing is null ? "" : "" }
            };
            ToolTipService.SetToolTip(item, $"GameBanana · {preset.CategoryId}");
            item.Click += OnRepositoryPresetClicked;
            NewRepositoryMenu.Items.Add(item);
        }
    }

    private WorkspaceRepository? FindRepositoryForPreset(RepositoryPresetInfo preset)
    {
        return _repositories.FirstOrDefault(repository =>
            string.Equals(repository.OnlineSourceSite, DefaultOnlineSourceSite, StringComparison.OrdinalIgnoreCase)
            && IsRepositoryForPreset(repository, preset));
    }

    private static bool IsRepositoryForPreset(WorkspaceRepository repository, RepositoryPresetInfo preset)
    {
        string categoryId = repository.OnlineCategoryId?.Trim() ?? string.Empty;
        string gameName = repository.OnlineGameName?.Trim() ?? string.Empty;
        return string.Equals(categoryId, preset.CategoryId, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(preset.Id, "genshin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(categoryId, "17510", StringComparison.OrdinalIgnoreCase))
            || string.Equals(gameName, preset.DisplayNameEn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(gameName, preset.DisplayNameZh, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EditRepositoryFromCardAsync(string id)
    {
        if (!string.Equals(id, _selectedRepositoryId, StringComparison.Ordinal))
        {
            SwitchRepository(id, openManager: false);
        }
        await EditSelectedRepositoryAsync();
    }

    private async Task DeleteRepositoryFromCardAsync(string id)
    {
        if (FindRepository(id) is { } repository)
        {
            await DeleteRepositoryAsync(repository);
        }
    }

    private static string TrimPreviewPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        return path.Length > 48 ? "..." + path[^45..] : path;
    }

    private void OnDashboardNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Dashboard;
        ApplyShellState(refreshRepository: false);
    }

    private void OnDashboardRepositorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingDashboardRepositorySelection
            || DashboardRepositoryComboBox.SelectedItem is not ComboBoxItem { Tag: string repositoryId })
        {
            return;
        }

        SwitchRepository(repositoryId, openManager: false);
    }

    private void OnDashboardOpenRepositoryClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedRepositoryId is not null)
        {
            SwitchRepository(_selectedRepositoryId, openManager: true);
        }
    }
}
