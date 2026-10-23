using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private void RefreshSettingsPane()
    {
        SettingsTitleTextBlock.Text = L("应用设置", "Application Settings");
        SettingsSubtitleTextBlock.Text = L("把语言、主题、仓库入口和更新检查集中放在这里。", "Language, theme, repository entry points, and update checks are collected here.");
        SettingsAppearanceTitleTextBlock.Text = L("界面与语言", "Appearance and Language");
        RefreshAppearanceSettings();
        RefreshProductivitySettingsText();
        SettingsProjectTitleTextBlock.Text = L("项目链接与软件版本", "Project Links and App Version");
        SettingsProjectHintTextBlock.Text = L("这里保留 GitHub 仓库入口和软件版本检查；Mod 更新请使用左侧的“更新”模块。", "This section keeps the GitHub repository link and app-version checks. Use the Updates section in the left navigation for mod updates.");
        RefreshProjectResourceText();
        UpdateCheckIntervalLabelTextBlock.Text = L("软件更新频率", "App update interval");
        UpdateCheckIntervalHintTextBlock.Text = L("这里只影响软件版本检查，不影响 Mod 更新检查。Mod 更新频率请到左侧“更新”模块设置。", "This only affects app-version checks, not mod update checks. Configure mod update frequency in the Updates section.");
        if (!AppSelfUpdateEnabled)
        {
            SettingsProjectHintTextBlock.Text = L("本地测试版已关闭软件自身更新，请按随包说明独立使用；Mod 更新不受影响。", "App self-updates are disabled in this local test build. Follow the included guide; Mod updates are unaffected.");
            UpdateCheckIntervalHintTextBlock.Text = L("测试版已禁用软件更新检查及自动安装，不会替换稳定版。", "App update checks and automatic installation are disabled in this beta. It does not replace the stable app.");
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
        ThemeToggleButton.Content = _isDarkTheme ? L("切换浅色", "Light theme") : L("切换深色", "Dark theme");
        EditOnlineRepositoryButton.Content = L("编辑当前仓库在线分类", "Edit Current Repository Category");
        EditCharacterMappingsButton.Content = L("角色分类映射", "Character Category Mapping");
        OpenGitHubButton.Content = L("打开 GitHub 仓库", "Open GitHub Repository");
        CheckUpdatesButton.Content = _isCheckingUpdates ? L("检查中...", "Checking...") : L("检查软件更新", "Check App Updates");
        CheckUpdatesButton.IsEnabled = AppSelfUpdateEnabled && !_isCheckingUpdates;
        if (!AppSelfUpdateEnabled)
        {
            CheckUpdatesButton.Content = L("测试版未接入自动更新", "Beta app updates disabled");
            InstallUpdateButton.Content = L("查看公开发布页", "View Public Releases");
            return;
        }
        InstallUpdateButton.Content = _localUpdatePackageVersion is not null
            ? L($"安装本地更新 v{_localUpdatePackageVersion}", $"Install Local Update v{_localUpdatePackageVersion}")
            : string.IsNullOrWhiteSpace(_latestReleaseUrl)
                ? L("检查并获取更新", "Check and Get Update")
                : !string.IsNullOrWhiteSpace(_latestReleasePackageUrl) && IsReleaseNewer(_latestReleaseTag ?? string.Empty)
                    ? L("下载并安装更新", "Download and Install Update")
                    : L("打开更新页面", "Open Release Page");
    }

    private void RefreshProjectResourceText()
    {
        ProjectResourcesTitleTextBlock.Text = L("项目与开源组件", "Project and Open-Source Components");
        ProjectResourcesHintTextBlock.Text = L(
            "列出软件运行时使用或调用的主要开源项目，以及本项目和作者主页。点击任意项目可打开其官方地址。",
            "Major open-source projects used or invoked at runtime, plus this project and the author's homepage. Select an item to open its official address.");

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
        LanguageSettingTitleTextBlock.Text = L("显示语言", "Display language");
        LanguageSettingDescriptionTextBlock.Text = L("切换整个应用的中文或英文界面。", "Switch the entire app between Chinese and English.");
        ThemeSettingTitleTextBlock.Text = L("应用主题", "App theme");
        ThemeSettingDescriptionTextBlock.Text = L("在浅色和深色 Fluent 外观之间切换。", "Switch between light and dark Fluent appearances.");
        MotionSettingTitleTextBlock.Text = L("减少动态效果", "Reduce motion");
        MotionSettingDescriptionTextBlock.Text = L("关闭页面滑入和图片淡入效果，减少视觉移动。", "Disable page slide-in and image fade effects to reduce visual movement.");
        DensitySettingTitleTextBlock.Text = L("界面密度", "Interface density");
        DensitySettingDescriptionTextBlock.Text = L("紧凑模式可以在同一屏幕显示更多内容。", "Compact mode shows more content on the same screen.");
        RefreshTraySetting();
        UpdateTrayLanguage();

        _isApplyingAppearanceSettings = true;
        ReduceMotionToggleSwitch.IsOn = _reduceMotion;
        ReduceMotionToggleSwitch.OnContent = L("开", "On");
        ReduceMotionToggleSwitch.OffContent = L("关", "Off");
        _isApplyingAppearanceSettings = false;

        _isApplyingDensitySelection = true;
        DensityComboBox.Items.Clear();
        DensityComboBox.Items.Add(new ComboBoxItem { Content = L("舒适", "Comfortable"), Tag = InterfaceDensity.Comfortable });
        DensityComboBox.Items.Add(new ComboBoxItem { Content = L("紧凑", "Compact"), Tag = InterfaceDensity.Compact });
        DensityComboBox.SelectedItem = DensityComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => item.Tag is InterfaceDensity density && density == _interfaceDensity);
        _isApplyingDensitySelection = false;
        ApplyInterfaceDensity();
    }

    private void RefreshOnlineNavigationButtonLabels()
    {
        RefreshOnlineButton.Content = _isLoadingOnlineMods
            ? L("加载中...", "Loading...")
            : L("刷新在线列表", "Refresh Online List");
        OnlinePrevPageButton.Content = L("上一页", "Previous");
        OnlineNextPageButton.Content = L("下一页", "Next");
    }

    private void RefreshSettingsOnlineConfigSummary()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        SettingsPlaceholderTitleTextBlock.Text = L("仓库在线分类", "Repository Online Category");

        if (repository is null)
        {
            SettingsPlaceholderTextBlock.Text = L(
                "先在仓库总览选择一个仓库，然后在这里填写 GameBanana 来源、目标游戏、皮肤分类 ID 和 Wiki。",
                "Select a repository from the dashboard first, then configure its GameBanana source, target game, skin category ID, and Wiki here.");
            return;
        }

        string source = GetEffectiveOnlineSource(repository);
        string gameName = GetEffectiveOnlineGameName(repository);
        string categoryId = GetEffectiveOnlineCategoryId(repository);
        string notes = string.IsNullOrWhiteSpace(repository.Notes)
            ? L("无", "None")
            : NormalizeUiText(repository.Notes);
        string wiki = string.IsNullOrWhiteSpace(repository.WikiUrl)
            ? L("未填写", "Not set")
            : repository.WikiUrl;
        int mappingCount = repository.CharacterCategoryMappings.Count;

        SettingsPlaceholderTextBlock.Text = L(
            $"当前仓库：{repository.Name}\n来源：{source}\n目标游戏：{(string.IsNullOrWhiteSpace(gameName) ? "未填写" : gameName)}\n皮肤分类 ID：{categoryId}\nWiki：{wiki}\n手动角色映射：{mappingCount} 项\n备注：{notes}",
            $"Repository: {repository.Name}\nSource: {source}\nGame: {(string.IsNullOrWhiteSpace(gameName) ? "Not set" : gameName)}\nSkin category ID: {categoryId}\nWiki: {wiki}\nManual character mappings: {mappingCount}\nNotes: {notes}");
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
        AppShellGrid.Padding = new Thickness(compact ? 8 : 12);
        AppShellGrid.RowSpacing = compact ? 8 : 12;
        HeaderTitleTextBlock.FontSize = compact ? 19 : 21;
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

            UpdateStatusTextBlock.Text = L("已在默认浏览器中打开 GitHub 仓库。", "Opened the GitHub repository in the default browser.");
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(
                L("打开 GitHub 仓库失败：", "Failed to open the GitHub repository: ") + ex.Message,
                L("打开失败", "Open failed"));
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

            await OpenExternalUrlAsync(url, L("打开项目地址失败", "Failed to open project address"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("OnExternalResourceClicked", ex);
        }
    }
}
