using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
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
    private const string GitHubRepositoryUrl = "https://github.com/uyujkk/Integrated_Mod_Manager";
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/uyujkk/Integrated_Mod_Manager/releases/latest";
    private const string DefaultOnlineSourceSite = OnlineSourceRules.DefaultSourceSite;
    private const string DefaultOnlineCategoryId = OnlineSourceRules.DefaultCategoryId;
    private const string DefaultOnlineGameName = ConfigurationService.DefaultGameName;
    private const string EndfieldOfficialWikiUrl = OnlineSourceRules.EndfieldOfficialWikiUrl;
    private const int ShortcutScanSafetyLimit = 256;

    private enum ShellLayoutMode
    {
        Expanded,
        Compact,
        Minimal
    }

    internal static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly BatchObservableCollection<SecondLevelFolderItem> _secondLevelItems = [];
    private readonly List<TextBox> _shortcutKeyBoxes = [];
    private readonly List<TextBox> _shortcutActionBoxes = [];
    private readonly Dictionary<string, List<ShortcutBinding>> _modBindings = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _modLinks = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly GameBananaMetadataService _gameBananaMetadataService;
    private readonly Dictionary<string, DownloadTaskCardView> _navTaskCards = new(StringComparer.Ordinal);

    // Kept alive for its ColorValuesChanged event, which follows the Windows app theme.
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private bool _isCheckingUpdates;
    private bool _hasPromptedForLocalUpdate;
    private bool _isApplyingUpdateCheckIntervalSelection;
    private bool _isApplyingModUpdateIntervalSelection;
    private bool _isApplyingRepositoryDeploymentMode;
    private string? _currentSecondLevelPath;
    private string? _localUpdatePackagePath;
    private Version? _localUpdatePackageVersion;
    private UiMessage? _modUpdateStatus;
    private UiMessage? _updateStatus;
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
        _services.Diagnostics.Attach(ReportUiActionFailure, () => _appDataStore.GetHealthSummary(), GetInstallRootPath);
        _services.Install.AttachBackups(busy => _workspaceView?.SetBusyState(busy), ReportUiActionFailure);
        _services.Workspace.AttachRefresh(RefreshListsAsync);
        _services.Launcher.Attach(LogApplicationIssue, SaveSelectedRepositoryFromInputs, RunUiActionAsync, ReportUiActionFailure, RefreshDashboard);
        _services.Configuration.Attach(LogApplicationIssue, () => _onlineView?.CaptureOnlineBrowseSession(), ApplySelectedRepositoryToInputs,
            SaveSelectedRepositoryFromInputs, _modBindings, _modLinks, WhatsNewFeatureSet);
        _services.Images.Attach(_httpClient, LogApplicationIssue);
        _services.OnlineCategories.Attach(_httpClient, LogApplicationIssue);
        _services.CharacterCatalog.Attach(_httpClient, _appDataStore, LogApplicationIssue,
            repository => BuiltInRepositories.Presets.FirstOrDefault(preset => IsRepositoryForPreset(repository, preset))?.WikiUrl);
        _services.Dialogs.Attach(() => RootGrid.XamlRoot, () => RootGrid.ActualTheme);
        _services.Downloads.Attach(_services, DispatcherQueue, LogApplicationIssue);
        _services.Downloads.TaskRefreshRequested += OnDownloadTaskRefreshRequested;
        _services.OnlineDownloads.Attach(this, DispatcherQueue);
        _services.Tracking.Attach(_services, LogApplicationIssue, LogApplicationIssueWithStack, SaveConfig,
            throwOnError => SaveShellConfig(throwOnError), SetModUpdateStatus, RefreshUpdatesPane,
            () => { _shell.CurrentSection = PrimarySection.Updates; ApplyShellState(refreshRepository: false); },
            RefreshListsAsync, _modLinks);
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
        ListenToShortcutState();
        if (_firstInstall) SaveConfig();
        InitializeTraySupport();
        InitializeShortcutPanelHotkey();
        TraceStartupStage("Configuration loaded");
        _services.Downloads.Load();
        InitializeAppearanceSettings();
        ApplyLanguage();
        TraceStartupStage("Theme and language applied");
        // The Online page set up its controls and character list here; without the page the list is still kept.
        EnsureOnlineCharacterOptions(_services.Repositories.Selected);
        DetectLocalUpdatePackage();
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
            SetDefaultStatus();
        }

        TraceStartupStage("MainWindow constructor completed");
    }

}
