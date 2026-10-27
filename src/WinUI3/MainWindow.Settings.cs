using System.Diagnostics;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void RefreshSettingsPane()
    {
        SettingsPageHeader.Title = T("Common.ApplicationSettings");
        SettingsPageHeader.Description = T("Settings.LanguageThemeRepositoryEntryPointsAnd");
        SettingsAppearanceTitleTextBlock.Text = T("Common.AppearanceAndLanguage");
        RefreshAppearanceSettings();
        RefreshProductivitySettingsText();
        SettingsProjectTitleTextBlock.Text = T("Common.ProjectLinksAndAppVersion");
        SettingsProjectHintTextBlock.Text = T("Common.ThisSectionKeepsTheGitHubRepository");
        RefreshProjectResourceText();
        UpdateCheckIntervalLabelTextBlock.Text = T("Settings.AppUpdateInterval");
        UpdateCheckIntervalHintTextBlock.Text = T("Settings.ThisOnlyAffectsAppVersionChecks");
        if (!AppSelfUpdateEnabled)
        {
            SettingsProjectHintTextBlock.Text = T("Settings.AppSelfUpdatesAreDisabledIn");
            UpdateCheckIntervalHintTextBlock.Text = T("Settings.AppUpdateChecksAndAutomaticInstallation");
            UpdateCheckIntervalComboBox.IsEnabled = false;
        }
        RefreshSettingsButtonLabels();

        if (string.IsNullOrWhiteSpace(_updateStatusZh) || string.IsNullOrWhiteSpace(_updateStatusEn))
        {
            SetUpdateStatus($"当前版本：{AppVersion}", $"Current version: {AppVersion}");
        }
        else
        {
            UpdateStatusTextBlock.Text = L(_updateStatusZh, _updateStatusEn);
        }

        PopulateUpdateCheckIntervalOptions();
        RefreshSettingsOnlineConfigSummary();
        RefreshUpdateDetailsView();
        RefreshBackupManagerText();
        RefreshEndfieldBridgeControls();
    }

    private void RefreshSettingsButtonLabels()
    {
        EditOnlineRepositoryButton.Content = T("Localization.EditCurrentRepositoryOnlineCategory");
        EditCharacterMappingsButton.Content = T("Common.CharacterCategoryMapping");
        OpenGitHubButton.Content = T("Common.OpenGitHubRepository");
        CheckUpdatesButton.Content = _isCheckingUpdates ? T("Common.Checking") : T("Common.CheckAppUpdates");
        CheckUpdatesButton.IsEnabled = AppSelfUpdateEnabled && !_isCheckingUpdates;
        if (!AppSelfUpdateEnabled)
        {
            CheckUpdatesButton.Content = T("Settings.BetaAppUpdatesDisabled");
            InstallUpdateButton.Content = T("Settings.ViewPublicReleases");
            return;
        }
        InstallUpdateButton.Content = _localUpdatePackageVersion is not null
            ? T("Settings.InstallLocalUpdateV", _localUpdatePackageVersion)
            : string.IsNullOrWhiteSpace(_latestReleaseUrl)
                ? T("Settings.CheckAndGetUpdate")
                : !string.IsNullOrWhiteSpace(_latestReleasePackageUrl) && IsReleaseNewer(_latestReleaseTag ?? string.Empty)
                    ? T("Settings.DownloadAndInstallUpdate")
                    : T("Settings.OpenReleasePage");
    }

    private void RefreshProjectResourceText()
    {
        ProjectResourcesTitleTextBlock.Text = T("Settings.ProjectAndOpenSourceComponents");
        ProjectResourcesHintTextBlock.Text = T("Settings.MajorOpenSourceProjectsUsedOr");

        foreach (Button resourceButton in ProjectResourcesGrid.Children.OfType<Button>())
        {
            if (resourceButton.Tag is not string url || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            ToolTipService.SetToolTip(resourceButton, url);
            string resourceName = resourceButton.Content is StackPanel panel
                ? panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? url
                : url;
            AutomationProperties.SetName(resourceButton, $"{resourceName} · {url}");
        }
    }

    private void RefreshAppearanceSettings()
    {
        LanguageSettingRow.Title = T("Settings.DisplayLanguage");
        LanguageSettingRow.Description = T("Settings.SwitchTheEntireAppBetweenChinese");
        ThemeSettingRow.Title = T("Settings.AppTheme");
        ThemeSettingRow.Description = T("Settings.SwitchBetweenLightAndDarkFluent");
        MotionSettingRow.Title = T("Settings.ReduceMotion");
        MotionSettingRow.Description = T("Settings.DisablePageSlideInAndImage");
        DensitySettingRow.Title = T("Settings.InterfaceDensity");
        DensitySettingRow.Description = T("Settings.CompactModeShowsMoreContentOn");
        RefreshTraySetting();
        UpdateTrayLanguage();

        ReduceMotionToggleSwitch.OnContent = T("Common.On2");
        ReduceMotionToggleSwitch.OffContent = T("Common.Off2");
        AppearanceSettings.SetOptionLabels(
            [T("Settings.FollowSystem"), T("Settings.Light"), T("Settings.Dark")],
            [T("Settings.Comfortable"), T("Settings.Compact")]);
        foreach (ComboBox comboBox in new[] { LanguageComboBox, ThemeComboBox, DensityComboBox })
        {
            AutomationProperties.SetName(comboBox, comboBox == LanguageComboBox
                ? LanguageSettingRow.Title
                : comboBox == ThemeComboBox ? ThemeSettingRow.Title : DensitySettingRow.Title);
        }
        ApplyInterfaceDensity();
    }

    // Copies the loaded choices into the view model, then applies whatever the user changes there.
    private void InitializeAppearanceSettings()
    {
        _themePreference = ThemePreferences.Parse(_storedThemePreference, _legacyDarkTheme);
        AppearanceSettings.LanguageIndex = _currentLanguage == AppLanguage.EnUs ? 1 : 0;
        AppearanceSettings.Theme = _themePreference;
        AppearanceSettings.DensityIndex = _interfaceDensity == InterfaceDensity.Compact ? 1 : 0;
        AppearanceSettings.ReduceMotion = _reduceMotion;
        AppearanceSettings.PropertyChanged += OnAppearanceSettingChanged;
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
            return _isDarkTheme;
        }
    }

    private void OnAppearanceSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppearanceSettingsViewModel.LanguageIndex):
                AppLanguage language = AppearanceSettings.LanguageIndex == 1 ? AppLanguage.EnUs : AppLanguage.ZhCn;
                if (language != _currentLanguage)
                {
                    SwitchLanguage(language);
                }
                break;
            case nameof(AppearanceSettingsViewModel.Theme):
                _themePreference = AppearanceSettings.Theme;
                ApplyTheme(ThemePreferences.IsDark(_themePreference, IsSystemThemeDark()));
                SaveConfig();
                SaveShellConfig();
                break;
            case nameof(AppearanceSettingsViewModel.DensityIndex):
                InterfaceDensity density = AppearanceSettings.DensityIndex == 1 ? InterfaceDensity.Compact : InterfaceDensity.Comfortable;
                if (density != _interfaceDensity)
                {
                    _interfaceDensity = density;
                    ApplyInterfaceDensity();
                    SaveShellConfig();
                }
                break;
            case nameof(AppearanceSettingsViewModel.ReduceMotion):
                if (AppearanceSettings.ReduceMotion != _reduceMotion)
                {
                    _reduceMotion = AppearanceSettings.ReduceMotion;
                    SaveShellConfig();
                    ShowAppNotification(
                        _reduceMotion ? "已减少界面动态效果。" : "已恢复界面动态效果。",
                        _reduceMotion ? "Interface motion has been reduced." : "Interface motion has been restored.",
                        InfoBarSeverity.Informational);
                }
                break;
        }
    }

    private void RefreshOnlineNavigationButtonLabels()
    {
        RefreshOnlineButton.Content = _isLoadingOnlineMods
            ? T("Common.Loading")
            : T("Settings.RefreshOnlineList");
        OnlinePrevPageButton.Content = T("Settings.Previous");
        OnlineNextPageButton.Content = T("Settings.Next");
    }

    private void RefreshSettingsOnlineConfigSummary()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        SettingsPlaceholderTitleTextBlock.Text = T("Settings.RepositoryOnlineCategory");

        if (repository is null)
        {
            SettingsPlaceholderTextBlock.Text = T("Settings.SelectARepositoryFromTheDashboard");
            SettingsOnlineInfoList.ItemsSource = null;
            return;
        }

        string source = GetEffectiveOnlineSource(repository);
        string gameName = GetEffectiveOnlineGameName(repository);
        string categoryId = GetEffectiveOnlineCategoryId(repository);
        bool ready = IsGameBananaSource(source) && !string.IsNullOrWhiteSpace(categoryId);
        string OrNotSet(string? value) => string.IsNullOrWhiteSpace(value) ? T("Settings.NotSet") : value;

        SettingsPlaceholderTextBlock.Text = T("Settings.OnlinePageReadsThisRepository");
        SettingsOnlineInfoList.ItemsSource = new List<SettingInfoItem>
        {
            new(T("Common.Repository"), repository.Name),
            new(T("Common.OnlineSource"), OrNotSet(source)),
            new(T("Dialogs.TargetGame"), OrNotSet(gameName)),
            new(T("Common.CategoryID"), OrNotSet(categoryId)),
            new(T("Dialogs.GameWiki"), OrNotSet(repository.WikiUrl)),
            new(T("Common.CharacterCategoryMapping"), T("Settings.MappingCount", repository.CharacterCategoryMappings.Count)),
            new(T("Common.Readiness"), ready ? T("Common.Ready") : T("Common.NeedsSetup")),
            new(T("Common.Notes"), string.IsNullOrWhiteSpace(repository.Notes) ? T("Settings.None") : NormalizeUiText(repository.Notes))
        };
    }

    private void OnConflictDetectionToggled(object sender, RoutedEventArgs e)
    {
        if (ConflictDetectionToggleSwitch is null)
        {
            return;
        }

        _enableConflictDetection = ConflictDetectionToggleSwitch.IsOn;
        SaveShellConfig();
    }

    private void OnLinkDeploymentToggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingRepositoryDeploymentMode || LinkDeploymentToggleSwitch is null)
        {
            return;
        }

        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        repository.UseDirectoryLinks = LinkDeploymentToggleSwitch.IsOn;
        SaveShellConfig();
        RefreshLocalizedStates();
        ApplySecondLevelSelectionState(GetSelectedSecondLevelItem(), preserveStatus: true);
    }

    private void ApplyInterfaceDensity()
    {
        bool compact = _interfaceDensity == InterfaceDensity.Compact;
        double padding = compact ? 8 : 12;
        // The title bar row reaches the window edges; only the content below it is inset.
        AppShellGrid.Padding = new Thickness(padding, 0, padding, padding);
        AppShellGrid.RowSpacing = compact ? 2 : 4;
        AppTitleBar.Margin = new Thickness(-padding, 0, -padding, 0);
        PrimaryNavBorder.Padding = new Thickness(8, compact ? 8 : 12, 8, compact ? 8 : 12);
        SecondaryNavBorder.Padding = new Thickness(compact ? 12 : 14);
        SecondaryNavPanel.Spacing = compact ? 5 : 8;
        FirstLevelListView.Padding = new Thickness(compact ? 3 : 6);
        SecondLevelListView.Padding = new Thickness(compact ? 3 : 6);
        ShortcutRowsPanel.Spacing = compact ? 6 : 10;
    }

    private void SwitchLanguage(AppLanguage language)
    {
        SaveCurrentModBindings();
        _currentLanguage = language;
        ApplyLanguage();
        RefreshVisibleShortcutDescriptionsForLanguage();
        RebuildShortcutPaneForLanguage();
        RefreshSettingsPane();
        RefreshUpdatesPane();
        SaveConfig();
        // Re-project the same raw cache in the new language; reject the older translation.
        if (IsOnlineDetailOpen && _activeOnlineDetailMod is OnlineModCard mod)
            _ = ShowOnlineModDetailsAsync(mod);
    }

    private void OnOpenGitHubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubRepositoryUrl,
                UseShellExecute = true
            });

            UpdateStatusTextBlock.Text = T("Settings.OpenedTheGitHubRepositoryInThe");
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                T("Settings.FailedToOpenTheGitHubRepository") + ex.Message,
                T("Common.OpenFailed"));
        }
    }

    private async void OnExternalResourceClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            await OpenExternalUrlAsync(url, T("Settings.FailedToOpenProjectAddress"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnExternalResourceClicked", ex);
        }
    }
}
