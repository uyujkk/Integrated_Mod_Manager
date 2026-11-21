namespace ModFolderCopier.WinUI;

/// <summary>
/// The composition root: the services the window and its pages share, created once at startup.
/// Pages get these instead of reaching into MainWindow (docs/development/UI-Structure-Plan.md, stage J1).
/// </summary>
internal sealed class AppServices
{
    public AppServices()
    {
        Dialogs = new(Language);
        OnlineSource = new(Language);
        CharacterCatalog = new(OnlineSource);
        Install = new(this);
        Tracking = new(Repositories);
        OnlineDownloads = new(this);
        OnlineCharacters = new(this);
        Slots = new(this);
        PresetPreviews = new(this);
    }

    public LanguageService Language { get; } = new();
    public AppearanceService Appearance { get; } = new();
    public ShellConfigService ShellConfig { get; } = new(AppContext.BaseDirectory);
    public RepositoryService Repositories { get; } = new();
    public DialogService Dialogs { get; }
    public StatusService Status { get; } = new();
    public DeploymentService Deployment { get; } = new(AppContext.BaseDirectory);
    public DownloadService Downloads { get; } = new(AppContext.BaseDirectory);

    /// <summary>Mods installed from the Online page and tracked for updates (stage J finish, step 5).</summary>
    public ModTrackingService Tracking { get; }

    /// <summary>Downloading and installing Mods from the online source, with or without the Online page (stage J finish, step 5a).</summary>
    public OnlineDownloadService OnlineDownloads { get; }

    /// <summary>The current repository's character list, kept whether or not the Online page exists (stage J finish, step 5b).</summary>
    public OnlineCharacterOptionsService OnlineCharacters { get; }

    /// <summary>State slots, Default slots and switching them from the overlay (stage J finish, step 6).</summary>
    public SlotService Slots { get; }

    /// <summary>Pictures of presets and state slots (stage J finish, step 6).</summary>
    public PresetPreviewService PresetPreviews { get; }
    public LaunchService Launch { get; } = new();

    /// <summary>Install transactions, conflict checks, archives and copy ownership records (stage J finish, step 4).</summary>
    public InstallService Install { get; }

    /// <summary>The Settings page's state; the window writes results here and answers its requests.</summary>
    public SettingsViewModel Settings { get; } = new();

    /// <summary>The Updates page's state (stage J3).</summary>
    public UpdatesViewModel Updates { get; } = new();

    public ImageService Images { get; } = new();

    /// <summary>Which online source each repository uses (stage J finish, step 3).</summary>
    public OnlineSourceService OnlineSource { get; }

    /// <summary>The character catalog of each repository's game (stage J finish, step 3).</summary>
    public CharacterCatalogService CharacterCatalog { get; }

    /// <summary>The Mod categories of each repository's game (issue #129).</summary>
    public OnlineCategoryService OnlineCategories { get; } = new();

    /// <summary>The sign-in of the GameBanana page opened for likes (issue #129), which Settings can clear.</summary>
    public GameBananaWebSession GameBananaWeb { get; } = new();

    /// <summary>The Online page's view model and preferences (stage J finish, step 3).</summary>
    public OnlineState Online { get; } = new();

    /// <summary>The window is closing: background work stops updating the pages (set once, by the window).</summary>
    public bool IsClosing { get; set; }

    /// <summary>The dashboard's state (stage J4).</summary>
    public DashboardViewModel Dashboard { get; } = new();

    /// <summary>The current repository's paths as edited on the dashboard; the window reads them here.</summary>
    public RepositoryPathsViewModel Paths { get; } = new();

    /// <summary>The integrated Endfield launch, used by the dashboard and the Mod workspace.</summary>
    public EndfieldBridgeService EndfieldBridge { get; } = new();

    /// <summary>The Shortcuts page and overlay state (stage J5).</summary>
    public ShortcutPageViewModel Shortcuts { get; } = new();

    /// <summary>The Shortcuts overlay; the window attaches it at startup.</summary>
    public IShortcutOverlay Overlay { get; set; } = null!;
}
