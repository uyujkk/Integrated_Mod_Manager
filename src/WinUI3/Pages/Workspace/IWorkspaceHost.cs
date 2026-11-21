using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What the Mod workspace page uses from the window (stage J7, decided 2026-11-10). State slots, preset pictures
/// (stage J finish, step 6), combination presets (step 7a) and the scan, status line and path actions (step 7b) have
/// since become services in AppServices, which the page uses directly; the window creates the page the first time the
/// workspace is shown. See UI-Structure-Plan.md, "J7 后续".
/// </summary>
internal interface IWorkspaceHost
{
    // The shell: the root element, window size, navigation, theme brushes, motion and tracked text boxes.
    FrameworkElement Root { get; }
    Windows.Foundation.Rect Bounds { get; }
    object OnlineNavButton { get; }
    void AttachTrackedTextInput(TextBox box);
    void OnOnlineNavClicked(object sender, RoutedEventArgs e);

    // Errors, the error log and listing a repository through the local index.
    void LogApplicationIssue(string operation, Exception exception);
    void ReportUiActionFailure(string operation, Exception exception);
    Task RunUiActionAsync(string operation, Func<Task> action);

    // Pickers, folders and the image viewer.
    Task ShowImageViewerAsync(ImageSource? source, string title, IReadOnlyList<string>? imageUrls = null, int selectedIndex = 0);

    // Saving the settings (beta-shell.json) and config.ini.
    bool SaveConfig();
    void SaveShellConfig(bool throwOnError = false);

    // Labels the window keeps for shared controls.
    void ApplyShortcutPlaceholders();
    void RefreshLocalizedStates();

    // Repositories: the selected one, switching, editing and the deployment mode switch.
    void OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e);
    void SwitchRepository(string repositoryId, bool openManager);

    // State the workspace shares with the dashboard, the Shortcuts page and the overlay.
    string[] ImageExtensions { get; }
    int ShortcutScanSafetyLimit { get; }
    string? CurrentSecondLevelPath { get; set; }
    bool EnableConflictDetection { get; }
    bool IsApplyingRepositoryDeploymentMode { get; }
    Dictionary<string, List<ShortcutBinding>> ModBindings { get; }
    Dictionary<string, string> ModLinks { get; }
    BatchObservableCollection<SecondLevelFolderItem> SecondLevelItems { get; }
    List<TextBox> ShortcutActionBoxes { get; }
    List<TextBox> ShortcutKeyBoxes { get; }

    // Character rename migration (#108): the references the window still owns (the install pipeline is in
    // AppServices.Install since stage J finish, step 4).
    ICharacterRenameReferences CreateCharacterRenameReferences(WorkspaceRepository repository);

    // Shortcut binding rules shared with the online install, and the Shortcuts page.
    string GetShortcutBindingDisplayAction(ShortcutBinding binding);
    string NormalizeLink(string link);
    Task<int> TryImportShortcutBindingsFromModFilesAsync(string modFolder);
    Task RefreshShortcutDeployedModsAsync(string? preferredPath = null);

    // Running the repository's launcher, the dashboard's avatars and the Online page.

    // Workspace timing diagnostics.
    RepositoryPerformanceRecorder.Scope? MeasureRepositoryUi(RepositoryPerformanceOperation operation, int count);
}
