using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;
using IntegratedModManager.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow : Window
{
    private const string AppVersion = ApplicationReleasePolicy.VersionLabel;

    // Shared state and file access; MainWindow forwards to these until each page owns its part.
    private readonly AppServices _services = new();

    /// <summary>The dashboard's repository cards and current-repository status.</summary>
    private DashboardViewModel Dashboard => _services.Dashboard;

    private RepositoryPathsViewModel Paths => _services.Paths;
    private static bool AppSelfUpdateEnabled => ApplicationReleasePolicy.ApplicationSelfUpdateEnabled;
    private const string DefaultOnlineSourceSite = OnlineSourceRules.DefaultSourceSite;
    private const string DefaultOnlineCategoryId = OnlineSourceRules.DefaultCategoryId;
    private const string DefaultOnlineGameName = ConfigurationService.DefaultGameName;
    private const string EndfieldOfficialWikiUrl = OnlineSourceRules.EndfieldOfficialWikiUrl;

    private enum ShellLayoutMode
    {
        Expanded,
        Compact,
        Minimal
    }

    internal static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly GameBananaMetadataService _gameBananaMetadataService;
    private readonly Dictionary<string, DownloadTaskCardView> _navTaskCards = new(StringComparer.Ordinal);

    // Kept alive for its ColorValuesChanged event, which follows the Windows app theme.
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private bool _isApplyingUpdateCheckIntervalSelection;
    private bool _isApplyingModUpdateIntervalSelection;
    private bool _isApplyingRepositoryDeploymentMode;
    private UiMessage? _modUpdateStatus;
    private ShellLayoutMode _shellLayoutMode = (ShellLayoutMode)(-1);
    private bool _primaryNavExpanded;
    private TextBox? _lastFocusedTextBox;
    private int _notificationVersion;
    private Microsoft.UI.Windowing.OverlappedPresenterState? _lastPresenterState;
    // Made the first time the Online page is shown (stage J finish, step 5b); null until then.
    private OnlinePage? _onlineView;
    // Created the first time the Mod workspace is shown (stage J finish, step 7b); null until then.
    private WorkspacePage? _workspaceView;

    public MainWindow()
    {
        TraceStartupStage("MainWindow constructor entered");
        _appDataStore = new AppDataStore(
            Path.Combine(AppContext.BaseDirectory, "cache", "app-index.db"),
            LogPersistentDataStoreIssue);
        _gameBananaMetadataService = new GameBananaMetadataService(_httpClient,
            new OnlineMetadataCacheAdapter(_appDataStore, OnlinePage._onlineDetailsCachePath, LogApplicationIssue),
            diagnostics: LogApplicationIssue);
        InitializeComponent();
        // Stage J7: the Mod workspace page; it reaches the window only through IWorkspaceHost.
        _services.Workspace.Attach(LogApplicationIssue, SaveConfig, _appDataStore, ApplyDashboardCharacterAvatar);
        _services.Diagnostics.Attach(ReportUiActionFailure, () => _appDataStore.GetHealthSummary(), AppUpdateService.GetInstallRootPath);
        _services.Install.AttachBackups(busy => _workspaceView?.SetBusyState(busy), ReportUiActionFailure);
        _services.Workspace.AttachRefresh(RefreshListsAsync);
        _services.AppUpdates.Attach(_httpClient, _appDataStore, RefreshSettingsPane,
            () => { _shell.CurrentSection = PrimarySection.Settings; ApplyShellState(refreshRepository: false); }, Close, ReportUiActionFailure);
        _services.Launcher.Attach(LogApplicationIssue, SaveSelectedRepositoryFromInputs, RunUiActionAsync, ReportUiActionFailure, RefreshDashboard);
        _services.Configuration.Attach(LogApplicationIssue, () => _onlineView?.CaptureOnlineBrowseSession(), ApplySelectedRepositoryToInputs,
            SaveSelectedRepositoryFromInputs, _services.ShortcutBindings.Bindings, _services.ShortcutBindings.Links, WhatsNewFeatureSet);
        _services.Images.Attach(_httpClient, LogApplicationIssue);
        _services.OnlineCategories.Attach(_httpClient, LogApplicationIssue);
        _services.CharacterCatalog.Attach(_httpClient, _appDataStore, LogApplicationIssue,
            repository => BuiltInRepositories.Presets.FirstOrDefault(preset => RepositoryPresetMatching.IsForPreset(repository, preset))?.WikiUrl);
        _services.Dialogs.Attach(() => RootGrid.XamlRoot, () => RootGrid.ActualTheme);
        _services.Downloads.Attach(_services, DispatcherQueue, LogApplicationIssue);
        _services.Downloads.TaskRefreshRequested += OnDownloadTaskRefreshRequested;
        _services.OnlineDownloads.Attach(this, DispatcherQueue);
        _services.Tracking.Attach(_services, LogApplicationIssue, LogApplicationIssueWithStack, SaveConfig,
            throwOnError => SaveShellConfig(throwOnError), SetModUpdateStatus, RefreshUpdatesPane,
            () => { _shell.CurrentSection = PrimarySection.Updates; ApplyShellState(refreshRepository: false); },
            RefreshListsAsync, _services.ShortcutBindings.Links);
        _services.Install.Attach(LogApplicationIssue, () => SaveShellConfig(), () => _shell.InstallBackupLimitGb,
            (percent, message) => DispatcherQueue.TryEnqueue(() => _workspaceView?.UpdateProgress(percent, message)),
            text => _services.Workspace.Status = text);
        _services.Appearance.ReduceAnimations = () => ShouldReduceAnimations;
        _services.Dialogs.AttachWindow(() => WinRT.Interop.WindowNative.GetWindowHandle(this));
        _services.Status.NotificationRequested += OnNotificationRequested;
        InitializeSettingsHost();
        InitializeUpdatesHost();
        InitializeDashboardHost();
        AttachShortcutServices();
        AttachCombinationService();
        InitializePageHost();
        SubscribeToAppearanceEvents();
        ApplyWindowIcon();
        TraceStartupStage("InitializeComponent completed");
        InitializePersistentDataStore();
        TraceStartupStage("Persistent data store initialized");
        InitializeAppTitleBar();
        InitializeCommandPaletteShortcut();
        TraceStartupStage("Input handlers initialized");
        // Neither file exists only on a first install; upgrades keep them, and with them the user's language.
        _firstInstall = _services.ShellConfig.IsFirstInstall;
        _services.Configuration.FirstInstall = _firstInstall;
        if (_firstInstall) _services.Language.Load(UiLanguagePolicy.ForSystem(System.Globalization.CultureInfo.CurrentUICulture.Name, UiText.Languages));
        LoadConfig();
        LoadShellConfig();
        _services.ShortcutPanel.ListenToState();
        if (_firstInstall) SaveConfig();
        InitializeTraySupport();
        _services.ShortcutPanel.InitializeHotkey();
        TraceStartupStage("Configuration loaded");
        _services.Downloads.Load();
        InitializeAppearanceSettings();
        ApplyLanguage();
        TraceStartupStage("Theme and language applied");
        // The Online page set up its controls and character list here; without the page the list is still kept.
        EnsureOnlineCharacterOptions(_services.Repositories.Selected);
        _services.AppUpdates.DetectLocalUpdatePackage();
        TraceStartupStage("Online controls and local update detection initialized");
        RefreshSettingsPane();
        TraceStartupStage("Settings and navigation refreshed");
        ApplyShellState(refreshRepository: false);
        UpdateShellLayout();
        RefreshDashboard();
        TraceStartupStage("Initial shell rendered");
        RootGrid.Loaded += OnRootGridLoaded;
        Closed += OnWindowClosed;

        if (Directory.Exists(Paths.Source))
        {
            _ = RefreshListsAsync();
        }
        else
        {
            _services.Workspace.SetDefaultStatus();
        }

        TraceStartupStage("MainWindow constructor completed");
    }

}
