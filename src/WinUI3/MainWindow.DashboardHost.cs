using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The window's side of the dashboard (Pages/DashboardPage.xaml): it keeps DashboardViewModel's cards,
// characters and choices in step with the repositories and answers the page's requests.
public sealed partial class MainWindow
{
    private void InitializeDashboardHost()
    {
        Dashboard.ActionRequested += OnDashboardActionRequested;
        _services.EndfieldBridge.LogIssue = LogApplicationIssue;
        _services.EndfieldBridge.BusyChanged += (_, _) =>
        {
            if (!_services.IsClosing) RefreshEndfieldBridgeControls();
        };
        Paths.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RepositoryPathsViewModel.UseDirectoryLinks)) _workspaceView.OnLinkDeploymentChanged();
        };
    }

    private void OnDashboardActionRequested(object? sender, DashboardActionRequest request)
    {
        var args = new RoutedEventArgs();
        switch (request.Action)
        {
            case DashboardAction.SwitchRepository when request.Argument is { } id: SwitchRepository(id, openManager: false); break;
            case DashboardAction.NewRepository when request.Argument is { } presetId: CreateRepositoryFromPreset(presetId); break;
            case DashboardAction.NewRepository: OnAddRepositoryDetailsClicked(this, args); break;
            case DashboardAction.EditRepository: OnEditRepositoryDetailsClicked(this, args); break;
            case DashboardAction.OpenRepository: OpenCurrentRepository(); break;
            case DashboardAction.PickSource: _workspaceView.OnPickSourceClicked(this, args); break;
            case DashboardAction.PickTarget: _workspaceView.OnPickTargetClicked(this, args); break;
            case DashboardAction.PickLauncher: _workspaceView.OnPickLauncherClicked(this, args); break;
            case DashboardAction.OpenSource: _workspaceView.OnOpenSourceClicked(this, args); break;
            case DashboardAction.OpenTarget: _workspaceView.OnOpenTargetClicked(this, args); break;
            case DashboardAction.OpenLauncher: _workspaceView.OnOpenLauncherClicked(this, args); break;
            case DashboardAction.BridgeOptions: OnBridgeOptionsClicked(this, args); break;
            case DashboardAction.BrowseOnline: OnOnlineNavClicked(this, args); break;
            case DashboardAction.CheckModUpdates: CheckModUpdatesFromDashboard(); break;
            case DashboardAction.RunLauncher: _workspaceView.OnRunLauncherClicked(this, args); break;
            case DashboardAction.GettingStartedTipsClosed: OnGettingStartedTipsClosed(); break;
        }
    }

    // Folder counts per repository id from the last background scan. Cards show these
    // (or "counting") at once; a newer scan replaces them without blocking the page.
    private readonly Dictionary<string, RepositoryContentSummary> _repositorySummaries = new(StringComparer.Ordinal);
    private int _repositoryScanGeneration;
    private RepositoryCardActions? _repositoryCardActions;

    // Card commands run from XAML: an exception that gets past here ends the whole process.
    private void SwitchRepositoryFromCard(string id, bool openManager)
    {
        try { SwitchRepository(id, openManager); }
        catch (Exception ex) { ReportUiActionFailure("Switch repository", ex); }
    }

    private void RefreshDashboard()
    {
        if (_services.Repositories.SelectedId is null && _services.Repositories.Items.Count > 0)
        {
            _services.Repositories.SelectedId = _services.Repositories.Items[0].Id;
        }

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
            id => SwitchRepositoryFromCard(id, openManager: false),
            id => SwitchRepositoryFromCard(id, openManager: true),
            EditRepositoryFromCardAsync,
            id =>
            {
                if (FindRepository(id) is { } repository) OpenRepositoryWiki(repository);
            },
            DeleteRepositoryFromCardAsync);

        var ids = new List<string>(_services.Repositories.Items.Count);
        for (int index = 0; index < _services.Repositories.Items.Count; index++)
        {
            WorkspaceRepository repository = _services.Repositories.Items[index];
            ids.Add(repository.Id);
            RepositoryCardViewModel card = Dashboard.GetOrAdd(repository.Id, index,
                () => new RepositoryCardViewModel(repository.Id, _repositoryCardActions));
            card.Name = repository.Name;
            card.Subtitle = DescribeRepositorySource(repository);
            card.IsCurrent = string.Equals(repository.Id, _services.Repositories.SelectedId, StringComparison.Ordinal);
            card.CanDelete = _services.Repositories.Items.Count > 1;
            card.HasWiki = Uri.TryCreate(repository.WikiUrl, UriKind.Absolute, out _);
            card.Labels = labels;
            ApplyRepositorySummary(card, repository);
        }
        Dashboard.RemoveExcept(ids);

        Dashboard.CanOpenRepository = GetSelectedRepository() is not null;
        Dashboard.CurrentRepositoryName = GetSelectedRepository()?.Name ?? string.Empty;
        RefreshDashboardCurrentStatus();
        RefreshTrayStatus();
        Dashboard.NotifyRefreshed();
        _ = ScanRepositorySummariesAsync();
    }

    private WorkspaceRepository? FindRepository(string id)
        => _services.Repositories.Items.FirstOrDefault(repository => string.Equals(repository.Id, id, StringComparison.Ordinal));

    private string DescribeRepositorySource(WorkspaceRepository repository)
    {
        string game = _services.OnlineSource.GameName(repository).Trim();
        string category = _services.OnlineSource.CategoryId(repository).Trim();
        string source = string.IsNullOrWhiteSpace(category)
            ? string.Empty
            : $"{_services.OnlineSource.Source(repository)} {category}";
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
        string key = $"{_services.Language.Current}|{repository?.Id}|{emptyText}|"
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
        => _characters.Find(path);

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
        _workspaceView.FirstLevelSearchTextBox.Text = string.Empty;
        await _workspaceView.RefreshListsAsync();
        _workspaceView.SelectFirstLevelByPath(path);
    }

    private void CheckModUpdatesFromDashboard()
    {
        SaveSelectedRepositoryFromInputs();
        NavigateToPrimarySection(PrimarySection.Updates);
        OnCheckModUpdatesClicked(this, new RoutedEventArgs());
    }

    private async Task ScanRepositorySummariesAsync()
    {
        int generation = ++_repositoryScanGeneration;
        (string Id, string? Source, string? Target)[] targets =
            [.. _services.Repositories.Items.Select(repository => (repository.Id, (string?)repository.SourcePath, (string?)repository.TargetPath))];
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

    // "New repository" offers a blank repository and the built-in game presets; a preset that
    // already has a repository is marked and switches to it instead of creating a second one.
    private void RefreshNewRepositoryMenu()
    {
        var choices = new List<NewRepositoryChoice> { new(null, T("Dashboard.BlankRepository"), "", null) };
        foreach (RepositoryPresetInfo preset in BuiltInRepositoryPresets)
        {
            string name = IsChineseUi ? preset.DisplayNameZh : preset.DisplayNameEn;
            WorkspaceRepository? existing = FindRepositoryForPreset(preset);
            choices.Add(new NewRepositoryChoice(preset.Id,
                existing is null ? name : T("Dashboard.PresetAlreadyAdded", name),
                existing is null ? "" : "",
                $"GameBanana · {preset.CategoryId}"));
        }
        Dashboard.SetNewRepositoryChoices(choices);
    }

    private WorkspaceRepository? FindRepositoryForPreset(RepositoryPresetInfo preset)
    {
        return RepositoryPresetMatching.FindExisting(_services.Repositories.Items, preset, DefaultOnlineSourceSite);
    }

    private static bool IsRepositoryForPreset(WorkspaceRepository repository, RepositoryPresetInfo preset)
        => RepositoryPresetMatching.IsForPreset(repository, preset);

    private async Task EditRepositoryFromCardAsync(string id)
    {
        if (!string.Equals(id, _services.Repositories.SelectedId, StringComparison.Ordinal))
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

    private void OnDashboardNavClicked(object sender, RoutedEventArgs e)
    {
        SaveSelectedRepositoryFromInputs();
        _currentPrimarySection = PrimarySection.Dashboard;
        ApplyShellState(refreshRepository: false);
    }

    private void OpenCurrentRepository()
    {
        if (_services.Repositories.SelectedId is not null)
        {
            SwitchRepository(_services.Repositories.SelectedId, openManager: true);
        }
    }
}
