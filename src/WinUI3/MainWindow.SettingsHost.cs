using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.DeveloperTools;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// The window's side of the Settings page (Pages/SettingsPage.xaml): it fills SettingsViewModel with
// what only the window knows, applies the choices made there and answers the page's requests.
public sealed partial class MainWindow
{
    private SettingsViewModel SettingsView => _services.Settings;

    private void InitializeSettingsHost()
    {
        SettingsView.SelfUpdateEnabled = AppSelfUpdateEnabled;
        SettingsView.AppVersion = AppVersion;
        SettingsView.ActionRequested += OnSettingsActionRequested;
        SettingsView.PropertyChanged += OnSettingsViewChanged;
    }

    // Writes the window's state into the page's view model; called after anything it shows changes.
    private void RefreshSettingsPane()
    {
        RefreshAppearanceSettings();
        RefreshStartupUpdateSetting();
        _services.AppUpdates.RefreshAppUpdateInstallState();
        _services.AppUpdates.RefreshStatusText();

        PopulateUpdateCheckIntervalOptions();
        RefreshSettingsOnlineConfigSummary();
        _services.AppUpdates.RefreshUpdateDetailsView();
        RefreshBackupManagerText();
        _services.Launcher.RefreshBridgeControls();
    }

    private void RefreshAppearanceSettings()
    {
        RefreshTraySetting();
        RefreshTitleBarSearchSetting();
        _services.Tray.UpdateLanguage();
        ApplyInterfaceDensity();
    }

    private void RefreshStartupUpdateSetting()
    {
        if (!AppSelfUpdateEnabled)
        {
            _shell.CheckAppUpdatesOnStartup = false;
        }
        _isApplyingStartupUpdateSetting = true;
        SettingsView.CheckOnStartup = _shell.CheckAppUpdatesOnStartup;
        _isApplyingStartupUpdateSetting = false;
    }

    // Copies the loaded choices into the view model, then applies whatever the user changes there.
    private void InitializeAppearanceSettings()
    {
        AppearanceSettingsViewModel appearance = SettingsView.Appearance;
        _shell.ThemePreference = ThemePreferences.Parse(_shell.StoredThemePreference, _shell.LegacyDarkTheme);
        appearance.SetLanguageOptions(UiText.Languages.Select(UiText.LanguageName).ToArray());
        appearance.LanguageIndex = Math.Max(0, IndexOfLanguage(_services.Language.Current));
        appearance.Theme = _shell.ThemePreference;
        appearance.DensityIndex = _shell.InterfaceDensity == InterfaceDensity.Compact ? 1 : 0;
        appearance.ReduceMotion = _shell.ReduceMotion;
        appearance.PropertyChanged += OnAppearanceSettingChanged;
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_shell.ThemePreference == ThemePreference.System)
            {
                ApplyTheme(ThemePreferences.IsDark(_shell.ThemePreference, IsSystemThemeDark()));
            }
        });
        ApplyTheme(ThemePreferences.IsDark(_shell.ThemePreference, IsSystemThemeDark()));
    }

    private bool IsSystemThemeDark()
    {
        try
        {
            return _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128;
        }
        catch (Exception)
        {
            return _services.Appearance.IsDark;
        }
    }

    private void OnAppearanceSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        AppearanceSettingsViewModel appearance = SettingsView.Appearance;
        switch (e.PropertyName)
        {
            case nameof(AppearanceSettingsViewModel.LanguageIndex):
                string language = UiText.Languages[Math.Clamp(appearance.LanguageIndex, 0, UiText.Languages.Count - 1)];
                if (!string.Equals(language, _services.Language.Current, StringComparison.Ordinal))
                {
                    SwitchLanguage(language);
                }
                break;
            case nameof(AppearanceSettingsViewModel.Theme):
                _shell.ThemePreference = appearance.Theme;
                ApplyTheme(ThemePreferences.IsDark(_shell.ThemePreference, IsSystemThemeDark()));
                SaveConfig();
                SaveShellConfig();
                break;
            case nameof(AppearanceSettingsViewModel.DensityIndex):
                InterfaceDensity density = appearance.DensityIndex == 1 ? InterfaceDensity.Compact : InterfaceDensity.Comfortable;
                if (density != _shell.InterfaceDensity)
                {
                    _shell.InterfaceDensity = density;
                    ApplyInterfaceDensity();
                    SaveShellConfig();
                }
                break;
            case nameof(AppearanceSettingsViewModel.ReduceMotion):
                if (appearance.ReduceMotion != _shell.ReduceMotion)
                {
                    _shell.ReduceMotion = appearance.ReduceMotion;
                    SaveShellConfig();
                    ShowAppNotification(
            _shell.ReduceMotion ? T("Settings.MotionReduced") : T("Settings.MotionRestored"),
            InfoBarSeverity.Informational);
                }
                break;
        }
    }

    // The update and tray choices made on the page.
    private void OnSettingsViewChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.CheckOnStartup):
                if (_isApplyingStartupUpdateSetting) return;
                _shell.CheckAppUpdatesOnStartup = ApplicationReleasePolicy.CanCheckApplicationUpdates(SettingsView.CheckOnStartup);
                SaveShellConfig();
                break;
            case nameof(SettingsViewModel.UpdateIntervalIndex):
                if (!AppSelfUpdateEnabled || _isApplyingUpdateCheckIntervalSelection) return;
                try
                {
                    _shell.UpdateCheckInterval = (UpdateCheckInterval)SettingsView.UpdateIntervalIndex;
                    SaveShellConfig();
                }
                catch (Exception ex) { ReportUiActionFailure("OnUpdateCheckIntervalSelectionChanged", ex); }
                break;
            case nameof(SettingsViewModel.MinimizeToTray):
                if (_isApplyingTraySetting) return;
                _shell.MinimizeToTray = SettingsView.MinimizeToTray;
                _services.Tray.SetVisible(_shell.MinimizeToTray);
                SaveShellConfig();
                break;
            case nameof(SettingsViewModel.ShowTitleBarSearch):
                if (_isApplyingTitleBarSearchSetting) return;
                _shell.ShowTitleBarSearch = SettingsView.ShowTitleBarSearch;
                ApplyTitleBarSearchVisibility();
                SaveShellConfig();
                break;
        }
    }

    private void OnSettingsActionRequested(object? sender, SettingsActionRequest request)
    {
        var args = new RoutedEventArgs();
        switch (request.Action)
        {
            case SettingsAction.OpenGitHub: OpenGitHubRepository(); break;
            case SettingsAction.CheckUpdates: _services.AppUpdates.CheckNow(); break;
            case SettingsAction.InstallUpdate: _services.AppUpdates.Install(); break;
            case SettingsAction.EditOnlineRepository: OnEditOnlineRepositoryClicked(this, args); break;
            case SettingsAction.EditCharacterMappings: OnEditCharacterMappingsClicked(this, args); break;
            case SettingsAction.ExportDiagnostics: _services.Diagnostics.Export(); break;
            case SettingsAction.SubmitDiagnostics: _services.Diagnostics.Submit(); break;
            case SettingsAction.CopyFeedbackGroup: CopyFeedbackGroupNumber(); break;
            case SettingsAction.OpenDiscord: OpenFeedbackDiscord(); break;
            case SettingsAction.OpenLink: OpenProjectResource(request.Argument!); break;
            case SettingsAction.ClearGameBananaSignIn: _ = ClearGameBananaSignInAsync(); break;
            case SettingsAction.OpenDeveloperTools: OpenDeveloperTools(); break;
        }
    }

    // Issue #129: asks first, closes the GameBanana page if it is open, then deletes its saved sign-in.
    private async Task ClearGameBananaSignInAsync()
    {
        try
        {
            if (!await _services.Dialogs.DangerConfirmAsync(T("Settings.ClearGameBananaSignInConfirmTitle"),
                    T("Settings.ClearGameBananaSignInConfirm"), T("Settings.ClearGameBananaSignInClear")))
                return;
            bool cleared = await _services.GameBananaWeb.ClearAsync();
            ShowAppNotification(T(cleared ? "Settings.GameBananaSignInCleared" : "Settings.GameBananaSignInNothingToClear"),
                cleared ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogApplicationIssue("Clear GameBanana sign-in", ex);
            ShowAppNotification(T("Settings.GameBananaSignInNotCleared"), InfoBarSeverity.Error, ex);
        }
    }

    private void RefreshSettingsOnlineConfigSummary()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            SettingsView.SetOnlineInfo(T("Settings.SelectARepositoryFromTheDashboard"), []);
            return;
        }

        string source = _services.OnlineSource.Source(repository);
        string gameName = _services.OnlineSource.GameName(repository);
        string categoryId = _services.OnlineSource.CategoryId(repository);
        bool ready = _services.OnlineSource.IsGameBanana(source) && !string.IsNullOrWhiteSpace(categoryId);
        string OrNotSet(string? value) => string.IsNullOrWhiteSpace(value) ? T("Settings.NotSet") : value;

        SettingsView.SetOnlineInfo(T("Settings.OnlinePageReadsThisRepository"),
        [
            new(T("Common.Repository"), repository.Name),
            new(T("Common.OnlineSource"), OrNotSet(source)),
            new(T("Dialogs.TargetGame"), OrNotSet(gameName)),
            new(T("Common.CategoryID"), OrNotSet(categoryId)),
            new(T("Dialogs.GameWiki"), OrNotSet(repository.WikiUrl)),
            new(T("Settings.MappingLabel"), T("Settings.MappingCount", repository.CharacterCategoryMappings.Count)),
            new(T("Common.Readiness"), ready ? T("Common.Ready") : T("Common.NeedsSetup")),
            new(T("Common.Notes"), string.IsNullOrWhiteSpace(repository.Notes) ? T("Settings.None") : NormalizeUiText(repository.Notes))
        ]);
    }

    private static int IndexOfLanguage(string language)
        => UiText.Languages.ToList().FindIndex(item => string.Equals(item, language, StringComparison.OrdinalIgnoreCase));

    private void SwitchLanguage(string language)
    {
        _workspaceView?.SaveCurrentModBindings();
        // The pages relabel themselves and the choice is saved (MainWindow.AppearanceEvents.cs).
        _services.Language.Change(language);
    }

    private void OpenGitHubRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppUpdateService.GitHubRepositoryUrl,
                UseShellExecute = true
            });

            SettingsView.UpdateStatus = T("Settings.OpenedTheGitHubRepositoryInThe");
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                T("Settings.FailedToOpenTheGitHubRepository") + ex.Message,
                T("Common.OpenFailed"));
        }
    }

    private async void OpenProjectResource(string url)
    {
        try
        {
            await OpenExternalUrlAsync(url, T("Settings.FailedToOpenProjectAddress"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnExternalResourceClicked", ex);
        }
    }

    // The separately released Developer Tools: <install root>\DeveloperTools\IntegratedModManager.DeveloperTools.exe,
    // opened on this app's data folder. Without them, the release page that has their package.
    private async void OpenDeveloperTools() => await RunUiActionAsync("Open developer tools", async () =>
    {
        string? tools = DeveloperToolsLocation.Find(AppUpdateService.GetInstallRootPath());
        if (tools is null)
        {
            ShowAppNotification(T("Settings.DeveloperToolsNotInstalled"), InfoBarSeverity.Informational);
            await OpenExternalUrlAsync(DeveloperToolsLocation.ReleasesUrl, T("Settings.DeveloperTools"));
            return;
        }
        var start = new ProcessStartInfo(tools) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(tools)! };
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        Process.Start(start)?.Dispose();
    });

    private async void OpenFeedbackDiscord() =>
        await RunUiActionAsync("Open feedback Discord", () => OpenExternalUrlAsync(SettingsViewModel.FeedbackDiscordUrl, T("Settings.FeedbackDiscord")));

    private void CopyFeedbackGroupNumber()
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(SettingsViewModel.FeedbackQqGroup);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            SettingsView.DiagnosticsStatus = T("Settings.GroupNumberCopied", SettingsViewModel.FeedbackQqGroup);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Copy feedback group number", ex);
            SettingsView.DiagnosticsStatus = T("Settings.GroupNumberNotCopied", SettingsViewModel.FeedbackQqGroup);
        }
    }
}
