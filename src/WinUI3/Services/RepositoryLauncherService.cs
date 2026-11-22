using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.EndfieldLaunch;


namespace ModFolderCopier.WinUI;

/// <summary>
/// Running the selected repository's launch (stage J finish, step 8a, moved from the window unchanged): the integrated
/// Endfield launch and its settings dialog, XXMI with the game's importer, or the repository's own program; and the
/// dashboard's launch card. The launch itself is the AGPL-3.0-only IntegratedModManager.EndfieldLaunch component,
/// started through EndfieldBridgeService; this service only edits its settings and starts it.
/// </summary>
internal sealed class RepositoryLauncherService(AppServices services)
{
    private readonly AppServices _services = services;
    private Action<string, Exception> _log = (_, _) => { };
    private Action _saveSelectedRepositoryFromInputs = () => { };
    private Func<string, Func<Task>, Task> _runUiAction = (_, action) => action();
    private Action<string, Exception> _reportFailure = (_, _) => { };
    private Action _refreshDashboard = () => { };

    /// <summary>
    /// Called once by the window: the error log, the path boxes into the selected repository, running a UI action with
    /// its error report, reporting a failed action, and redrawing the dashboard.
    /// </summary>
    public void Attach(Action<string, Exception> log, Action saveSelectedRepositoryFromInputs, Func<string, Func<Task>, Task> runUiAction,
        Action<string, Exception> reportFailure, Action refreshDashboard)
    {
        _log = log;
        _saveSelectedRepositoryFromInputs = saveSelectedRepositoryFromInputs;
        _runUiAction = runUiAction;
        _reportFailure = reportFailure;
        _refreshDashboard = refreshDashboard;
    }

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    /// <summary>The dashboard's launch card for the selected repository.</summary>
    public void RefreshBridgeControls()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        _services.Dashboard.BridgeVisible = EndfieldBridgeService.CanUse(repository);
        _services.Dashboard.BridgeLabel = repository?.UseEndfieldBridge == true
            ? T("EndfieldBridge.LaunchIntegratedEndfieldLaunchSettingsRisks")
            : T("EndfieldBridge.LaunchExistingLoaderIntegratedLaunchAvailable");
        _services.Dashboard.BridgeEnabled = !_services.EndfieldBridge.OperationInProgress && !_services.EndfieldBridge.LaunchInProgress;
    }

    /// <summary>The dashboard's "launch settings" for an Endfield repository.</summary>
    public async void OnBridgeOptionsClicked()
    {
        try
        {
            _saveSelectedRepositoryFromInputs();
            if (_services.Repositories.Selected is { } repository && EndfieldBridgeService.CanUse(repository))
                await ShowEndfieldBridgeOptionsAsync(repository);
        }
        catch (Exception ex)
        {
            _reportFailure("OnBridgeOptionsClicked", ex);
        }
    }

    private async Task ShowEndfieldBridgeOptionsAsync(WorkspaceRepository repository)
    {
        if (_services.EndfieldBridge.OperationInProgress) return;
        _services.EndfieldBridge.OperationInProgress = true;
        RefreshBridgeControls();
        try
        {
            EndfieldLaunchService service = EndfieldBridgeService.CreateLaunchService();
            bool migrated = _services.EndfieldBridge.ImportLegacySettings(repository, service);
            if (migrated) _services.Configuration.SaveShell();
            EndfieldLaunchSettingsDialog.Settings? settings = await EndfieldLaunchSettingsDialog.ShowAsync(_services, new EndfieldLaunchSettingsDialog.Host(
                (operation, action) => _runUiAction(operation, action), extensions => _services.Dialogs.PickFileAsync(extensions), OpenTextDocument,
                url => _services.Dialogs.OpenExternalUrlAsync(url, T("EndfieldBridge.FailedToOpenPage")), _reportFailure), repository, service, migrated);
            if (settings is not null)
            {
                repository.UseEndfieldBridge = settings.UseIntegrated;
                if (repository.UseEndfieldBridge) repository.EndfieldRiskAccepted = true;
                repository.EndfieldGamePath = settings.GamePath;
                repository.EndfieldParentPath = settings.ParentPath;
                repository.EndfieldGameArguments = settings.Arguments;
                repository.EndfieldUseMods = settings.UseMods;
                repository.LauncherPath = settings.LauncherPath;
                if (_services.Repositories.Selected?.Id == repository.Id) _services.Paths.Launcher = repository.LauncherPath;
                _services.Workspace.Status = repository.UseEndfieldBridge
                    ? T("EndfieldBridge.EndfieldLaunchSettingsSavedRunLauncher")
                    : T("EndfieldBridge.SavedRunLauncherStartsTheExisting");
            }

            _services.Configuration.SaveShell();
        }
        catch (Exception ex)
        {
            if (_services.IsClosing) return;
            _log("Endfield launch settings", ex);
            await ShowMessageAsync(T("EndfieldBridge.CouldNotOpenEndfieldLaunchSettings") + ex.Message, T("EndfieldBridge.EndfieldLaunch"));
        }
        finally
        {
            _services.EndfieldBridge.OperationInProgress = false;
            if (!_services.IsClosing)
            {
                RefreshBridgeControls();
                _refreshDashboard();
            }
        }

    }

    private void OpenTextDocument(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("EndfieldBridge.FileNotFound") + path);
        Process.Start(new ProcessStartInfo("notepad.exe", CommandLine.Quote(path)) { UseShellExecute = true });
    }

    /// <summary>Runs the selected repository's launch: the integrated Endfield launch, XXMI with the game's importer, or its own program.</summary>
    public async Task RunAsync()
    {
        if (_services.EndfieldBridge.OperationInProgress || _services.EndfieldBridge.LaunchInProgress) return;
        WorkspaceRepository? repository = _services.Repositories.Selected;
        bool useIntegrated = EndfieldBridgeService.CanUse(repository) && repository!.UseEndfieldBridge;
        try
        {
            if (useIntegrated)
            {
                _saveSelectedRepositoryFromInputs();
                await StartIntegratedEndfieldLaunchAsync(repository!);
                return;
            }

            if (await TryStartThroughXxmiAsync(repository)) return;

            string loader = EndfieldBridgeService.CanUse(repository)
                ? EndfieldBridgeService.ResolveLoader(_services.Paths.Launcher)
                : _services.Paths.Launcher.Trim();
            if (string.IsNullOrWhiteSpace(loader) || !File.Exists(loader))
            {
                await ShowMessageAsync(T("EndfieldBridge.NoValidDefaultXXMIPathWas"), T("EndfieldBridge.LauncherNotSet"));
                return;
            }

            _services.Launch.StartLoader(loader);
            _services.Workspace.Status = T("EndfieldBridge.ExistingLoaderStarted");
            if (string.IsNullOrWhiteSpace(_services.Paths.Launcher)) _services.Paths.Launcher = loader;
            _services.Configuration.Save();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(T("EndfieldBridge.FailedToRunLauncher") + ex.Message, T("EndfieldBridge.LaunchFailed"));
        }
    }

    // Other games need no setup in XXMI's configuration: naming the importer is enough, and XXMI starts the game.
    // Returns false to run the repository's own program: no importer for this game, or a launcher that is not XXMI.
    private async Task<bool> TryStartThroughXxmiAsync(WorkspaceRepository? repository)
    {
        if (repository is null || EndfieldBridgeService.CanUse(repository)) return false;
        RepositoryPresetInfo? preset = BuiltInRepositories.Presets.FirstOrDefault(candidate => RepositoryPresetMatching.IsForPreset(repository, candidate));
        if (XxmiLaunchPolicy.ImporterFor(preset?.Id, repository.XxmiImporter) is not { } importer) return false;
        string loader = EndfieldBridgeService.ResolveLoader(_services.Paths.Launcher);
        if (!XxmiLaunchPolicy.IsXxmiLauncher(loader) || !File.Exists(loader)) return false;

        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(XxmiLaunchPolicy.LauncherFileName)).Length > 0)
        {
            await ShowMessageAsync(T("XxmiLaunch.AlreadyRunning"), T("EndfieldBridge.LaunchFailed"));
            return true;
        }
        string workingDirectory = Path.GetDirectoryName(loader) ?? AppContext.BaseDirectory;
        try
        {
            string configuration = XxmiIntegration.ConfigPath(loader);
            workingDirectory = Path.GetDirectoryName(configuration) ?? workingDirectory;
            string? problem = XxmiLaunchPolicy.CheckImporter(await File.ReadAllTextAsync(configuration), importer) switch
            {
                XxmiImporterState.ImporterMissing => T("XxmiLaunch.ImporterMissing", importer),
                XxmiImporterState.NoGameFolder => T("XxmiLaunch.NoGameFolder", importer),
                _ => null
            };
            if (problem is not null)
            {
                await ShowMessageAsync(problem, T("EndfieldBridge.LaunchFailed"));
                return true;
            }
        }
        catch (Exception ex) when (ex is EndfieldLaunchException or IOException or UnauthorizedAccessException)
        {
            // XXMI may keep its configuration elsewhere. It reports its own problems once started.
            _log("Read XXMI configuration", ex);
        }

        _services.Launch.StartWithArguments(loader, XxmiLaunchPolicy.Arguments(importer), workingDirectory);
        _services.Workspace.Status = T("XxmiLaunch.Started", importer);
        if (string.IsNullOrWhiteSpace(_services.Paths.Launcher)) _services.Paths.Launcher = loader;
        _services.Configuration.Save();
        return true;
    }

    private async Task StartIntegratedEndfieldLaunchAsync(WorkspaceRepository repository)
    {
        EndfieldLaunchService service = EndfieldBridgeService.CreateLaunchService();
        if (_services.EndfieldBridge.ImportLegacySettings(repository, service)) _services.Configuration.SaveShell();
        if (!repository.EndfieldRiskAccepted || !EndfieldGame.IsValidGame(repository.EndfieldGamePath))
        {
            await ShowEndfieldBridgeOptionsAsync(repository);
            return;
        }

        if (LaunchCommand.IsLaunchRunning())
        {
            await ShowMessageAsync(T("EndfieldBridge.AnEndfieldLaunchSessionIsAlready"), T("EndfieldBridge.EndfieldLaunch"));
            return;
        }

        string sessionId = Guid.NewGuid().ToString("N");
        LaunchProfile profile = EndfieldBridgeService.BuildLaunchProfile(repository, !_services.Language.IsChinese, sessionId);
        if (profile.WithMods && !File.Exists(profile.Xxmi))
        {
            await ShowMessageAsync(T("EndfieldBridge.ModLoadingIsOnButXXMI"), T("EndfieldBridge.EndfieldLaunch"));
            return;
        }

        service.Store.SaveProfile(profile);
        try
        {
            // One UAC prompt: the manager's own executable runs the launch without a window.
            Process.Start(new ProcessStartInfo(EndfieldBridgeService.LaunchExecutablePath, LaunchCommand.LaunchVerb)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _services.Workspace.Status = T("EndfieldBridge.AdministratorApprovalWasDeclinedTheGame");
            return;
        }

        _services.EndfieldBridge.LaunchInProgress = true;
        RefreshBridgeControls();
        _services.Workspace.Status = T("EndfieldBridge.StartingEndfield");
        try
        {
            LaunchStatus? status = await EndfieldBridgeService.WaitForLaunchStatusAsync(service.Store, sessionId, TimeSpan.FromSeconds(profile.WithMods ? 120 : 45), () => _services.IsClosing);
            bool english = profile.English;
            switch (status?.State)
            {
                case LaunchStates.GameStarted:
                    _services.Workspace.Status = status!.Message.For(english);
                    _services.Status.Notify(T("EndfieldBridge.Started"));
                    break;
                case LaunchStates.Failed:
                case LaunchStates.Exited:
                    _services.Workspace.Status = T("EndfieldBridge.TheEndfieldLaunchDidNotComplete");
                    await ShowMessageAsync(status!.Message.For(english), T("EndfieldBridge.EndfieldLaunch"));
                    break;
                case LaunchStates.LoaderStarted:
                    _services.Workspace.Status = T("EndfieldBridge.HandedToXXMIEFMINoGame");
                    break;
                default:
                    _services.Workspace.Status = T("EndfieldBridge.NoLaunchResultYetOpenThe");
                    break;
            }
        }
        finally
        {
            _services.EndfieldBridge.LaunchInProgress = false;
            if (!_services.IsClosing) RefreshBridgeControls();
        }
    }
}
