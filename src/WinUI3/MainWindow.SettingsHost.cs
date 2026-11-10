using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Configuration;
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
        RefreshAppUpdateInstallState();

        if (_updateStatus is null)
        {
            SetUpdateStatus(M("Updates.CurrentVersion", AppVersion));
        }
        else
        {
            SettingsView.UpdateStatus = T(_updateStatus);
        }

        PopulateUpdateCheckIntervalOptions();
        RefreshSettingsOnlineConfigSummary();
        RefreshUpdateDetailsView();
        RefreshBackupManagerText();
        RefreshEndfieldBridgeControls();
    }

    private void RefreshAppearanceSettings()
    {
        RefreshTraySetting();
        RefreshTitleBarSearchSetting();
        UpdateTrayLanguage();
        ApplyInterfaceDensity();
    }

    private void RefreshStartupUpdateSetting()
    {
        if (!AppSelfUpdateEnabled)
        {
            _checkAppUpdatesOnStartup = false;
        }
        _isApplyingStartupUpdateSetting = true;
        SettingsView.CheckOnStartup = _checkAppUpdatesOnStartup;
        _isApplyingStartupUpdateSetting = false;
    }

    // What the update button can do now.
    private void RefreshAppUpdateInstallState()
        => SettingsView.SetInstallState(
            _localUpdatePackageVersion?.ToString(),
            hasReleasePage: !string.IsNullOrWhiteSpace(_latestReleaseUrl),
            canDownloadRelease: !string.IsNullOrWhiteSpace(_latestReleasePackageUrl) && IsReleaseNewer(_latestReleaseTag ?? string.Empty));

    // Copies the loaded choices into the view model, then applies whatever the user changes there.
    private void InitializeAppearanceSettings()
    {
        AppearanceSettingsViewModel appearance = SettingsView.Appearance;
        _themePreference = ThemePreferences.Parse(_storedThemePreference, _legacyDarkTheme);
        appearance.SetLanguageOptions(UiText.Languages.Select(UiText.LanguageName).ToArray());
        appearance.LanguageIndex = Math.Max(0, IndexOfLanguage(_services.Language.Current));
        appearance.Theme = _themePreference;
        appearance.DensityIndex = _interfaceDensity == InterfaceDensity.Compact ? 1 : 0;
        appearance.ReduceMotion = _reduceMotion;
        appearance.PropertyChanged += OnAppearanceSettingChanged;
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_themePreference == ThemePreference.System)
            {
                ApplyTheme(ThemePreferences.IsDark(_themePreference, IsSystemThemeDark()));
            }
        });
        ApplyTheme(ThemePreferences.IsDark(_themePreference, IsSystemThemeDark()));
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
                _themePreference = appearance.Theme;
                ApplyTheme(ThemePreferences.IsDark(_themePreference, IsSystemThemeDark()));
                SaveConfig();
                SaveShellConfig();
                break;
            case nameof(AppearanceSettingsViewModel.DensityIndex):
                InterfaceDensity density = appearance.DensityIndex == 1 ? InterfaceDensity.Compact : InterfaceDensity.Comfortable;
                if (density != _interfaceDensity)
                {
                    _interfaceDensity = density;
                    ApplyInterfaceDensity();
                    SaveShellConfig();
                }
                break;
            case nameof(AppearanceSettingsViewModel.ReduceMotion):
                if (appearance.ReduceMotion != _reduceMotion)
                {
                    _reduceMotion = appearance.ReduceMotion;
                    SaveShellConfig();
                    ShowAppNotification(
            _reduceMotion ? T("Settings.MotionReduced") : T("Settings.MotionRestored"),
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
                _checkAppUpdatesOnStartup = ApplicationReleasePolicy.CanCheckApplicationUpdates(SettingsView.CheckOnStartup);
                SaveShellConfig();
                break;
            case nameof(SettingsViewModel.UpdateIntervalIndex):
                if (!AppSelfUpdateEnabled || _isApplyingUpdateCheckIntervalSelection) return;
                try
                {
                    _updateCheckInterval = (UpdateCheckInterval)SettingsView.UpdateIntervalIndex;
                    SaveShellConfig();
                }
                catch (Exception ex) { ReportUiActionFailure("OnUpdateCheckIntervalSelectionChanged", ex); }
                break;
            case nameof(SettingsViewModel.MinimizeToTray):
                if (_isApplyingTraySetting) return;
                _minimizeToTray = SettingsView.MinimizeToTray;
                SetTrayIconVisibility(_minimizeToTray);
                SaveShellConfig();
                break;
            case nameof(SettingsViewModel.ShowTitleBarSearch):
                if (_isApplyingTitleBarSearchSetting) return;
                _showTitleBarSearch = SettingsView.ShowTitleBarSearch;
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
            case SettingsAction.CheckUpdates: OnCheckUpdatesClicked(this, args); break;
            case SettingsAction.InstallUpdate: OnInstallUpdateClicked(this, args); break;
            case SettingsAction.EditOnlineRepository: OnEditOnlineRepositoryClicked(this, args); break;
            case SettingsAction.EditCharacterMappings: OnEditCharacterMappingsClicked(this, args); break;
            case SettingsAction.ExportDiagnostics: OnExportDiagnosticsClicked(this, args); break;
            case SettingsAction.SubmitDiagnostics: OnSubmitDiagnosticsClicked(this, args); break;
            case SettingsAction.CopyFeedbackGroup: CopyFeedbackGroupNumber(); break;
            case SettingsAction.OpenDiscord: OpenFeedbackDiscord(); break;
            case SettingsAction.OpenLink: OpenProjectResource(request.Argument!); break;
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

        string source = _onlineView.GetEffectiveOnlineSource(repository);
        string gameName = _onlineView.GetEffectiveOnlineGameName(repository);
        string categoryId = _onlineView.GetEffectiveOnlineCategoryId(repository);
        bool ready = _onlineView.IsGameBananaSource(source) && !string.IsNullOrWhiteSpace(categoryId);
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
        SaveCurrentModBindings();
        // The pages relabel themselves and the choice is saved (MainWindow.AppearanceEvents.cs).
        _services.Language.Change(language);
    }

    private void OpenGitHubRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubRepositoryUrl,
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
