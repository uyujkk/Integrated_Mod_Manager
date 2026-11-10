using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Shortcuts;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// What the Online page uses from the window (stage J6, decided 2026-11-09: one interface instead of new services for
/// now). A later stage splits it into a character catalogue, an online source and an install flow service; see
/// UI-Structure-Plan.md, "J6 后续".
/// </summary>
internal interface IOnlineHost
{
    // The shell: whether the page is shown, theme brushes, density and motion, pickers, links and the image viewer.
    bool IsOnlinePageShown { get; }
    FrameworkElement Root { get; }
    Windows.Foundation.Rect Bounds { get; }
    // The density name, part of the page's layout cache key.
    string Density { get; }
    bool ReduceMotion { get; }
    void AnimateImageReveal(UIElement image);
    Task ShowImageViewerAsync(ImageSource? source, string title, IReadOnlyList<string>? imageUrls = null, int selectedIndex = 0);

    // Errors and the error log.
    void LogApplicationIssue(string operation, Exception exception);
    void ReportUiActionFailure(string operation, Exception exception);
    Task RunUiActionAsync(string operation, Func<Task> action);

    // Saving the settings (beta-shell.json) and config.ini.
    void SaveShellConfig(bool throwOnError = false);
    bool SaveConfig();

    // The repository and the Mod workspace: the selected repository, refreshing and selecting after an install.
    Task RefreshListsAsync();
    void SelectSecondLevelByPath(string? path);
    void SetBusyState(bool busy);
    bool IsFavoriteCharacter(string characterKey);
    void ToggleFavoriteCharacter(string characterKey);

    // Shortcut bindings and links of installed Mods, which an install fills in.
    Dictionary<string, List<ShortcutBinding>> ModBindings { get; }
    Dictionary<string, string> ModLinks { get; }
    ShortcutBinding CreateAutomaticShortcutBinding(DetectedModShortcut shortcut);

    // Tracked Mods, download tasks and the Updates page.
    Dictionary<string, TrackedModOrigin> TrackedModOrigins { get; }
    List<TrackedModUpdateResult> TrackedModUpdateResults { get; }
    DownloadTaskItem CreateDownloadTask(OnlineModCard mod, string destinationPath);
    void UpdateDownloadTask(DownloadTaskItem task, DownloadTaskState state, double progress, UiMessage status, bool forceRefresh = false);
    void RefreshDownloadTaskCard(DownloadTaskItem task);
    void RefreshUpdatesPane();
    void SetModUpdateStatus(UiMessage status);
    void ExtractArchiveToDirectory(string archivePath, string destinationDirectory);

    // The HTTP client, the local index and the online metadata service the window creates at startup.
    HttpClient Http { get; }
    AppDataStore DataStore { get; }
    GameBananaMetadataService Metadata { get; }

    // Shared values and helpers of the window.
    int ShortcutScanSafetyLimit { get; }
    IReadOnlyList<RepositoryPresetInfo> BuiltInRepositoryPresets { get; }
    IReadOnlyList<string> ImageExtensions { get; }
    bool IsRepositoryForPreset(WorkspaceRepository repository, RepositoryPresetInfo preset);
    bool IsSupportedArchiveFile(string path);
    string? FindPreviewImage(List<string> files);
    void DeleteDirectoryTreeSafely(string path);
}
