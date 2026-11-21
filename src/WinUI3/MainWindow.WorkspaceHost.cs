using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

// The window's side of the Mod workspace page (stage J7): what the page asks of the window, forwarded to it, the page
// button that switches the window to the preset view, and the window's half of a preset operation's busy state.
public sealed partial class MainWindow : IWorkspaceHost
{
    private void AttachWorkspacePageEvents()
    {
        _workspaceView.OpenCombinationPresetsButton.Click += OnOpenCombinationPresetsClicked;
    }

    FrameworkElement IWorkspaceHost.Root => RootGrid;
    Windows.Foundation.Rect IWorkspaceHost.Bounds => Bounds;
    object IWorkspaceHost.OnlineNavButton => OnlineNavButton;
    void IWorkspaceHost.AttachTrackedTextInput(TextBox box) => AttachTrackedTextInput(box);
    void IWorkspaceHost.OnOnlineNavClicked(object sender, RoutedEventArgs e) => OnOnlineNavClicked(sender, e);

    (List<FirstLevelFolderItem> Items, int SecondCount) IWorkspaceHost.LoadFolderItemsUsingIndex(string repositoryId, string sourceDir, string targetDir, CancellationToken cancellationToken, out int warningCount) => LoadFolderItemsUsingIndex(repositoryId, sourceDir, targetDir, cancellationToken, out warningCount);
    void IWorkspaceHost.LogApplicationIssue(string operation, Exception exception) => LogApplicationIssue(operation, exception);
    void IWorkspaceHost.ReportUiActionFailure(string operation, Exception exception) => ReportUiActionFailure(operation, exception);
    Task IWorkspaceHost.RunUiActionAsync(string operation, Func<Task> action) => RunUiActionAsync(operation, action);

    Task IWorkspaceHost.ShowImageViewerAsync(ImageSource? source, string title, IReadOnlyList<string>? imageUrls, int selectedIndex) => ShowImageViewerAsync(source, title, imageUrls, selectedIndex);

    bool IWorkspaceHost.SaveConfig() => SaveConfig();
    void IWorkspaceHost.SaveShellConfig(bool throwOnError) => SaveShellConfig(throwOnError);

    void IWorkspaceHost.ApplyShortcutPlaceholders() => ApplyShortcutPlaceholders();
    void IWorkspaceHost.RefreshLocalizedStates() => RefreshLocalizedStates();

    void IWorkspaceHost.OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e) => OnEditRepositoryDetailsClicked(sender, e);
    void IWorkspaceHost.SwitchRepository(string repositoryId, bool openManager) => SwitchRepository(repositoryId, openManager);

    string[] IWorkspaceHost.ImageExtensions => ImageExtensions;
    int IWorkspaceHost.ShortcutScanSafetyLimit => ShortcutScanSafetyLimit;
    CharacterListViewModel IWorkspaceHost.Characters => _characters;
    string? IWorkspaceHost.CurrentSecondLevelPath { get => _currentSecondLevelPath; set => _currentSecondLevelPath = value; }
    bool IWorkspaceHost.EnableConflictDetection => _enableConflictDetection;
    CancellationTokenSource? IWorkspaceHost.FolderRefreshCancellation { get => _folderRefreshCancellation; set => _folderRefreshCancellation = value; }
    int IWorkspaceHost.FolderRefreshGeneration { get => _folderRefreshGeneration; set => _folderRefreshGeneration = value; }
    bool IWorkspaceHost.IsApplyingRepositoryDeploymentMode => _isApplyingRepositoryDeploymentMode;
    Dictionary<string, List<ShortcutBinding>> IWorkspaceHost.ModBindings => _modBindings;
    Dictionary<string, string> IWorkspaceHost.ModLinks => _modLinks;
    BatchObservableCollection<SecondLevelFolderItem> IWorkspaceHost.SecondLevelItems => _secondLevelItems;
    List<TextBox> IWorkspaceHost.ShortcutActionBoxes => _shortcutActionBoxes;
    List<TextBox> IWorkspaceHost.ShortcutKeyBoxes => _shortcutKeyBoxes;

    ICharacterRenameReferences IWorkspaceHost.CreateCharacterRenameReferences(WorkspaceRepository repository) => new CharacterRenameReferences(this, repository);




    string IWorkspaceHost.GetShortcutBindingDisplayAction(ShortcutBinding binding) => GetShortcutBindingDisplayAction(binding);
    string IWorkspaceHost.NormalizeLink(string link) => NormalizeLink(link);
    Task<int> IWorkspaceHost.TryImportShortcutBindingsFromModFilesAsync(string modFolder) => TryImportShortcutBindingsFromModFilesAsync(modFolder);
    Task IWorkspaceHost.RefreshShortcutDeployedModsAsync(string? preferredPath) => RefreshShortcutDeployedModsAsync(preferredPath);

    Task IWorkspaceHost.RunRepositoryLauncherAsync() => RunRepositoryLauncherAsync();
    void IWorkspaceHost.ApplyDashboardCharacterAvatar(FirstLevelFolderItem item) => ApplyDashboardCharacterAvatar(item);

    RepositoryPerformanceRecorder.Scope? IWorkspaceHost.MeasureRepositoryUi(RepositoryPerformanceOperation operation, int count) => MeasureRepositoryUi(operation, count);

    // The slot card is the workspace page's (stage J finish, step 6); the window and the services ask it to redraw.
    private void RefreshPersistentSlotControls() => _workspaceView.RefreshPersistentSlotControls();

    // Combination presets are a service and their card is the workspace page's (stage J finish, step 7); the window
    // locks its navigation and path boxes while one is saved, restored or bundled.
    private void AttachCombinationService()
    {
        _services.Combinations.Attach(() => SaveShellConfig());
        _services.Combinations.BusyChanged += OnCombinationBusyChanged;
    }

    private void OnCombinationBusyChanged(object? sender, EventArgs e)
    {
        bool busy = _services.Combinations.IsApplying;
        ShellNavigationView.IsHitTestVisible = !busy;
        foreach (NavigationViewItem item in new[] { DashboardNavButton, RepositoryNavButton, OnlineNavButton, UpdatesNavButton, SettingsNavButton })
            item.IsEnabled = !busy;
        Paths.ArePathsEditable = !busy;
    }

    private void OnOpenCombinationPresetsClicked(object sender, RoutedEventArgs e)
    {
        if (!_services.Combinations.IsApplying && !_services.Slots.IsRestoring)
        {
            NavigateToPrimarySection(PrimarySection.Repository);
            _workspaceView.SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView.Presets);
        }
    }
}
