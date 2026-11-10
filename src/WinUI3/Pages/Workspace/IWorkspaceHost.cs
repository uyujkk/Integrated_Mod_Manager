using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Presets;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What the Mod workspace page uses from the window (stage J7, decided 2026-11-10: the interface moves; the state slot,
/// preset preview and combination preset logic stays with the window for now). A later stage splits it into slot,
/// preset preview and combination preset services; see UI-Structure-Plan.md, "J7 后续".
/// </summary>
internal interface IWorkspaceHost
{
    // The shell: the root element, window size, navigation, theme brushes, motion and tracked text boxes.
    FrameworkElement Root { get; }
    Windows.Foundation.Rect Bounds { get; }
    object OnlineNavButton { get; }
    void AttachTrackedTextInput(TextBox box);
    Brush GetAppThemeBrush(string resourceKey);
    void OnOnlineNavClicked(object sender, RoutedEventArgs e);

    // Errors, the error log and listing a repository through the local index.
    (List<FirstLevelFolderItem> Items, int SecondCount) LoadFolderItemsUsingIndex(string repositoryId, string sourceDir, string targetDir, CancellationToken cancellationToken, out int warningCount);
    void LogApplicationIssue(string operation, Exception exception);
    bool ShouldReduceAnimations { get; }
    void ReportUiActionFailure(string operation, Exception exception);
    Task RunUiActionAsync(string operation, Func<Task> action);

    // Pickers, folders and the image viewer.
    void OpenDirectory(string? directory, string title);
    void OpenFileLocation(string? filePath, string title);
    Task<string?> PickFileAsync(string[] extensions);
    Task<string?> PickFolderAsync(string? preferredPath = null);
    Task ShowImageViewerAsync(ImageSource? source, string title, IReadOnlyList<string>? imageUrls = null, int selectedIndex = 0);

    // Saving the settings (beta-shell.json) and config.ini.
    bool SaveConfig();
    void SaveShellConfig(bool throwOnError = false);

    // Labels the window keeps for shared controls.
    void ApplyShortcutPlaceholders();
    void RefreshLocalizedStates();

    // Repositories: the selected one, switching, editing and the deployment mode switch.
    WorkspaceRepository? GetSelectedRepository();
    void OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e);
    void SwitchRepository(string repositoryId, bool openManager);

    // State the workspace shares with the dashboard, the Shortcuts page and the overlay.
    string[] ImageExtensions { get; }
    int ShortcutScanSafetyLimit { get; }
    IReadOnlyList<string> SupportedArchiveExtensions { get; }
    CharacterListViewModel Characters { get; }
    List<ModConfigurationProfile> ConfigurationProfiles { get; }
    string? CurrentSecondLevelPath { get; set; }
    bool EnableConflictDetection { get; }
    CancellationTokenSource? FolderRefreshCancellation { get; set; }
    int FolderRefreshGeneration { get; set; }
    bool IsApplyingRepositoryDeploymentMode { get; }
    Dictionary<string, List<ShortcutBinding>> ModBindings { get; }
    Dictionary<string, string> ModLinks { get; }
    OnlinePage OnlineView { get; }
    BatchObservableCollection<SecondLevelFolderItem> SecondLevelItems { get; }
    string? SelectedConfigurationProfileId { get; }
    List<TextBox> ShortcutActionBoxes { get; }
    List<TextBox> ShortcutKeyBoxes { get; }
    Dictionary<string, TrackedModOrigin> TrackedModOrigins { get; }
    IReadOnlyList<EndfieldCharacterInfo> EndfieldCharacters { get; }

    // Install transactions: conflicts, backups, commit and rollback.
    void CommitInstallTransaction(string transactionPath);
    Task<bool> ConfirmInstallConflictsAsync(IReadOnlyList<string> fileConflicts, IReadOnlyList<(string? Candidate, ModIniConflictSummary Summary)> iniConflicts);
    void CopyDirectoryTree(string sourcePath, string targetPath);
    Task<string> CreateInstallTransactionAsync(string description, WorkspaceRepository repository, IEnumerable<string> affectedTargets);
    void DeleteDirectoryTreeSafely(string path);
    List<string> DetectModFileConflicts(string sourcePath, string targetRoot, string targetPathToIgnore);
    List<string> DetectModFileConflicts(string sourcePath, string targetRoot, IEnumerable<string> targetPathsToIgnore);
    string GetSafePathInsideDirectory(string rootPath, string relativePath);
    (List<string> Directories, List<string> Files) InspectDirectoryTreeSafely(string rootPath);
    Task<bool> OfferBrokenLinkCleanupAsync(string loaderRoot);
    Task RollbackInstallTransactionAsync(string transactionPath, bool clearLastTransaction);

    // INI conflict detection.
    IReadOnlyList<ModIniConflictSummary> DetectModIniConflictsSafely(string candidateDirectory, string targetRoot, IEnumerable<string> ignoredTargets);

    // Archives: supported formats and importing their contents.
    string ImportArchiveContents(string archivePath, string firstLevelDirectory);
    bool IsSupportedArchiveFile(string path);

    // Copy ownership records (#107).
    CopyDeploymentLedger CopyLedger(WorkspaceRepository repository);
    Func<string, string, bool> OwnsCopy(WorkspaceRepository repository);
    void SaveCopyRecordsIfChanged();

    // Character rename migration (#108).
    ICharacterRenameReferences CreateCharacterRenameReferences(WorkspaceRepository repository);
    string CharacterRenameBlockText(CharacterRenamePlan plan, WorkspaceRepository repository);

    // State slots and default slots.
    void RefreshPersistentSlotControls();
    Task SeedDefaultSlotsAsync(WorkspaceRepository repository, IReadOnlyList<FirstLevelFolderItem> characters);

    // Preset previews.
    FrameworkElement CreatePresetPreviewEditor(string repositoryId, string presetId, string? previewFile, Action<string?> assign, Action refresh);
    Image? CreatePresetPreviewImage(string? previewFile, double width, double height);

    // Combination presets and bundles.
    IEnumerable<ModConfigurationProfile> GetCurrentRepositoryProfiles();
    ModConfigurationProfile? GetSelectedConfigurationProfile();
    void RefreshConfigurationProfiles();
    Task ExportModsAsBundleAsync(string root, string name, string game, bool includeState, CombinationPresetState[] mods, string confirmation);
    void InitializeCombinationBundleActions();

    // Shortcut binding rules shared with the online install, and the Shortcuts page.
    string GetShortcutBindingDisplayAction(ShortcutBinding binding);
    string NormalizeLink(string link);
    Task<int> TryImportShortcutBindingsFromModFilesAsync(string modFolder);
    Task RefreshShortcutDeployedModsAsync(string? preferredPath = null);

    // Running the repository's launcher, the dashboard's avatars and the Online page.
    Task RunRepositoryLauncherAsync();
    void ApplyDashboardCharacterAvatar(FirstLevelFolderItem item);

    // Workspace timing diagnostics.
    RepositoryPerformanceRecorder.Scope? MeasureRepositoryUi(RepositoryPerformanceOperation operation, int count);
}
