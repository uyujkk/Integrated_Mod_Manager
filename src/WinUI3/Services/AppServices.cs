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
    }

    public LanguageService Language { get; } = new();
    public AppearanceService Appearance { get; } = new();
    public ShellConfigService ShellConfig { get; } = new(AppContext.BaseDirectory);
    public RepositoryService Repositories { get; } = new();
    public DialogService Dialogs { get; }
    public StatusService Status { get; } = new();
    public DeploymentService Deployment { get; } = new(AppContext.BaseDirectory);
    public DownloadService Downloads { get; } = new(AppContext.BaseDirectory);
    public LaunchService Launch { get; } = new();

    /// <summary>The Settings page's state; the window writes results here and answers its requests.</summary>
    public SettingsViewModel Settings { get; } = new();

    /// <summary>The Updates page's state (stage J3).</summary>
    public UpdatesViewModel Updates { get; } = new();

    public ImageService Images { get; } = new();

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
