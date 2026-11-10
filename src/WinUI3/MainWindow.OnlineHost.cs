using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Shortcuts;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

// The window's side of the Online page (stage J6): what the page asks of the window, forwarded to it.
public sealed partial class MainWindow : IOnlineHost
{
    // Normally the plain client as before. IMM_ONLINE_FIXTURES (screenshots and UI tests only) answers every request
    // from synthetic samples instead and sends nothing to the network.
    private static HttpClient CreateHttpClient()
    {
        string? fixtures = Environment.GetEnvironmentVariable(OnlineHttpClientFactory.FixturesVariable);
        if (!string.IsNullOrWhiteSpace(fixtures))
            LogApplicationIssue("Online", new InvalidOperationException($"{OnlineFixtureHandler.ModeName} is on: {fixtures}"));
        return OnlineHttpClientFactory.Create($"IntegratedModManager/{AppVersion.TrimStart('v', 'V')}", fixtures,
            message => LogApplicationIssue("Online", new InvalidOperationException(message)));
    }

    bool IOnlineHost.IsOnlinePageShown => _currentPrimarySection == PrimarySection.Online;
    FrameworkElement IOnlineHost.Root => RootGrid;
    Windows.Foundation.Rect IOnlineHost.Bounds => Bounds;
    Brush IOnlineHost.GetAppThemeBrush(string resourceKey) => GetAppThemeBrush(resourceKey);
    string IOnlineHost.Density => _interfaceDensity.ToString();
    bool IOnlineHost.ReduceMotion => _reduceMotion;
    bool IOnlineHost.ShouldReduceAnimations => ShouldReduceAnimations;
    void IOnlineHost.AnimateImageReveal(UIElement image) => AnimateImageReveal(image);
    Task<string?> IOnlineHost.PickFolderAsync(string? preferredPath) => PickFolderAsync(preferredPath);
    Task IOnlineHost.OpenExternalUrlAsync(string? url, string failureTitle) => OpenExternalUrlAsync(url, failureTitle);
    Task IOnlineHost.ShowImageViewerAsync(ImageSource? source, string title, IReadOnlyList<string>? imageUrls, int selectedIndex)
        => ShowImageViewerAsync(source, title, imageUrls, selectedIndex);

    void IOnlineHost.LogApplicationIssue(string operation, Exception exception) => LogApplicationIssue(operation, exception);
    void IOnlineHost.ReportUiActionFailure(string operation, Exception exception) => ReportUiActionFailure(operation, exception);
    Task IOnlineHost.RunUiActionAsync(string operation, Func<Task> action) => RunUiActionAsync(operation, action);

    void IOnlineHost.SaveShellConfig(bool throwOnError) => SaveShellConfig(throwOnError);
    bool IOnlineHost.SaveConfig() => SaveConfig();

    WorkspaceRepository? IOnlineHost.GetSelectedRepository() => GetSelectedRepository();
    Task IOnlineHost.RefreshListsAsync() => RefreshListsAsync();
    void IOnlineHost.SelectSecondLevelByPath(string? path) => SelectSecondLevelByPath(path);
    void IOnlineHost.SetBusyState(bool busy) => SetBusyState(busy);
    bool IOnlineHost.IsFavoriteCharacter(string characterKey) => IsFavoriteCharacter(characterKey);
    void IOnlineHost.ToggleFavoriteCharacter(string characterKey) => ToggleFavoriteCharacter(characterKey);

    Dictionary<string, List<ShortcutBinding>> IOnlineHost.ModBindings => _modBindings;
    Dictionary<string, string> IOnlineHost.ModLinks => _modLinks;
    ShortcutBinding IOnlineHost.CreateAutomaticShortcutBinding(DetectedModShortcut shortcut) => CreateAutomaticShortcutBinding(shortcut);

    Dictionary<string, TrackedModOrigin> IOnlineHost.TrackedModOrigins => _trackedModOrigins;
    List<TrackedModUpdateResult> IOnlineHost.TrackedModUpdateResults => _trackedModUpdateResults;
    DownloadTaskItem IOnlineHost.CreateDownloadTask(OnlineModCard mod, string destinationPath) => CreateDownloadTask(mod, destinationPath);
    void IOnlineHost.UpdateDownloadTask(DownloadTaskItem task, DownloadTaskState state, double progress, UiMessage status, bool forceRefresh)
        => UpdateDownloadTask(task, state, progress, status, forceRefresh);
    void IOnlineHost.RefreshDownloadTaskCard(DownloadTaskItem task) => RefreshDownloadTaskCard(task);
    void IOnlineHost.RefreshUpdatesPane() => RefreshUpdatesPane();
    void IOnlineHost.SetModUpdateStatus(UiMessage status) => SetModUpdateStatus(status);
    void IOnlineHost.ExtractArchiveToDirectory(string archivePath, string destinationDirectory) => ExtractArchiveToDirectory(archivePath, destinationDirectory);

    HttpClient IOnlineHost.Http => _httpClient;
    AppDataStore IOnlineHost.DataStore => _appDataStore;
    GameBananaMetadataService IOnlineHost.Metadata => _gameBananaMetadataService;

    string IOnlineHost.DefaultOnlineCategoryId => DefaultOnlineCategoryId;
    string IOnlineHost.DefaultOnlineSourceSite => DefaultOnlineSourceSite;
    string IOnlineHost.EndfieldOfficialWikiUrl => EndfieldOfficialWikiUrl;
    int IOnlineHost.ShortcutScanSafetyLimit => ShortcutScanSafetyLimit;
    IReadOnlyList<RepositoryPresetInfo> IOnlineHost.BuiltInRepositoryPresets => BuiltInRepositoryPresets;
    IReadOnlyList<EndfieldCharacterInfo> IOnlineHost.EndfieldCharacters => EndfieldCharacters;
    IReadOnlyList<string> IOnlineHost.ImageExtensions => ImageExtensions;
    bool IOnlineHost.IsRepositoryForPreset(WorkspaceRepository repository, RepositoryPresetInfo preset) => IsRepositoryForPreset(repository, preset);
    bool IOnlineHost.IsSupportedArchiveFile(string path) => IsSupportedArchiveFile(path);
    string? IOnlineHost.FindPreviewImage(List<string> files) => FindPreviewImage(files);
    void IOnlineHost.DeleteDirectoryTreeSafely(string path) => DeleteDirectoryTreeSafely(path);
}
