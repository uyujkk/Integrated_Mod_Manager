using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What the Mod workspace page uses from the window (stage J7, decided 2026-11-10). State slots, preset pictures
/// (stage J finish, step 6), combination presets (step 7a) and the scan, status line and path actions (step 7b) have
/// since become services in AppServices, which the page uses directly; the window creates the page the first time the
/// workspace is shown. Bindings, links and the character rename references followed in step 8b (ShortcutBindingService,
/// CharacterRenameReferences). See UI-Structure-Plan.md, "J7 后续".
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

    // Repositories: the selected one, switching, editing and the deployment mode switch.
    void OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e);
    void SwitchRepository(string repositoryId, bool openManager);

    // State the workspace shares with the dashboard, the Shortcuts page and the overlay.
    string[] ImageExtensions { get; }
    bool EnableConflictDetection { get; }
    bool IsApplyingRepositoryDeploymentMode { get; }

    // Workspace timing diagnostics.
    RepositoryPerformanceRecorder.Scope? MeasureRepositoryUi(RepositoryPerformanceOperation operation, int count);
}
