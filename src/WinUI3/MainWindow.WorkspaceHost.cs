using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

// The window's side of the Mod workspace page (stage J7): what the page asks of the window, forwarded to it, and the
// page controls whose handlers are window logic (state slots and combination presets).
public sealed partial class MainWindow : IWorkspaceHost
{
    private void AttachWorkspacePageEvents()
    {
        _workspaceView.BrowsePersistentUserIniButton.Click += OnBrowsePersistentUserIniClicked;
        _workspaceView.PersistentSlotComboBox.SelectionChanged += OnPersistentSlotSelectionChanged;
        _workspaceView.CapturePersistentSlotButton.Click += OnCapturePersistentSlotClicked;
        _workspaceView.DeletePersistentSlotButton.Click += OnDeletePersistentSlotClicked;
        _workspaceView.RestorePersistentRuntimeButton.Click += OnRestorePersistentRuntimeClicked;
        _workspaceView.OpenCombinationPresetsButton.Click += OnOpenCombinationPresetsClicked;
        _workspaceView.ConfigurationProfileComboBox.SelectionChanged += OnConfigurationProfileSelectionChanged;
        _workspaceView.BrowseCombinationUserIniButton.Click += OnBrowsePersistentUserIniClicked;
        _workspaceView.CreateConfigurationProfileButton.Click += OnCreateConfigurationProfileClicked;
        _workspaceView.UpdateConfigurationProfileButton.Click += OnUpdateConfigurationProfileClicked;
        _workspaceView.ApplyConfigurationProfileButton.Click += OnApplyConfigurationProfileClicked;
        _workspaceView.DeleteConfigurationProfileButton.Click += OnDeleteConfigurationProfileClicked;
    }

    FrameworkElement IWorkspaceHost.Root => RootGrid;
    Windows.Foundation.Rect IWorkspaceHost.Bounds => Bounds;
    object IWorkspaceHost.OnlineNavButton => OnlineNavButton;
    void IWorkspaceHost.AttachTrackedTextInput(TextBox box) => AttachTrackedTextInput(box);
    Brush IWorkspaceHost.GetAppThemeBrush(string resourceKey) => GetAppThemeBrush(resourceKey);
    void IWorkspaceHost.OnOnlineNavClicked(object sender, RoutedEventArgs e) => OnOnlineNavClicked(sender, e);

    (List<FirstLevelFolderItem> Items, int SecondCount) IWorkspaceHost.LoadFolderItemsUsingIndex(string repositoryId, string sourceDir, string targetDir, CancellationToken cancellationToken, out int warningCount) => LoadFolderItemsUsingIndex(repositoryId, sourceDir, targetDir, cancellationToken, out warningCount);
    void IWorkspaceHost.LogApplicationIssue(string operation, Exception exception) => LogApplicationIssue(operation, exception);
    bool IWorkspaceHost.ShouldReduceAnimations => ShouldReduceAnimations;
    void IWorkspaceHost.ReportUiActionFailure(string operation, Exception exception) => ReportUiActionFailure(operation, exception);
    Task IWorkspaceHost.RunUiActionAsync(string operation, Func<Task> action) => RunUiActionAsync(operation, action);

    void IWorkspaceHost.OpenDirectory(string? directory, string title) => OpenDirectory(directory, title);
    void IWorkspaceHost.OpenFileLocation(string? filePath, string title) => OpenFileLocation(filePath, title);
    Task<string?> IWorkspaceHost.PickFileAsync(string[] extensions) => PickFileAsync(extensions);
    Task<string?> IWorkspaceHost.PickFolderAsync(string? preferredPath) => PickFolderAsync(preferredPath);
    Task IWorkspaceHost.ShowImageViewerAsync(ImageSource? source, string title, IReadOnlyList<string>? imageUrls, int selectedIndex) => ShowImageViewerAsync(source, title, imageUrls, selectedIndex);

    bool IWorkspaceHost.SaveConfig() => SaveConfig();
    void IWorkspaceHost.SaveShellConfig(bool throwOnError) => SaveShellConfig(throwOnError);

    void IWorkspaceHost.ApplyShortcutPlaceholders() => ApplyShortcutPlaceholders();
    void IWorkspaceHost.RefreshLocalizedStates() => RefreshLocalizedStates();

    WorkspaceRepository? IWorkspaceHost.GetSelectedRepository() => GetSelectedRepository();
    void IWorkspaceHost.OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e) => OnEditRepositoryDetailsClicked(sender, e);
    void IWorkspaceHost.SwitchRepository(string repositoryId, bool openManager) => SwitchRepository(repositoryId, openManager);

    string[] IWorkspaceHost.ImageExtensions => ImageExtensions;
    int IWorkspaceHost.ShortcutScanSafetyLimit => ShortcutScanSafetyLimit;
    IReadOnlyList<string> IWorkspaceHost.SupportedArchiveExtensions => SupportedArchiveExtensions;
    CharacterListViewModel IWorkspaceHost.Characters => _characters;
    List<ModConfigurationProfile> IWorkspaceHost.ConfigurationProfiles => _configurationProfiles;
    string? IWorkspaceHost.CurrentSecondLevelPath { get => _currentSecondLevelPath; set => _currentSecondLevelPath = value; }
    bool IWorkspaceHost.EnableConflictDetection => _enableConflictDetection;
    CancellationTokenSource? IWorkspaceHost.FolderRefreshCancellation { get => _folderRefreshCancellation; set => _folderRefreshCancellation = value; }
    int IWorkspaceHost.FolderRefreshGeneration { get => _folderRefreshGeneration; set => _folderRefreshGeneration = value; }
    bool IWorkspaceHost.IsApplyingRepositoryDeploymentMode => _isApplyingRepositoryDeploymentMode;
    Dictionary<string, List<ShortcutBinding>> IWorkspaceHost.ModBindings => _modBindings;
    Dictionary<string, string> IWorkspaceHost.ModLinks => _modLinks;
    OnlinePage IWorkspaceHost.OnlineView => _onlineView;
    BatchObservableCollection<SecondLevelFolderItem> IWorkspaceHost.SecondLevelItems => _secondLevelItems;
    string? IWorkspaceHost.SelectedConfigurationProfileId => _selectedConfigurationProfileId;
    List<TextBox> IWorkspaceHost.ShortcutActionBoxes => _shortcutActionBoxes;
    List<TextBox> IWorkspaceHost.ShortcutKeyBoxes => _shortcutKeyBoxes;
    Dictionary<string, TrackedModOrigin> IWorkspaceHost.TrackedModOrigins => _trackedModOrigins;
    IReadOnlyList<EndfieldCharacterInfo> IWorkspaceHost.EndfieldCharacters => EndfieldCharacters;

    void IWorkspaceHost.CommitInstallTransaction(string transactionPath) => CommitInstallTransaction(transactionPath);
    Task<bool> IWorkspaceHost.ConfirmInstallConflictsAsync(IReadOnlyList<string> fileConflicts, IReadOnlyList<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts) => ConfirmInstallConflictsAsync(fileConflicts, iniConflicts);
    void IWorkspaceHost.CopyDirectoryTree(string sourcePath, string targetPath) => CopyDirectoryTree(sourcePath, targetPath);
    Task<string> IWorkspaceHost.CreateInstallTransactionAsync(string description, WorkspaceRepository repository, IEnumerable<string> affectedTargets) => CreateInstallTransactionAsync(description, repository, affectedTargets);
    void IWorkspaceHost.DeleteDirectoryTreeSafely(string path) => DeleteDirectoryTreeSafely(path);
    List<string> IWorkspaceHost.DetectModFileConflicts(string sourcePath, string targetRoot, string targetPathToIgnore) => DetectModFileConflicts(sourcePath, targetRoot, targetPathToIgnore);
    List<string> IWorkspaceHost.DetectModFileConflicts(string sourcePath, string targetRoot, IEnumerable<string> targetPathsToIgnore) => DetectModFileConflicts(sourcePath, targetRoot, targetPathsToIgnore);
    string IWorkspaceHost.GetSafePathInsideDirectory(string rootPath, string relativePath) => GetSafePathInsideDirectory(rootPath, relativePath);
    (List<string> Directories, List<string> Files) IWorkspaceHost.InspectDirectoryTreeSafely(string rootPath) => InspectDirectoryTreeSafely(rootPath);
    Task<bool> IWorkspaceHost.OfferBrokenLinkCleanupAsync(string loaderRoot) => OfferBrokenLinkCleanupAsync(loaderRoot);
    Task IWorkspaceHost.RollbackInstallTransactionAsync(string transactionPath, bool clearLastTransaction) => RollbackInstallTransactionAsync(transactionPath, clearLastTransaction);

    IReadOnlyList<ModIniConflictSummary> IWorkspaceHost.DetectModIniConflictsSafely(string candidateDirectory, string targetRoot, IEnumerable<string> ignoredTargets) => DetectModIniConflictsSafely(candidateDirectory, targetRoot, ignoredTargets);

    string IWorkspaceHost.ImportArchiveContents(string archivePath, string firstLevelDirectory) => ImportArchiveContents(archivePath, firstLevelDirectory);
    bool IWorkspaceHost.IsSupportedArchiveFile(string path) => IsSupportedArchiveFile(path);

    CopyDeploymentLedger IWorkspaceHost.CopyLedger(WorkspaceRepository repository) => CopyLedger(repository);
    Func<string, string, bool> IWorkspaceHost.OwnsCopy(WorkspaceRepository repository) => OwnsCopy(repository);
    void IWorkspaceHost.SaveCopyRecordsIfChanged() => SaveCopyRecordsIfChanged();

    ICharacterRenameReferences IWorkspaceHost.CreateCharacterRenameReferences(WorkspaceRepository repository) => new CharacterRenameReferences(this, repository);
    string IWorkspaceHost.CharacterRenameBlockText(CharacterRenamePlan plan, WorkspaceRepository repository) => CharacterRenameBlockText(plan, repository);

    void IWorkspaceHost.RefreshPersistentSlotControls() => RefreshPersistentSlotControls();
    Task IWorkspaceHost.SeedDefaultSlotsAsync(WorkspaceRepository repository, IReadOnlyList<FirstLevelFolderItem> characters) => SeedDefaultSlotsAsync(repository, characters);

    FrameworkElement IWorkspaceHost.CreatePresetPreviewEditor(string repositoryId, string presetId, string? previewFile, Action<string?> assign, Action refresh) => CreatePresetPreviewEditor(repositoryId, presetId, previewFile, assign, refresh);
    Image? IWorkspaceHost.CreatePresetPreviewImage(string? previewFile, double width, double height) => CreatePresetPreviewImage(previewFile, width, height);

    IEnumerable<ModConfigurationProfile> IWorkspaceHost.GetCurrentRepositoryProfiles() => GetCurrentRepositoryProfiles();
    ModConfigurationProfile? IWorkspaceHost.GetSelectedConfigurationProfile() => GetSelectedConfigurationProfile();
    void IWorkspaceHost.RefreshConfigurationProfiles() => RefreshConfigurationProfiles();
    Task IWorkspaceHost.ExportModsAsBundleAsync(string root, string name, string game, bool includeState, CombinationPresetState[] mods, string confirmation) => ExportModsAsBundleAsync(root, name, game, includeState, mods, confirmation);
    void IWorkspaceHost.InitializeCombinationBundleActions() => InitializeCombinationBundleActions();

    string IWorkspaceHost.GetShortcutBindingDisplayAction(ShortcutBinding binding) => GetShortcutBindingDisplayAction(binding);
    string IWorkspaceHost.NormalizeLink(string link) => NormalizeLink(link);
    Task<int> IWorkspaceHost.TryImportShortcutBindingsFromModFilesAsync(string modFolder) => TryImportShortcutBindingsFromModFilesAsync(modFolder);
    Task IWorkspaceHost.RefreshShortcutDeployedModsAsync(string? preferredPath) => RefreshShortcutDeployedModsAsync(preferredPath);

    Task IWorkspaceHost.RunRepositoryLauncherAsync() => RunRepositoryLauncherAsync();
    void IWorkspaceHost.ApplyDashboardCharacterAvatar(FirstLevelFolderItem item) => ApplyDashboardCharacterAvatar(item);

    RepositoryPerformanceRecorder.Scope? IWorkspaceHost.MeasureRepositoryUi(RepositoryPerformanceOperation operation, int count) => MeasureRepositoryUi(operation, count);
}
