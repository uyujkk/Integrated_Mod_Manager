using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.EndfieldLaunch;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// Endfield launch settings and the integrated launch. The launch itself is the AGPL-3.0-only
// IntegratedModManager.EndfieldLaunch component; this page only edits its settings and starts it.
public sealed partial class MainWindow
{

    private void RefreshEndfieldBridgeControls()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        Dashboard.BridgeVisible = EndfieldBridgeService.CanUse(repository);
        Dashboard.BridgeLabel = repository?.UseEndfieldBridge == true
            ? T("EndfieldBridge.LaunchIntegratedEndfieldLaunchSettingsRisks")
            : T("EndfieldBridge.LaunchExistingLoaderIntegratedLaunchAvailable");
        Dashboard.BridgeEnabled = !_services.EndfieldBridge.OperationInProgress && !_services.EndfieldBridge.LaunchInProgress;
    }

    private async void OnBridgeOptionsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSelectedRepositoryFromInputs();
            if (GetSelectedRepository() is { } repository && EndfieldBridgeService.CanUse(repository))
                await ShowEndfieldBridgeOptionsAsync(repository);
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnBridgeOptionsClicked", ex);
        }
    }

    private async Task ShowEndfieldBridgeOptionsAsync(WorkspaceRepository repository)
    {
        if (_services.EndfieldBridge.OperationInProgress) return;
        _services.EndfieldBridge.OperationInProgress = true;
        RefreshEndfieldBridgeControls();
        try
        {
            EndfieldLaunchService service = EndfieldBridgeService.CreateLaunchService();
            bool migrated = _services.EndfieldBridge.ImportLegacySettings(repository, service);
            if (migrated) SaveShellConfig();
            EndfieldLaunchSettingsDialog.Settings? settings = await EndfieldLaunchSettingsDialog.ShowAsync(_services, new EndfieldLaunchSettingsDialog.Host(
                (operation, action) => RunUiActionAsync(operation, action), extensions => PickFileAsync(extensions), OpenTextDocument,
                url => OpenExternalUrlAsync(url, T("EndfieldBridge.FailedToOpenPage")), ReportUiActionFailure), repository, service, migrated);
            if (settings is not null)
            {
                repository.UseEndfieldBridge = settings.UseIntegrated;
                if (repository.UseEndfieldBridge) repository.EndfieldRiskAccepted = true;
                repository.EndfieldGamePath = settings.GamePath;
                repository.EndfieldParentPath = settings.ParentPath;
                repository.EndfieldGameArguments = settings.Arguments;
                repository.EndfieldUseMods = settings.UseMods;
                repository.LauncherPath = settings.LauncherPath;
                if (GetSelectedRepository()?.Id == repository.Id) Paths.Launcher = repository.LauncherPath;
                StatusTextBlock.Text = repository.UseEndfieldBridge
                    ? T("EndfieldBridge.EndfieldLaunchSettingsSavedRunLauncher")
                    : T("EndfieldBridge.SavedRunLauncherStartsTheExisting");
            }

            SaveShellConfig();
        }
        catch (Exception ex)
        {
            if (_onlineView._onlineWindowClosed) return;
            LogApplicationIssue("Endfield launch settings", ex);
            await ShowMessageAsync(T("EndfieldBridge.CouldNotOpenEndfieldLaunchSettings") + ex.Message, T("EndfieldBridge.EndfieldLaunch"));
        }
        finally
        {
            _services.EndfieldBridge.OperationInProgress = false;
            if (!_onlineView._onlineWindowClosed)
            {
                RefreshEndfieldBridgeControls();
                RefreshDashboard();
            }
        }

    }

    private void OpenTextDocument(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("EndfieldBridge.FileNotFound") + path);
        Process.Start(new ProcessStartInfo("notepad.exe", CommandLine.Quote(path)) { UseShellExecute = true });
    }

    private async Task RunRepositoryLauncherAsync()
    {
        if (_services.EndfieldBridge.OperationInProgress || _services.EndfieldBridge.LaunchInProgress) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        bool useIntegrated = EndfieldBridgeService.CanUse(repository) && repository!.UseEndfieldBridge;
        try
        {
            if (useIntegrated)
            {
                SaveSelectedRepositoryFromInputs();
                await StartIntegratedEndfieldLaunchAsync(repository!);
                return;
            }

            if (await TryStartThroughXxmiAsync(repository)) return;

            string loader = EndfieldBridgeService.CanUse(repository)
                ? EndfieldBridgeService.ResolveLoader(Paths.Launcher)
                : Paths.Launcher.Trim();
            if (string.IsNullOrWhiteSpace(loader) || !File.Exists(loader))
            {
                await ShowMessageAsync(T("EndfieldBridge.NoValidDefaultXXMIPathWas"), T("EndfieldBridge.LauncherNotSet"));
                return;
            }

            _services.Launch.StartLoader(loader);
            StatusTextBlock.Text = T("EndfieldBridge.ExistingLoaderStarted");
            if (string.IsNullOrWhiteSpace(Paths.Launcher)) Paths.Launcher = loader;
            SaveConfig();
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
        RepositoryPresetInfo? preset = BuiltInRepositoryPresets.FirstOrDefault(candidate => IsRepositoryForPreset(repository, candidate));
        if (XxmiLaunchPolicy.ImporterFor(preset?.Id, repository.XxmiImporter) is not { } importer) return false;
        string loader = EndfieldBridgeService.ResolveLoader(Paths.Launcher);
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
            LogApplicationIssue("Read XXMI configuration", ex);
        }

        _services.Launch.StartWithArguments(loader, XxmiLaunchPolicy.Arguments(importer), workingDirectory);
        StatusTextBlock.Text = T("XxmiLaunch.Started", importer);
        if (string.IsNullOrWhiteSpace(Paths.Launcher)) Paths.Launcher = loader;
        SaveConfig();
        return true;
    }

    private async Task StartIntegratedEndfieldLaunchAsync(WorkspaceRepository repository)
    {
        EndfieldLaunchService service = EndfieldBridgeService.CreateLaunchService();
        if (_services.EndfieldBridge.ImportLegacySettings(repository, service)) SaveShellConfig();
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
        LaunchProfile profile = EndfieldBridgeService.BuildLaunchProfile(repository, !IsChineseUi, sessionId);
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
            StatusTextBlock.Text = T("EndfieldBridge.AdministratorApprovalWasDeclinedTheGame");
            return;
        }

        _services.EndfieldBridge.LaunchInProgress = true;
        RefreshEndfieldBridgeControls();
        StatusTextBlock.Text = T("EndfieldBridge.StartingEndfield");
        try
        {
            LaunchStatus? status = await EndfieldBridgeService.WaitForLaunchStatusAsync(service.Store, sessionId, TimeSpan.FromSeconds(profile.WithMods ? 120 : 45), () => _onlineView._onlineWindowClosed);
            bool english = profile.English;
            switch (status?.State)
            {
                case LaunchStates.GameStarted:
                    StatusTextBlock.Text = status!.Message.For(english);
                    ShowAppNotification(T("EndfieldBridge.Started"));
                    break;
                case LaunchStates.Failed:
                case LaunchStates.Exited:
                    StatusTextBlock.Text = T("EndfieldBridge.TheEndfieldLaunchDidNotComplete");
                    await ShowMessageAsync(status!.Message.For(english), T("EndfieldBridge.EndfieldLaunch"));
                    break;
                case LaunchStates.LoaderStarted:
                    StatusTextBlock.Text = T("EndfieldBridge.HandedToXXMIEFMINoGame");
                    break;
                default:
                    StatusTextBlock.Text = T("EndfieldBridge.NoLaunchResultYetOpenThe");
                    break;
            }
        }
        finally
        {
            _services.EndfieldBridge.LaunchInProgress = false;
            if (!_onlineView._onlineWindowClosed) RefreshEndfieldBridgeControls();
        }
    }

    // Equal-width command buttons in rows, so long labels in either language never clip.
}
