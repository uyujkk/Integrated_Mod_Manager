using System.Diagnostics;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void RefreshSettingsPane()
    {
        SettingsTitleTextBlock.Text = T("Common.ApplicationSettings");
        SettingsSubtitleTextBlock.Text = T("Settings.LanguageThemeRepositoryEntryPointsAnd");
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
        LanguageToggleButton.Content = _currentLanguage == AppLanguage.ZhCn ? "English" : "中文";
        ThemeToggleButton.Content = _isDarkTheme ? T("Common.LightTheme") : T("Common.DarkTheme");
        EditOnlineRepositoryButton.Content = T("Settings.EditCurrentRepositoryCategory");
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
        LanguageSettingTitleTextBlock.Text = T("Settings.DisplayLanguage");
        LanguageSettingDescriptionTextBlock.Text = T("Settings.SwitchTheEntireAppBetweenChinese");
        ThemeSettingTitleTextBlock.Text = T("Settings.AppTheme");
        ThemeSettingDescriptionTextBlock.Text = T("Settings.SwitchBetweenLightAndDarkFluent");
        MotionSettingTitleTextBlock.Text = T("Settings.ReduceMotion");
        MotionSettingDescriptionTextBlock.Text = T("Settings.DisablePageSlideInAndImage");
        DensitySettingTitleTextBlock.Text = T("Settings.InterfaceDensity");
        DensitySettingDescriptionTextBlock.Text = T("Settings.CompactModeShowsMoreContentOn");
        RefreshTraySetting();
        UpdateTrayLanguage();

        _isApplyingAppearanceSettings = true;
        ReduceMotionToggleSwitch.IsOn = _reduceMotion;
        ReduceMotionToggleSwitch.OnContent = T("Common.On2");
        ReduceMotionToggleSwitch.OffContent = T("Common.Off2");
        _isApplyingAppearanceSettings = false;

        _isApplyingDensitySelection = true;
        DensityComboBox.Items.Clear();
        DensityComboBox.Items.Add(new ComboBoxItem { Content = T("Settings.Comfortable"), Tag = InterfaceDensity.Comfortable });
        DensityComboBox.Items.Add(new ComboBoxItem { Content = T("Settings.Compact"), Tag = InterfaceDensity.Compact });
        DensityComboBox.SelectedItem = DensityComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => item.Tag is InterfaceDensity density && density == _interfaceDensity);
        _isApplyingDensitySelection = false;
        ApplyInterfaceDensity();
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
            return;
        }

        string source = GetEffectiveOnlineSource(repository);
        string gameName = GetEffectiveOnlineGameName(repository);
        string categoryId = GetEffectiveOnlineCategoryId(repository);
        string notes = string.IsNullOrWhiteSpace(repository.Notes)
            ? T("Settings.None")
            : NormalizeUiText(repository.Notes);
        string wiki = string.IsNullOrWhiteSpace(repository.WikiUrl)
            ? T("Settings.NotSet")
            : repository.WikiUrl;
        int mappingCount = repository.CharacterCategoryMappings.Count;

        SettingsPlaceholderTextBlock.Text = T("Settings.RepositorySourceGameSkinCategoryID", repository.Name, source, (string.IsNullOrWhiteSpace(gameName) ? "未填写" : gameName), categoryId, wiki, mappingCount, notes, (string.IsNullOrWhiteSpace(gameName) ? "Not set" : gameName));
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

    private void OnThemeToggleClicked(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_isDarkTheme);
        SaveConfig();
    }

    private void OnReduceMotionToggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingAppearanceSettings)
        {
            return;
        }

        _reduceMotion = ReduceMotionToggleSwitch.IsOn;
        SaveShellConfig();
        ShowAppNotification(
            _reduceMotion ? "已减少界面动态效果。" : "已恢复界面动态效果。",
            _reduceMotion ? "Interface motion has been reduced." : "Interface motion has been restored.",
            InfoBarSeverity.Informational);
    }

    private void OnDensitySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingDensitySelection || DensityComboBox.SelectedItem is not ComboBoxItem { Tag: InterfaceDensity density })
        {
            return;
        }

        _interfaceDensity = density;
        ApplyInterfaceDensity();
        SaveShellConfig();
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

    private void OnLanguageToggleClicked(object sender, RoutedEventArgs e)
    {
        SaveCurrentModBindings();
        _currentLanguage = _currentLanguage == AppLanguage.ZhCn ? AppLanguage.EnUs : AppLanguage.ZhCn;
        ApplyLanguage();
        RefreshVisibleShortcutDescriptionsForLanguage();
        RebuildShortcutPaneForLanguage();
        RefreshSettingsPane();
        RefreshUpdatesPane();
        SaveConfig();
        // Re-project the same raw cache in the new language; reject the older translation.
        if (OnlineDetailsSplitView.IsPaneOpen && _activeOnlineDetailMod is OnlineModCard mod)
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
