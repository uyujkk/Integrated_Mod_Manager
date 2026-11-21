using IntegratedModManager.Core.Configuration;
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

    bool IOnlineHost.IsOnlinePageShown => _shell.CurrentSection == PrimarySection.Online;
    FrameworkElement IOnlineHost.Root => RootGrid;
    Windows.Foundation.Rect IOnlineHost.Bounds => Bounds;
    string IOnlineHost.Density => _shell.InterfaceDensity.ToString();
    bool IOnlineHost.ReduceMotion => _shell.ReduceMotion;
    void IOnlineHost.AnimateImageReveal(UIElement image) => WorkspacePage.AnimateImageReveal(_services, image);
    Task IOnlineHost.ShowImageViewerAsync(ImageSource? source, string title, IReadOnlyList<string>? imageUrls, int selectedIndex)
        => ShowImageViewerAsync(source, title, imageUrls, selectedIndex);

    void IOnlineHost.LogApplicationIssue(string operation, Exception exception) => LogApplicationIssue(operation, exception);
    void IOnlineHost.ReportUiActionFailure(string operation, Exception exception) => ReportUiActionFailure(operation, exception);
    Task IOnlineHost.RunUiActionAsync(string operation, Func<Task> action) => RunUiActionAsync(operation, action);

    void IOnlineHost.SaveShellConfig(bool throwOnError) => SaveShellConfig(throwOnError);
    bool IOnlineHost.SaveConfig() => SaveConfig();

    Task IOnlineHost.RefreshListsAsync() => RefreshListsAsync();
    void IOnlineHost.SelectSecondLevelByPath(string? path) => _workspaceView?.SelectSecondLevelByPath(path);
    void IOnlineHost.SetBusyState(bool busy) => _workspaceView?.SetBusyState(busy);
    bool IOnlineHost.IsFavoriteCharacter(string characterKey) => IsFavoriteCharacter(characterKey);
    void IOnlineHost.ToggleFavoriteCharacter(string characterKey) => ToggleFavoriteCharacter(characterKey);

    Dictionary<string, List<ShortcutBinding>> IOnlineHost.ModBindings => _modBindings;
    Dictionary<string, string> IOnlineHost.ModLinks => _modLinks;
    ShortcutBinding IOnlineHost.CreateAutomaticShortcutBinding(DetectedModShortcut shortcut) => CreateAutomaticShortcutBinding(shortcut);

    void IOnlineHost.RefreshDownloadTaskCard(DownloadTaskItem task) => RefreshDownloadTaskCard(task);
    void IOnlineHost.RefreshUpdatesPane() => RefreshUpdatesPane();
    void IOnlineHost.SetModUpdateStatus(UiMessage status) => SetModUpdateStatus(status);

    HttpClient IOnlineHost.Http => _httpClient;
    AppDataStore IOnlineHost.DataStore => _appDataStore;
    GameBananaMetadataService IOnlineHost.Metadata => _gameBananaMetadataService;

    int IOnlineHost.ShortcutScanSafetyLimit => ShortcutScanSafetyLimit;
    IReadOnlyList<string> IOnlineHost.ImageExtensions => ImageExtensions;
    string? IOnlineHost.FindPreviewImage(List<string> files) => WorkspacePage.FindPreviewImage(files, ImageExtensions);
}
