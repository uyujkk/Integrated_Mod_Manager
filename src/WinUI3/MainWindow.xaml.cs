using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using IntegratedModManager.Core.Archives;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.Core.Updates;
using IntegratedModManager.Data;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow : Window
{
    private const string AppVersion = ApplicationReleasePolicy.VersionLabel;

    /// <summary>The Settings page binds its appearance choices to this view model.</summary>
    public AppearanceSettingsViewModel AppearanceSettings { get; } = new();

    /// <summary>The dashboard's repository cards and current-repository status.</summary>
    public DashboardViewModel Dashboard { get; } = new();
    private static bool AppSelfUpdateEnabled => ApplicationReleasePolicy.ApplicationSelfUpdateEnabled;
    private const string GitHubRepositoryUrl = "https://github.com/uyujkk/Integrated_Mod_Manager";
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/uyujkk/Integrated_Mod_Manager/releases/latest";
    private const string DefaultOnlineSourceSite = "GameBanana";
    private const string DefaultOnlineCategoryId = "42770";
    private const string DefaultOnlineGameName = "Arknights: Endfield";
    private const string EndfieldOfficialWikiUrl = "https://wiki.skland.com/endfield";
    private const string EndfieldOfficialOperatorCatalogUrl = "https://endfield.hypergryph.com/operator";
    private const string HoYoWikiApiBaseUrl = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/wapi";
    private const int OnlineDisplayPageSize = 40;
    private const int OnlineRawPageLimit = 80;
    private static readonly TimeSpan OnlineModListRequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OnlinePreviewImageRequestTimeout = TimeSpan.FromSeconds(15);
    // How long a page may wait for its download counts before it is shown without them.
    private static readonly TimeSpan OnlineDownloadCountInlineBudget = TimeSpan.FromSeconds(2);
    private const long MaxOnlinePreviewImageBytes = 20L * 1024 * 1024;
    private const int ArchiveToolTimeoutMinutes = 10;
    private const int InitialShortcutRows = 1;
    private const int ShortcutScanSafetyLimit = 256;
    private static readonly IReadOnlyList<string> SupportedArchiveExtensions = ArchiveExtractor.SupportedExtensions;
    private static readonly ArchiveExtractor ArchiveExtraction = new(
        FindSevenZipExecutable, FindBandizipExecutable, TimeSpan.FromMinutes(ArchiveToolTimeoutMinutes));
    private static readonly Regex LocalUpdatePackageRegex = new(
        @"^Integrated_Mod_Manager-v(?<version>\d+\.\d+\.\d+)\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> GenericOnlineCharacterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "operator", "operators", "character", "characters", "skin", "skins",
        "other", "others", "misc", "miscellaneous", "uncategorized", "unclassified"
    };

    private enum PrimarySection
    {
        Dashboard,
        Repository,
        Online,
        Updates,
        Settings,
        Shortcuts
    }

    private enum AppLanguage
    {
        ZhCn,
        EnUs
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

    private enum OnlineSortMode
    {
        Hotness,
        Downloads,
        Likes,
        Views,
        Updated
    }

    private enum OnlineCardLayoutMode
    {
        List,
        Grid
    }

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
    private static readonly SolidColorBrush CopiedBrush = new(ColorHelper.FromArgb(255, 36, 124, 76));
    private static readonly SolidColorBrush MissingBrush = new(ColorHelper.FromArgb(255, 177, 76, 55));
    private static readonly SolidColorBrush NeutralBrush = new(ColorHelper.FromArgb(255, 60, 60, 60));

    private readonly string _configPath = Path.Combine(AppContext.BaseDirectory, "config.ini");
    private readonly string _shellConfigPath = Path.Combine(AppContext.BaseDirectory, "beta-shell.json");
    private readonly string _onlineImageCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-images");
    private readonly string _onlinePageCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-pages");
    private readonly string _onlineDetailsCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-details");
    private readonly string _onlineCharacterCachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-characters");
    private readonly ConfigurationFileStore _configurationFiles = new(Path.Combine(AppContext.BaseDirectory, "backups", "config"));
    private readonly string _modInstallBackupPath = Path.Combine(AppContext.BaseDirectory, "backups", "mod-installs");
    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly RequestCooldown _onlineRateLimit = new();
    private readonly SemaphoreSlim _onlineCharacterAvatarDownloadGate = new(3, 3);
    private readonly BatchObservableCollection<FirstLevelFolderItem> _firstLevelItems = [];
    private readonly BatchObservableCollection<SecondLevelFolderItem> _secondLevelItems = [];
    private readonly ObservableCollection<OnlineModCard> _onlineVisibleMods = [];
    private readonly List<FirstLevelFolderItem> _allFirstLevelItems = [];
    private readonly List<Border> _shortcutKeyBorders = [];
    private readonly List<TextBox> _shortcutKeyBoxes = [];
    private readonly List<TextBox> _shortcutActionBoxes = [];
    private readonly Dictionary<string, List<ShortcutBinding>> _modBindings = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _modLinks = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, TrackedModOrigin> _trackedModOrigins = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly GameBananaMetadataService _gameBananaMetadataService;
    private readonly List<WorkspaceRepository> _repositories = [];
    private readonly List<OnlineModCard> _onlineMods = [];
    private readonly List<OnlineCharacterOption> _onlineCharacterOptions = [];
    private readonly List<string> _onlineDetailImageUrls = [];
    private readonly List<TrackedModUpdateResult> _trackedModUpdateResults = [];
    private readonly List<ModConfigurationProfile> _configurationProfiles = [];
    private readonly List<ModPersistentSlot> _modPersistentSlots = [];
    private readonly ObservableCollection<DownloadTaskItem> _downloadTasks = [];
    private readonly Dictionary<string, DownloadTaskCardView> _downloadTaskCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DownloadTaskCardView> _titleBarTaskCards = new(StringComparer.Ordinal);

    private bool _isDarkTheme;
    private ThemePreference _themePreference = ThemePreference.System;
    // Kept alive for its ColorValuesChanged event, which follows the Windows app theme.
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private string? _storedThemePreference;
    private bool? _legacyDarkTheme;
    private bool _isCheckingUpdates;
    private bool _isCheckingModUpdates;
    private bool _isLoadingOnlineMods;
    private readonly HashSet<string> _loadingOnlineCharacterCatalogKeys = new(StringComparer.Ordinal);
    private bool _isLoadingBindings;
    private bool _isLoadingModLink;
    private bool _hasPromptedForLocalUpdate;
    private bool _isApplyingOnlineCharacterSelection;
    private bool _isApplyingDashboardRepositorySelection;
    private bool _isApplyingUpdateCheckIntervalSelection;
    private bool _isApplyingModUpdateIntervalSelection;
    private bool _isApplyingConfigurationProfileSelection;
    private bool _isApplyingRepositoryDeploymentMode;
    private bool _enableConflictDetection = true;
    private bool _reduceMotion;
    private int _visibleShortcutRows = 1;
    private TextBox? _activeShortcutKeyBox;
    private string? _currentSecondLevelPath;
    private string? _latestReleaseBody;
    private string? _latestReleasePublishedAt;
    private string? _latestReleaseTag;
    private string? _latestReleaseTitle;
    private string? _latestReleaseUrl;
    private string? _localUpdatePackagePath;
    private Version? _localUpdatePackageVersion;
    private string? _onlineStatusZh;
    private string? _onlineStatusEn;
    private string? _onlineCacheStatusZh;
    private string? _onlineCacheStatusEn;
    private string? _lastLoadedOnlineConfigKey;
    private string? _blockedOnlineAutoLoadConfigKey;
    private string? _onlineCharacterStripSignature;
    private string? _onlineCharacterCatalogKey;
    private bool _useHorizontalOnlineCharacterRail;
    private int _onlineCurrentPage = 1;
    private int _onlineTotalCount;
    private int _onlineTotalPages = 1;
    private string _onlineCharacterFilter = string.Empty;
    private string _onlineSearchText = string.Empty;
    private string? _selectedRepositoryId;
    private string? _selectedConfigurationProfileId;
    private string? _lastInstallTransactionPath;
    private string _modUpdateStatusEn = string.Empty;
    private string _modUpdateStatusZh = string.Empty;
    private string _updateStatusEn = string.Empty;
    private string _updateStatusZh = string.Empty;
    private OnlineModCard? _activeOnlineDetailMod;
    private AppLanguage _currentLanguage = AppLanguage.ZhCn;
    private PrimarySection _currentPrimarySection = PrimarySection.Dashboard;
    private ShellLayoutMode _shellLayoutMode = (ShellLayoutMode)(-1);
    private bool _primaryNavExpanded;
    private bool _navigationCollapsed;
    private OnlineSortMode _onlineSortMode = OnlineSortMode.Hotness;
    private OnlineCardLayoutMode _onlineCardLayoutMode = OnlineCardLayoutMode.List;
    private UpdateCheckInterval _updateCheckInterval = UpdateCheckInterval.Manual;
    private DateTimeOffset? _lastUpdateCheckUtc;
    private UpdateCheckInterval _modUpdateCheckInterval = UpdateCheckInterval.Manual;
    private DateTimeOffset? _lastModUpdateCheckUtc;
    private TextBox? _lastFocusedTextBox;
    private int _notificationVersion;
    private int _onlineHeroImageRequestVersion;
    private int _onlineDetailImageIndex;
    private int _onlineCharacterAnimationVersion;
    private int _onlineCharacterAvatarRequestVersion;
    private CancellationTokenSource _onlineCharacterAvatarCancellation = new();
    private int _onlineDetailAnimationVersion;
    private int _onlineDownloadEnrichmentVersion;
    private int _onlineModRequestVersion;
    private int _previewImageRequestVersion;
    private string? _foregroundDownloadTaskId;
    private CancellationTokenSource? _onlineModListCancellation;
    private bool _animateOnlineCardsOnNextRefresh;
    private int? _savedWindowX;
    private int? _savedWindowY;
    private int? _savedWindowWidth;
    private bool _savedWindowMaximized;
    private Microsoft.UI.Windowing.OverlappedPresenterState? _lastPresenterState;
    private int? _savedWindowHeight;
    private readonly HashSet<string> _onlineKnownCharacters = new(StringComparer.OrdinalIgnoreCase);
    private InterfaceDensity _interfaceDensity = InterfaceDensity.Comfortable;

    public MainWindow()
    {
        TraceStartupStage("MainWindow constructor entered");
        _appDataStore = new AppDataStore(
            Path.Combine(AppContext.BaseDirectory, "cache", "app-index.db"),
            LogPersistentDataStoreIssue);
        _gameBananaMetadataService = new GameBananaMetadataService(_httpClient,
            new OnlineMetadataCacheAdapter(_appDataStore, _onlineDetailsCachePath, LogApplicationIssue),
            diagnostics: LogApplicationIssue);
        InitializeComponent();
        ApplyWindowIcon();
        TraceStartupStage("InitializeComponent completed");
        InitializePersistentDataStore();
        TraceStartupStage("Persistent data store initialized");
        InitializeAppTitleBar();
        EditOnlineRepositoryButton.Click -= OnEditRepositoryDetailsClicked;
        EditOnlineRepositoryButton.Click += OnEditOnlineRepositoryClicked;
        FirstLevelListView.ItemsSource = _firstLevelItems;
        SecondLevelListView.ItemsSource = _secondLevelItems;
        OnlineVirtualizedListView.ItemsSource = _onlineVisibleMods;
        OnlineVirtualizedGridView.ItemsSource = _onlineVisibleMods;
        TraceStartupStage("List sources attached");
        RegisterTrackedTextInputs();
        BuildShortcutRows();
        TraceStartupStage("Input handlers and shortcuts initialized");
        LoadConfig();
        LoadShellConfig();
        InitializeTraySupport();
        InitializeShortcutPanelHotkey();
        try
        {
            InitializeRefinedWorkspace();
            InitializeRepositoryWorkspaceV4();
        }
        catch (Exception ex)
        {
            TraceStartupStage("Refined workspace initialization failed: " + ex);
            throw;
        }
        TraceStartupStage("Configuration loaded");
        LoadDownloadTasks();
        InitializeAppearanceSettings();
        ApplyLanguage();
        TraceStartupStage("Theme and language applied");
        InitializeOnlineControls();
        InitializeBrowseSessionTracking();
        DetectLocalUpdatePackage();
        TraceStartupStage("Online controls and local update detection initialized");
        RefreshSettingsPane();
        RefreshSecondaryNavigation();
        TraceStartupStage("Settings and navigation refreshed");
        ApplyShellState(refreshRepository: false);
        UpdateShellLayout();
        RefreshDashboard();
        TraceStartupStage("Initial shell rendered");
        RootGrid.Loaded += OnRootGridLoaded;
        Closed += OnWindowClosed;

        if (Directory.Exists(SourceTextBox.Text))
        {
            _ = RefreshListsAsync();
        }
        else
        {
            SetDefaultStatus();
        }

        TraceStartupStage("MainWindow constructor completed");
    }

    /// <summary>A folder the user may choose to have created (issue #6); created only after confirmation.</summary>
    private sealed record NewCharacterFolderChoice(string Path);

    private int _folderRefreshGeneration;
    private CancellationTokenSource? _folderRefreshCancellation;
}
