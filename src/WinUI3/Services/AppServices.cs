namespace ModFolderCopier.WinUI;

/// <summary>
/// The composition root: the services the window and its pages share, created once at startup.
/// Pages get these instead of reaching into MainWindow (docs/development/UI-Structure-Plan.md, stage J1).
/// </summary>
internal sealed class AppServices
{
    public LanguageService Language { get; } = new();
    public AppearanceService Appearance { get; } = new();
    public ShellConfigService ShellConfig { get; } = new(AppContext.BaseDirectory);
    public RepositoryService Repositories { get; } = new();
    public DialogService Dialogs { get; } = new();
    public StatusService Status { get; } = new();
    public DeploymentService Deployment { get; } = new(AppContext.BaseDirectory);
    public DownloadService Downloads { get; } = new(AppContext.BaseDirectory);
    public LaunchService Launch { get; } = new();

    /// <summary>The Settings page's state; the window writes results here and answers its requests.</summary>
    public SettingsViewModel Settings { get; } = new();

    /// <summary>The Updates page's state (stage J3).</summary>
    public UpdatesViewModel Updates { get; } = new();

    public ImageService Images { get; } = new();
}
