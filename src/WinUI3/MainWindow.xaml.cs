using IntegratedModManager.Core.Archives;
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
    private const string DefaultOnlineGameName = "Arknights: Endfield";
    private const string EndfieldOfficialWikiUrl = OnlineSourceRules.EndfieldOfficialWikiUrl;
    private const int ArchiveToolTimeoutMinutes = 10;
    private const int ShortcutScanSafetyLimit = 256;
    private static readonly IReadOnlyList<string> SupportedArchiveExtensions = ArchiveExtractor.SupportedExtensions;
    private static readonly ArchiveExtractor ArchiveExtraction = new(
        FindSevenZipExecutable, FindBandizipExecutable, TimeSpan.FromMinutes(ArchiveToolTimeoutMinutes));

    private enum PrimarySection
    {
        Dashboard,
        Repository,
        Online,
        Updates,
        Settings,
        Shortcuts
    }

    private enum ShellLayoutMode
    {
        Expanded,
        Compact,
        Minimal
    }

    private enum InterfaceDensity
    {
        Comfortable,
        Compact
    }

    private enum UpdateCheckInterval
    {
        Manual,
        Daily,
        EveryThreeDays,
        Weekly
    }

    internal static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly CharacterListViewModel _characters = new();
    private readonly BatchObservableCollection<SecondLevelFolderItem> _secondLevelItems = [];
    private readonly List<TextBox> _shortcutKeyBoxes = [];
    private readonly List<TextBox> _shortcutActionBoxes = [];
    private readonly Dictionary<string, List<ShortcutBinding>> _modBindings = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _modLinks = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, TrackedModOrigin> _trackedModOrigins = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly GameBananaMetadataService _gameBananaMetadataService;
    private readonly List<TrackedModUpdateResult> _trackedModUpdateResults = [];
    private readonly List<ModConfigurationProfile> _configurationProfiles = [];
    private readonly List<ModPersistentSlot> _modPersistentSlots = [];
    private readonly Dictionary<string, DownloadTaskCardView> _navTaskCards = new(StringComparer.Ordinal);

    private ThemePreference _themePreference = ThemePreference.System;
    // Kept alive for its ColorValuesChanged event, which follows the Windows app theme.
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private string? _storedThemePreference;
    private bool? _legacyDarkTheme;
    private bool _isCheckingUpdates;
    private bool _isCheckingModUpdates;
    private bool _hasPromptedForLocalUpdate;
    private bool _isApplyingUpdateCheckIntervalSelection;
    private bool _isApplyingModUpdateIntervalSelection;
    private bool _isApplyingConfigurationProfileSelection;
    private bool _isApplyingRepositoryDeploymentMode;
    private bool _enableConflictDetection = true;
    private bool _reduceMotion;
    private string? _currentSecondLevelPath;
    private string? _latestReleaseBody;
    private string? _latestReleasePublishedAt;
    private string? _latestReleaseTag;
    private string? _latestReleaseTitle;
    private string? _latestReleaseUrl;
    private string? _localUpdatePackagePath;
    private Version? _localUpdatePackageVersion;
    private string? _selectedConfigurationProfileId;
    private string? _lastInstallTransactionPath;
    private UiMessage? _modUpdateStatus;
    private UiMessage? _updateStatus;
    private PrimarySection _currentPrimarySection = PrimarySection.Dashboard;
    private ShellLayoutMode _shellLayoutMode = (ShellLayoutMode)(-1);
    private bool _primaryNavExpanded;
    private bool _navigationCollapsed;
    private UpdateCheckInterval _updateCheckInterval = UpdateCheckInterval.Manual;
    private DateTimeOffset? _lastUpdateCheckUtc;
    private UpdateCheckInterval _modUpdateCheckInterval = UpdateCheckInterval.Manual;
    private DateTimeOffset? _lastModUpdateCheckUtc;
    private TextBox? _lastFocusedTextBox;
    private int _notificationVersion;
    private int? _savedWindowX;
    private int? _savedWindowY;
    private int? _savedWindowWidth;
    private bool _savedWindowMaximized;
    private Microsoft.UI.Windowing.OverlappedPresenterState? _lastPresenterState;
    private int? _savedWindowHeight;
    private InterfaceDensity _interfaceDensity = InterfaceDensity.Comfortable;
    private readonly OnlinePage _onlineView;
    private readonly WorkspacePage _workspaceView;

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
        // Stage J6: the Online page; it reaches the window only through IOnlineHost.
        _onlineView = new OnlinePage(_services, this);
        // Stage J7: the Mod workspace page; it reaches the window only through IWorkspaceHost.
        _workspaceView = new WorkspacePage(_services, this);
        AttachWorkspacePageEvents();
        _services.Images.Attach(_httpClient, LogApplicationIssue);
        _services.CharacterCatalog.Attach(_httpClient, _appDataStore, LogApplicationIssue,
            repository => BuiltInRepositoryPresets.FirstOrDefault(preset => IsRepositoryForPreset(repository, preset))?.WikiUrl);
        _services.Dialogs.Attach(() => RootGrid.XamlRoot, () => RootGrid.ActualTheme);
        _services.Appearance.ReduceAnimations = () => ShouldReduceAnimations;
        _services.Dialogs.AttachWindow(() => WinRT.Interop.WindowNative.GetWindowHandle(this));
        _services.Status.NotificationRequested += OnNotificationRequested;
        InitializeSettingsHost();
        InitializeUpdatesHost();
        InitializeDashboardHost();
        AttachShortcutServices();
        InitializePageHost();
        SubscribeToAppearanceEvents();
        ApplyWindowIcon();
        TraceStartupStage("InitializeComponent completed");
        InitializePersistentDataStore();
        TraceStartupStage("Persistent data store initialized");
        InitializeAppTitleBar();
        InitializeCommandPaletteShortcut();
        _workspaceView.FirstLevelListView.ItemsSource = _characters.Visible;
        _workspaceView.SecondLevelListView.ItemsSource = _secondLevelItems;
        _onlineView.OnlineVirtualizedListView.ItemsSource = _services.Online.Page.VisibleMods;
        _onlineView.OnlineVirtualizedGridView.ItemsSource = _services.Online.Page.VisibleMods;
        TraceStartupStage("List sources attached");
        RegisterTrackedTextInputs();
        _workspaceView.BuildShortcutRows();
        TraceStartupStage("Input handlers and shortcuts initialized");
        // Neither file exists only on a first install; upgrades keep them, and with them the user's language.
        _firstInstall = _services.ShellConfig.IsFirstInstall;
        if (_firstInstall) _services.Language.Load(UiLanguagePolicy.ForSystem(System.Globalization.CultureInfo.CurrentUICulture.Name, UiText.Languages));
        LoadConfig();
        LoadShellConfig();
        ListenToShortcutState();
        if (_firstInstall) SaveConfig();
        InitializeTraySupport();
        InitializeShortcutPanelHotkey();
        try
        {
            _workspaceView.InitializeRefinedWorkspace();
            _workspaceView.InitializeRepositoryWorkspaceV4();
        }
        catch (Exception ex)
        {
            TraceStartupStage("Refined workspace initialization failed: " + ex);
            throw;
        }
        TraceStartupStage("Configuration loaded");
        _onlineView.LoadDownloadTasks();
        InitializeAppearanceSettings();
        ApplyLanguage();
        TraceStartupStage("Theme and language applied");
        _onlineView.InitializeOnlineControls();
        _onlineView.InitializeBrowseSessionTracking();
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
            _ = _workspaceView.RefreshListsAsync();
        }
        else
        {
            SetDefaultStatus();
        }

        TraceStartupStage("MainWindow constructor completed");
    }

    private int _folderRefreshGeneration;
    private CancellationTokenSource? _folderRefreshCancellation;
}
