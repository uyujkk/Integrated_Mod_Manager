using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private string L(string zh, string en)
    {
        return BetaLocalization.Resolve(_currentLanguage == AppLanguage.EnUs, zh, en);
    }

    private string NormalizeUiText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return _currentLanguage == AppLanguage.EnUs
            ? value
            : BetaLocalization.NormalizeChineseText(value);
    }

    private void ApplyLanguage()
    {
        Title = L($"集成化 Mod 管理器 {AppVersion}", $"Integrated Mod Manager {AppVersion}");

        BetaTitleTextBlock.Text = _shellLayoutMode == ShellLayoutMode.Compact
            ? "管理器"
            : L("工作区", "Workspace");
        BetaCaptionTextBlock.Text = L("统一管理仓库切换、在线资源浏览和本地 Mod 操作。", "Manage repository switching, online resources, and local mod actions in one place.");
        ApplyPrimaryNavigationContent();

        SecondaryNavTitleTextBlock.Text = _currentPrimarySection switch
        {
            PrimarySection.Dashboard => L("仓库预览", "Repository Preview"),
            PrimarySection.Repository => L("仓库切换", "Repository Switcher"),
            PrimarySection.Online => L("在线来源", "Online Sources"),
            PrimarySection.Updates => L("更新导航", "Updates Navigation"),
            _ => L("设置导航", "Settings Navigation")
        };
        SecondaryNavHintTextBlock.Text = _currentPrimarySection switch
        {
            PrimarySection.Dashboard => L("查看全部仓库或单个仓库的预览数据。", "Browse summary data for all repositories or a single repository."),
            PrimarySection.Repository => L("从这里切换工作区，主区继续复用现有单仓库逻辑。", "Switch workspaces here while reusing the existing single-repository view."),
            PrimarySection.Online => L("在线页会按当前仓库的来源映射加载后续内容。", "The online page will use the selected repository as its source mapping context."),
            PrimarySection.Updates => L("这里集中管理配置方案、安装回滚、下载任务和已追踪 Mod 更新。", "Manage profiles, install rollback, download tasks, and tracked mod updates here."),
            _ => L("在这里统一管理语言、主题、更新检查和项目链接。", "Manage language, theme, update checks, and project links here.")
        };
        ToolTipService.SetToolTip(AddRepositoryButton, L("新建仓库", "Create repository"));
        ToolTipService.SetToolTip(RenameRepositoryButton, L("编辑当前仓库", "Edit current repository"));
        ToolTipService.SetToolTip(DeleteRepositoryButton, L("删除当前仓库", "Delete current repository"));
        ToolTipService.SetToolTip(OpenGitHubButton, L("打开 GitHub 仓库", "Open GitHub repository"));
        ToolTipService.SetToolTip(CheckUpdatesButton, L("检查软件版本更新", "Check for app updates"));
        ToolTipService.SetToolTip(CheckModUpdatesButton, L("手动检查已追踪 Mod 更新", "Check tracked mod updates now"));
        ToolTipService.SetToolTip(EditOnlineRepositoryButton, L("编辑当前仓库在线分类", "Edit current repository online category"));
        ToolTipService.SetToolTip(RefreshOnlineButton, L("刷新在线 Mod 列表", "Refresh online mod list"));
        ToolTipService.SetToolTip(OnlinePrevPageButton, null);
        ToolTipService.SetToolTip(OnlineNextPageButton, null);
        ToolTipService.SetToolTip(OnlineSearchTextBox, L("搜索当前页的角色、Mod 或作者；author:作者名 仅搜索作者", "Search this page by character, mod or author; author:name searches authors only"));
        ToolTipService.SetToolTip(OnlineSortComboBox, L("切换当前在线列表的排序方式", "Change the sort order for the current online list"));
        ToolTipService.SetToolTip(PreviewImage, L("点击查看大图", "Click to view full size"));
        ToolTipService.SetToolTip(OnlineDetailHeroImage, L("点击查看大图", "Click to view full size"));
        AutomationProperties.SetName(AddRepositoryButton, L("新建仓库", "Create repository"));
        AutomationProperties.SetName(RenameRepositoryButton, L("编辑当前仓库", "Edit current repository"));
        AutomationProperties.SetName(DeleteRepositoryButton, L("删除当前仓库", "Delete current repository"));
        AutomationProperties.SetName(OpenGitHubButton, L("打开 GitHub 仓库", "Open GitHub repository"));
        AutomationProperties.SetName(CheckUpdatesButton, L("检查软件版本更新", "Check for app updates"));
        AutomationProperties.SetName(CheckModUpdatesButton, L("手动检查已追踪 Mod 更新", "Check tracked mod updates now"));
        AutomationProperties.SetName(EditOnlineRepositoryButton, L("编辑当前仓库在线分类", "Edit current repository online category"));
        AutomationProperties.SetName(RefreshOnlineButton, L("刷新在线 Mod 列表", "Refresh online mod list"));
        AutomationProperties.SetName(OnlinePrevPageButton, L("上一页", "Previous page"));
        AutomationProperties.SetName(OnlineNextPageButton, L("下一页", "Next page"));
        AutomationProperties.SetName(OnlineSearchTextBox, L("搜索当前页的角色、Mod 或作者", "Search this page by character, mod or author"));
        AutomationProperties.SetName(OnlineSortComboBox, L("在线列表排序", "Online list sort"));
        AutomationProperties.SetName(PreviewImage, L("打开 Mod 预览大图", "Open full-size mod preview"));
        AutomationProperties.SetName(OnlineDetailHeroImage, L("打开在线 Mod 大图", "Open full-size online mod image"));
        InitializeOnlineControls();

        DashboardTitleTextBlock.Text = L("仓库总览", "Repository Dashboard");
        DashboardSubtitleTextBlock.Text = L("在一个页面完成仓库切换、预设创建、路径状态检查和管理入口。", "Switch repositories, create presets, review path status, and enter management from one page.");
        DashboardAddRepositoryTextBlock.Text = L("新建仓库", "New Repository");
        DashboardEditRepositoryTextBlock.Text = L("编辑当前", "Edit Current");
        DashboardCurrentRepositoryLabelTextBlock.Text = L("当前工作仓库", "Active Repository");
        DashboardOpenRepositoryButton.Content = L("进入 Mod 管理", "Open Mod Management");
        DashboardRepoCountLabelTextBlock.Text = L("仓库数量", "Repositories");
        DashboardModCountLabelTextBlock.Text = L("Mod 总数", "Total Mods");
        DashboardReadyCountLabelTextBlock.Text = L("已就绪路径", "Ready Paths");
        DashboardPresetsTitleTextBlock.Text = L("游戏仓库预设", "Game Repository Presets");
        DashboardPresetsHintTextBlock.Text = L("预设已包含 GameBanana 分类 ID 和官方 Wiki 地址；点击后补充本地路径即可。", "Each preset includes its GameBanana category ID and official Wiki URL. Click one and add local paths.");
        DashboardZzzPresetTitleTextBlock.Text = L("绝区零", "Zenless Zone Zero");
        DashboardGenshinPresetTitleTextBlock.Text = L("原神", "Genshin Impact");
        DashboardHsrPresetTitleTextBlock.Text = L("崩坏：星穹铁道", "Honkai: Star Rail");
        DashboardEndfieldPresetTitleTextBlock.Text = L("明日方舟：终末地", "Arknights: Endfield");
        DashboardRepositoriesTitleTextBlock.Text = L("全部仓库", "All Repositories");
        DashboardRepositoriesHintTextBlock.Text = L("卡片会标记当前仓库；可直接切换、进入管理或打开对应 Wiki。", "Cards mark the active repository and provide direct switching, management, and Wiki access.");
        ToolTipService.SetToolTip(DashboardRepositoryComboBox, L("切换当前工作仓库", "Switch the active repository"));
        ToolTipService.SetToolTip(DashboardOpenRepositoryButton, L("打开当前仓库的 Mod 管理界面", "Open mod management for the active repository"));

        OnlineTitleTextBlock.Text = L("在线 Mod 浏览", "Online Mod Browser");
        OnlineSubtitleTextBlock.Text = L("这里保留在线 Mod 列表和右侧预览详情；不同游戏的 GameBanana 皮肤分类可在“设置”页面按仓库分别配置。", "This page keeps the online mod list and the right-side preview details. GameBanana skin categories for different games can be configured per repository in Settings.");
        OnlineRepositoryLabelTextBlock.Text = L("当前仓库", "Repository");
        OnlineSourceLabelTextBlock.Text = L("在线来源", "Online source");
        OnlineReadyLabelTextBlock.Text = L("准备状态", "Readiness");
        OnlineConfigTitleTextBlock.Text = L("当前仓库映射", "Current repository mapping");
        OnlineConfigHintTextBlock.Text = L("在线页面会优先读取当前仓库的来源站点、目标游戏和皮肤分类 ID。需要切换不同游戏时，直接去“设置”里改当前仓库即可。", "The online page reads the current repository's source site, target game, and skin category ID first. To switch games, edit the current repository in Settings.");
        OnlineCategoryLabelTextBlock.Text = L("分类 ID", "Category ID");
        OnlineNotesLabelTextBlock.Text = L("备注", "Notes");
        OnlinePreviewTitleTextBlock.Text = L("在线 Mod 列表", "Online Mod List");
        OnlinePreviewHintTextBlock.Text = L("这里读取的是外网 Mod 站点数据，如果加载较慢或失败，可能需要 VPN。", "This page loads data from an external mod site. If loading is slow or fails, a VPN may be required.");
        EditOnlineRepositoryButton.Content = L("编辑当前仓库在线分类", "Edit current repository online category");

        SettingsTitleTextBlock.Text = L("应用设置", "Application Settings");
        SettingsSubtitleTextBlock.Text = L("在这里配置语言、主题、更新检查和项目链接。", "Configure language, theme, update checks, and project links here.");
        SettingsAppearanceTitleTextBlock.Text = L("界面与语言", "Appearance and Language");
        RefreshAppearanceSettings();
        SettingsProjectTitleTextBlock.Text = L("项目链接与软件版本", "Project Links and App Version");
        SettingsProjectHintTextBlock.Text = L("这里保留 GitHub 仓库入口和软件版本检查；Mod 更新请使用左侧的“更新”模块。", "This section keeps the GitHub repository link and app-version checks. Use the Updates section in the left navigation for mod updates.");
        RefreshProjectResourceText();
        SettingsPlaceholderTitleTextBlock.Text = L("仓库在线配置", "Repository Online Config");
        SettingsPlaceholderTextBlock.Text = L("在线 Mod 页面需要的来源站点、分类 ID 和备注都集中放在这里管理。", "Manage the source site, category ID, and notes used by the online mod page here.");
        OpenGitHubButton.Content = L("打开 GitHub 仓库", "Open GitHub Repository");
        CheckUpdatesButton.Content = _isCheckingUpdates ? L("检查中...", "Checking...") : L("检查软件更新", "Check App Updates");
        UpdateStatusTextBlock.Text = string.IsNullOrWhiteSpace(UpdateStatusTextBlock.Text)
            ? L($"当前版本：{AppVersion}", $"Current version: {AppVersion}")
            : UpdateStatusTextBlock.Text;

        HeaderTitleTextBlock.Text = L("集成化 Mod 管理器", "Integrated Mod Manager");
        HeaderFrameworkBadgeTextBlock.Text = "WinUI 3";
        HeaderVersionBadgeTextBlock.Text = AppVersion;
        HeaderSubtitleTextBlock.Text = string.Empty;
        HeaderCaptionTextBlock.Text = L(
            "左侧先选第一层分类，再选第二层 Mod。双击第二层可按当前模式部署或移除。",
            "Select a first-level category first, then a second-level mod. Double-click a mod to deploy or remove it using the current mode.");

        LanguageToggleButton.Content = _currentLanguage == AppLanguage.ZhCn ? "English" : "中文";
        LanguageToggleButton.IsEnabled = true;
        ThemeToggleButton.Content = _isDarkTheme ? L("切换浅色", "Light theme") : L("切换深色", "Dark theme");

        PathSectionTitleTextBlock.Text = L("路径与启动器", "Paths and Launcher");
        PathSectionSubtitleTextBlock.Text = L(
            "把主文件夹、副文件夹和启动器统一放在这里，常用操作会直接使用这些路径。",
            "Keep the source folder, target folder, and launcher together here for quick access.");
        SourceLabelTextBlock.Text = L("源文件夹", "Source");
        TargetLabelTextBlock.Text = L("目标文件夹", "Target");
        LauncherLabelTextBlock.Text = L("启动器", "Launcher");
        PickSourceButton.Content = L("选择", "Browse");
        PickTargetButton.Content = L("选择", "Browse");
        PickLauncherButton.Content = L("选择程序", "Browse EXE");
        OpenSourceButton.Content = L("打开源文件夹", "Open Source");
        OpenTargetButton.Content = L("打开目标文件夹", "Open Target");
        OpenLauncherButton.Content = L("打开启动器位置", "Open Launcher Folder");
        LauncherTextBox.PlaceholderText = L("选择 XXMI Launcher.exe 或其他启动器", "Select XXMI Launcher.exe or another launcher");
        PathHintTextBlock.Text = L(
            "链接模式让加载器与仓库使用同一份文件；同一第一层角色改链其他 Mod 时，会自动断开旧链接。复制模式保持 v3.8 原有行为。",
            "Link mode shares one set of files between the loader and repository. Linking another mod under the same character automatically disconnects the old link. Copy mode keeps the original v3.8 behavior.");
        LinkDeploymentToggleSwitch.Header = L("使用目录联接部署 Mod（实验）", "Deploy Mods with directory junctions (experimental)");
        LinkDeploymentToggleSwitch.OnContent = L("链接", "Link");
        LinkDeploymentToggleSwitch.OffContent = L("复制", "Copy");
        RefreshButton.Content = L("刷新目录", "Refresh");
        ImportZipButton.Content = L("导入 Mod", "Import Mod");
        ToolTipService.SetToolTip(ImportZipButton, L("导入到当前选中文件夹", "Import into the selected folder"));
        RunLauncherButton.Content = L("运行启动器", "Run Launcher");
        ToggleCopyButton.Content = L("部署 Mod", "Deploy Mod");

        FirstCountLabelTextBlock.Text = L("第一层文件夹", "First-level folders");
        FirstCountHintTextBlock.Text = L("当前 Mod 仓库中的分类数量", "Number of categories in the source folder");
        SecondCountLabelTextBlock.Text = L("第二层文件夹", "Second-level folders");
        SecondCountHintTextBlock.Text = L("当前扫描到的 Mod 总数", "Total mod folders found in the current source");
        CurrentStateLabelTextBlock.Text = L("当前部署状态", "Deployment status");
        CurrentStateHintTextBlock.Text = L("区分普通副本和目录联接", "Distinguishes normal copies from directory junctions");
        CurrentFolderLabelTextBlock.Text = L("当前第二层文件夹", "Current second-level folder");
        CurrentFolderHintTextBlock.Text = L("这里显示当前选中的 Mod 名称", "Shows the currently selected mod name");

        FirstLevelSectionTitleTextBlock.Text = L("角色与分类", "Categories");
        FirstLevelSectionSubtitleTextBlock.Text = L("先选择分类目录", "Choose a category folder first");
        FirstLevelSearchTextBox.PlaceholderText = L("搜索第一层文件夹", "Search first-level folders");
        CreateFirstLevelButton.Content = L("新建第一层文件夹", "New Folder");
        RenameFirstLevelButton.Content = L("重命名当前第一层", "Rename");
        SecondLevelSectionTitleTextBlock.Text = L("Mod 收藏", "Mod Library");
        SecondLevelSectionSubtitleTextBlock.Text = L("再选择具体 Mod", "Then choose a specific mod");
        DeleteSecondLevelButton.Content = L("删除当前第二层 Mod", "Delete Mod");

        ShortcutSectionTitleTextBlock.Text = L("快捷键与功能", "Shortcut Notes");
        ShortcutSectionSubtitleTextBlock.Text = L("当前选中 Mod 的快捷键说明", "Shortcut notes for the selected mod");
        AddShortcutRowButton.Content = L("新增一行", "Add Row");
        ShortcutHintTextBlock.Text = L(
            "自动读取 Mod INI 的 [Key] 段，可手动修正；这里只记录游戏快捷键，不会在管理器中跳转或部署 Mod。",
            "Reads [Key] sections from Mod INIs; notes can be edited. These are game shortcuts only and never select or deploy Mods in the manager.");

        PreviewSectionTitleTextBlock.Text = L("默认图片预览", "Image Preview");
        PreviewSectionSubtitleTextBlock.Text = L(
            "优先 preview / cover / thumbnail / image，也支持把图片拖到这里。",
            "Prefers preview / cover / thumbnail / image, and also supports dragging an image here.");
        ModLinkSectionTitleTextBlock.Text = L("Mod 链接", "Mod Link");
        ModLinkSectionSubtitleTextBlock.Text = L(
            "为当前 Mod 绑定一个网页链接，可直接快速访问。",
            "Bind a web link to the current mod for quick access.");
        ModLinkTextBox.PlaceholderText = L("粘贴当前 Mod 对应的网址", "Paste the web link for the current mod");
        PreviewFooterTextBlock.Text = L(
            "预览区会显示当前第二层文件夹的图片，下方链接可一键用默认浏览器打开。",
            "The preview shows the current second-level folder image, and the link below opens in the default browser.");
        OpenModLinkButton.Content = L("快速访问", "Open Link");

        ProgressTextBlock.Text = string.IsNullOrWhiteSpace(ProgressTextBlock.Text)
            ? L("当前无复制任务", "No active copy task")
            : ProgressTextBlock.Text;
        AuthorTextBlock.Text = L("工具作者：uyujkk", "Tool author: uyujkk");

        ApplyShortcutPlaceholders();
        UpdateShortcutRowVisibility();
        UpdateLinkButtonState();
        RefreshLocalizedStates();
        ApplyTextOverrides();
        ApplyActionButtonPresentation();

        if (FirstLevelListView.SelectedItem is FirstLevelFolderItem firstItem)
        {
            string? selectedPath = _currentSecondLevelPath;
            PopulateSecondLevelList(firstItem, preserveStatus: true);
            SelectSecondLevelByPath(selectedPath);
        }
        else if (GetSelectedSecondLevelItem() is SecondLevelFolderItem secondItem)
        {
            ShowSecondLevelDetails(secondItem, preserveStatus: true);
        }
        else
        {
            CurrentFolderTextBlock.Text = StateNotSelectedText;
            CurrentStateTextBlock.Text = StateNotSelectedText;
            SetStateColor(CurrentStateTextBlock.Text);
            ClearPreview();
            SetDefaultStatus();
        }
    }

    private void ApplyTextOverrides()
    {
        PathSectionSubtitleTextBlock.Text = L(
            "把 Mod 存储文件夹、目标文件夹和启动器统一放在这里，常用操作可以直接使用这些路径。",
            "Keep the mod storage folder, target folder, and launcher together here for quick access.");
        SourceLabelTextBlock.Text = L("Mod 存储文件夹", "Mod Storage");
        TargetLabelTextBlock.Text = L("目标文件夹", "Target Folder");
        OpenSourceButton.Content = L("打开 Mod 存储文件夹", "Open Mod Storage");
        OpenTargetButton.Content = L("打开目标文件夹", "Open Target Folder");
        PathHintTextBlock.Text = L(
            "链接模式共享仓库文件；同一角色改链其他 Mod 会自动断开旧链接，移除时也不会删除仓库 Mod。",
            "Link mode shares repository files. Linking another Mod for the same character disconnects the old link, and removal never deletes the repository Mod.");
        ImportZipButton.Content = L("导入 Mod", "Import Mod");

        FirstCountHintTextBlock.Text = L("当前 Mod 存储文件夹中的分类数量", "Number of categories in the mod storage folder");

        ShortcutSectionTitleTextBlock.Text = L("快捷键与描述", "Shortcut and Description");
        ShortcutSectionSubtitleTextBlock.Text = L("为当前选中的 Mod 记录快捷键和描述", "Record a shortcut and description for the selected mod");
        ShortcutHintTextBlock.Text = L(
            "点击输入框录入游戏快捷键，支持单键、组合键和符号键；这里只保存说明，不触发 Mod 切换或部署。Tab 与窗口切换组合不会覆盖记录。",
            "Click a box to record game keys, combinations, or symbols. Notes never switch or deploy Mods. Tab and window-switching combinations do not overwrite the record.");
        RefreshPersistentSlotControls();
    }

    private void ApplyActionButtonPresentation()
    {
        CreateFirstLevelButton.Content = new FontIcon { Glyph = "\uE710", FontSize = 16 };
        RenameFirstLevelButton.Content = new FontIcon { Glyph = "\uE70F", FontSize = 16 };
        DeleteSecondLevelButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 };

        string createTip = L("新建第一层文件夹", "Create first-level folder");
        string renameTip = L("重命名当前第一层", "Rename first-level folder");
        string deleteTip = L("删除当前第二层 Mod", "Delete selected mod");

        ToolTipService.SetToolTip(CreateFirstLevelButton, createTip);
        ToolTipService.SetToolTip(RenameFirstLevelButton, renameTip);
        ToolTipService.SetToolTip(DeleteSecondLevelButton, deleteTip);

        AutomationProperties.SetName(CreateFirstLevelButton, createTip);
        AutomationProperties.SetName(RenameFirstLevelButton, renameTip);
        AutomationProperties.SetName(DeleteSecondLevelButton, deleteTip);
    }

    private void ApplyShortcutPlaceholders()
    {
        foreach (TextBox box in _shortcutKeyBoxes)
        {
            box.PlaceholderText = L("快捷键", "Shortcut");
        }

        foreach (TextBox box in _shortcutActionBoxes)
        {
            box.PlaceholderText = L("描述", "Description");
        }
    }

    private void SetDefaultStatus()
    {
        StatusTextBlock.Text = L(
                    $"请选择 Mod 存储文件夹和目标文件夹。当前版本：{AppVersion}",
            $"Choose a mod storage folder and target folder. Current version: {AppVersion}");
    }

    private void RefreshLocalizedStates()
    {
        string targetDir = (TargetTextBox.Text ?? string.Empty).Trim();
        foreach (FirstLevelFolderItem first in _firstLevelItems)
        {
            foreach (SecondLevelFolderItem second in first.Children)
            {
                second.State = GetFolderCopyState(targetDir, second.Path);
            }
        }
    }

    private string StateNotSelectedText => L("未选择", "Not selected");

    private string StateNotConfiguredText => L("未配置", "Not configured");

    private string StateCopiedText => L("已复制", "Copied");

    private string StateLinkedText => L("已链接", "Linked");

    private string StateMissingText => L("未部署", "Not deployed");
}
