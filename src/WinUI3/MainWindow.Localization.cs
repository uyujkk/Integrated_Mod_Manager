using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // Text from the language files (Strings/*.json) by key.
    private string T(string key)
    {
        return L(UiText.Chinese(key), UiText.English(key));
    }

    // Text with values: the language files use {0}, {1}, ... placeholders.
    private string T(string key, params object?[] values)
    {
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, T(key), values);
    }

    // For text pairs built at runtime, such as stored status messages.
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
        Title = T("Common.IntegratedModManager", AppVersion);

        BetaTitleTextBlock.Text = _shellLayoutMode == ShellLayoutMode.Compact
            ? "管理器"
            : T("Localization.Workspace");
        BetaCaptionTextBlock.Text = T("Localization.ManageRepositorySwitchingOnlineResourcesAnd");
        ApplyPrimaryNavigationContent();

        SecondaryNavTitleTextBlock.Text = _currentPrimarySection switch
        {
            PrimarySection.Dashboard => T("Localization.RepositoryPreview"),
            PrimarySection.Repository => T("Localization.RepositorySwitcher"),
            PrimarySection.Online => T("Localization.OnlineSources"),
            PrimarySection.Updates => T("Localization.UpdatesNavigation"),
            _ => T("Localization.SettingsNavigation")
        };
        SecondaryNavHintTextBlock.Text = _currentPrimarySection switch
        {
            PrimarySection.Dashboard => T("Localization.BrowseSummaryDataForAllRepositories"),
            PrimarySection.Repository => T("Localization.SwitchWorkspacesHereWhileReusingThe"),
            PrimarySection.Online => T("Localization.TheOnlinePageWillUseThe"),
            PrimarySection.Updates => T("Localization.ManageProfilesInstallRollbackDownloadTasks"),
            _ => T("Localization.ManageLanguageThemeUpdateChecksAnd")
        };
        ToolTipService.SetToolTip(AddRepositoryButton, T("Localization.CreateRepository"));
        ToolTipService.SetToolTip(RenameRepositoryButton, T("Localization.EditCurrentRepository"));
        ToolTipService.SetToolTip(DeleteRepositoryButton, T("Localization.DeleteCurrentRepository"));
        ToolTipService.SetToolTip(OpenGitHubButton, T("Localization.OpenGitHubRepository"));
        ToolTipService.SetToolTip(CheckUpdatesButton, T("Localization.CheckForAppUpdates"));
        ToolTipService.SetToolTip(CheckModUpdatesButton, T("Localization.CheckTrackedModUpdatesNow"));
        ToolTipService.SetToolTip(EditOnlineRepositoryButton, T("Localization.EditCurrentRepositoryOnlineCategory"));
        ToolTipService.SetToolTip(RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        ToolTipService.SetToolTip(OnlinePrevPageButton, null);
        ToolTipService.SetToolTip(OnlineNextPageButton, null);
        ToolTipService.SetToolTip(OnlineSearchTextBox, T("Localization.SearchThisPageByCharacterMod2"));
        ToolTipService.SetToolTip(OnlineSortComboBox, T("Localization.ChangeTheSortOrderForThe"));
        ToolTipService.SetToolTip(PreviewImage, T("Localization.ClickToViewFullSize"));
        ToolTipService.SetToolTip(OnlineDetailHeroImage, T("Localization.ClickToViewFullSize"));
        AutomationProperties.SetName(AddRepositoryButton, T("Localization.CreateRepository"));
        AutomationProperties.SetName(RenameRepositoryButton, T("Localization.EditCurrentRepository"));
        AutomationProperties.SetName(DeleteRepositoryButton, T("Localization.DeleteCurrentRepository"));
        AutomationProperties.SetName(OpenGitHubButton, T("Localization.OpenGitHubRepository"));
        AutomationProperties.SetName(CheckUpdatesButton, T("Localization.CheckForAppUpdates"));
        AutomationProperties.SetName(CheckModUpdatesButton, T("Localization.CheckTrackedModUpdatesNow"));
        AutomationProperties.SetName(EditOnlineRepositoryButton, T("Localization.EditCurrentRepositoryOnlineCategory"));
        AutomationProperties.SetName(RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        AutomationProperties.SetName(OnlinePrevPageButton, T("Localization.PreviousPage"));
        AutomationProperties.SetName(OnlineNextPageButton, T("Localization.NextPage"));
        AutomationProperties.SetName(OnlineSearchTextBox, T("Localization.SearchThisPageByCharacterMod"));
        AutomationProperties.SetName(OnlineSortComboBox, T("Localization.OnlineListSort"));
        AutomationProperties.SetName(PreviewImage, T("Localization.OpenFullSizeModPreview"));
        AutomationProperties.SetName(OnlineDetailHeroImage, T("Localization.OpenFullSizeOnlineModImage"));
        InitializeOnlineControls();

        DashboardTitleTextBlock.Text = T("Localization.RepositoryDashboard");
        DashboardSubtitleTextBlock.Text = T("Localization.SwitchRepositoriesCreatePresetsReviewPath");
        DashboardAddRepositoryTextBlock.Text = T("Localization.NewRepository");
        DashboardEditRepositoryTextBlock.Text = T("Localization.EditCurrent");
        DashboardCurrentRepositoryLabelTextBlock.Text = T("Localization.ActiveRepository");
        DashboardOpenRepositoryButton.Content = T("Localization.OpenModManagement");
        DashboardRepoCountLabelTextBlock.Text = T("Localization.Repositories");
        DashboardModCountLabelTextBlock.Text = T("Localization.TotalMods");
        DashboardReadyCountLabelTextBlock.Text = T("Localization.ReadyPaths");
        DashboardPresetsTitleTextBlock.Text = T("Localization.GameRepositoryPresets");
        DashboardPresetsHintTextBlock.Text = T("Localization.EachPresetIncludesItsGameBananaCategory");
        DashboardZzzPresetTitleTextBlock.Text = T("Localization.ZenlessZoneZero");
        DashboardGenshinPresetTitleTextBlock.Text = T("Localization.GenshinImpact");
        DashboardHsrPresetTitleTextBlock.Text = T("Localization.HonkaiStarRail");
        DashboardEndfieldPresetTitleTextBlock.Text = T("Localization.ArknightsEndfield");
        DashboardRepositoriesTitleTextBlock.Text = T("Common.AllRepositories");
        DashboardRepositoriesHintTextBlock.Text = T("Localization.CardsMarkTheActiveRepositoryAnd");
        ToolTipService.SetToolTip(DashboardRepositoryComboBox, T("Localization.SwitchTheActiveRepository"));
        ToolTipService.SetToolTip(DashboardOpenRepositoryButton, T("Localization.OpenModManagementForTheActive"));

        OnlineTitleTextBlock.Text = T("Common.OnlineModBrowser");
        OnlineSubtitleTextBlock.Text = T("Localization.ThisPageKeepsTheOnlineMod");
        OnlineRepositoryLabelTextBlock.Text = T("Common.Repository");
        OnlineSourceLabelTextBlock.Text = T("Common.OnlineSource");
        OnlineReadyLabelTextBlock.Text = T("Common.Readiness");
        OnlineConfigTitleTextBlock.Text = T("Common.CurrentRepositoryMapping");
        OnlineConfigHintTextBlock.Text = T("Localization.TheOnlinePageReadsTheCurrent");
        OnlineCategoryLabelTextBlock.Text = T("Common.CategoryID");
        OnlineNotesLabelTextBlock.Text = T("Common.Notes");
        OnlinePreviewTitleTextBlock.Text = T("Common.OnlineModList");
        OnlinePreviewHintTextBlock.Text = T("Localization.ThisPageLoadsDataFromAn");
        EditOnlineRepositoryButton.Content = T("Localization.EditCurrentRepositoryOnlineCategory");

        SettingsTitleTextBlock.Text = T("Common.ApplicationSettings");
        SettingsSubtitleTextBlock.Text = T("Localization.ConfigureLanguageThemeUpdateChecksAnd");
        SettingsAppearanceTitleTextBlock.Text = T("Common.AppearanceAndLanguage");
        RefreshAppearanceSettings();
        SettingsProjectTitleTextBlock.Text = T("Common.ProjectLinksAndAppVersion");
        SettingsProjectHintTextBlock.Text = T("Common.ThisSectionKeepsTheGitHubRepository");
        RefreshProjectResourceText();
        SettingsPlaceholderTitleTextBlock.Text = T("Localization.RepositoryOnlineConfig");
        SettingsPlaceholderTextBlock.Text = T("Localization.ManageTheSourceSiteCategoryID");
        OpenGitHubButton.Content = T("Common.OpenGitHubRepository");
        CheckUpdatesButton.Content = _isCheckingUpdates ? T("Common.Checking") : T("Common.CheckAppUpdates");
        UpdateStatusTextBlock.Text = string.IsNullOrWhiteSpace(UpdateStatusTextBlock.Text)
            ? T("Localization.CurrentVersion", AppVersion)
            : UpdateStatusTextBlock.Text;

        HeaderTitleTextBlock.Text = T("Localization.IntegratedModManager");
        HeaderVersionBadgeTextBlock.Text = AppVersion;
        HeaderSubtitleTextBlock.Text = string.Empty;
        HeaderCaptionTextBlock.Text = T("Localization.SelectAFirstLevelCategoryFirst");

        LanguageToggleButton.Content = _currentLanguage == AppLanguage.ZhCn ? "English" : "中文";
        LanguageToggleButton.IsEnabled = true;
        ThemeToggleButton.Content = _isDarkTheme ? T("Common.LightTheme") : T("Common.DarkTheme");

        PathSectionTitleTextBlock.Text = T("Localization.PathsAndLauncher");
        PathSectionSubtitleTextBlock.Text = T("Localization.KeepTheSourceFolderTargetFolder");
        SourceLabelTextBlock.Text = T("Localization.Source");
        TargetLabelTextBlock.Text = T("Localization.Target");
        LauncherLabelTextBlock.Text = T("Common.Launcher");
        PickSourceButton.Content = T("Localization.Browse");
        PickTargetButton.Content = T("Localization.Browse");
        PickLauncherButton.Content = T("Localization.BrowseEXE");
        OpenSourceButton.Content = T("Localization.OpenSource");
        OpenTargetButton.Content = T("Localization.OpenTarget");
        OpenLauncherButton.Content = T("Localization.OpenLauncherFolder");
        LauncherTextBox.PlaceholderText = T("Localization.SelectXXMILauncherExeOrAnother");
        PathHintTextBlock.Text = T("Localization.LinkModeSharesOneSetOf");
        LinkDeploymentToggleSwitch.Header = T("Localization.DeployModsWithDirectoryJunctions");
        LinkDeploymentToggleSwitch.OnContent = T("Localization.Link");
        LinkDeploymentToggleSwitch.OffContent = T("Localization.Copy");
        RefreshButton.Content = T("Localization.Refresh");
        ImportZipButton.Content = T("Localization.ImportMod");
        ToolTipService.SetToolTip(ImportZipButton, T("Localization.ImportIntoTheSelectedFolder"));
        RunLauncherButton.Content = T("Localization.RunLauncher");
        ToggleCopyButton.Content = T("Localization.DeployMod");

        FirstCountLabelTextBlock.Text = T("Localization.FirstLevelFolders");
        FirstCountHintTextBlock.Text = T("Localization.NumberOfCategoriesInTheSource");
        SecondCountLabelTextBlock.Text = T("Localization.SecondLevelFolders");
        SecondCountHintTextBlock.Text = T("Localization.TotalModFoldersFoundInThe");
        CurrentStateLabelTextBlock.Text = T("Localization.DeploymentStatus");
        CurrentStateHintTextBlock.Text = T("Localization.DistinguishesNormalCopiesFromDirectoryJunctions");
        CurrentFolderLabelTextBlock.Text = T("Localization.CurrentSecondLevelFolder");
        CurrentFolderHintTextBlock.Text = T("Localization.ShowsTheCurrentlySelectedModName");

        FirstLevelSectionTitleTextBlock.Text = T("Localization.Categories");
        FirstLevelSectionSubtitleTextBlock.Text = T("Localization.ChooseACategoryFolderFirst");
        FirstLevelSearchTextBox.PlaceholderText = T("Localization.SearchFirstLevelFolders");
        CreateFirstLevelButton.Content = T("Localization.NewFolder");
        RenameFirstLevelButton.Content = T("Localization.Rename");
        SecondLevelSectionTitleTextBlock.Text = T("Localization.ModLibrary");
        SecondLevelSectionSubtitleTextBlock.Text = T("Localization.ThenChooseASpecificMod");
        DeleteSecondLevelButton.Content = T("Localization.DeleteMod");

        ShortcutSectionTitleTextBlock.Text = T("Localization.ShortcutNotes");
        ShortcutSectionSubtitleTextBlock.Text = T("Localization.ShortcutNotesForTheSelectedMod");
        AddShortcutRowButton.Content = T("Localization.AddRow");
        ShortcutHintTextBlock.Text = T("Localization.ReadsKeySectionsFromModINIs");

        PreviewSectionTitleTextBlock.Text = T("Localization.ImagePreview");
        PreviewSectionSubtitleTextBlock.Text = T("Localization.PrefersPreviewCoverThumbnailImageAnd");
        ModLinkSectionTitleTextBlock.Text = T("Localization.ModLink");
        ModLinkSectionSubtitleTextBlock.Text = T("Localization.BindAWebLinkToThe");
        ModLinkTextBox.PlaceholderText = T("Localization.PasteTheWebLinkForThe");
        PreviewFooterTextBlock.Text = T("Localization.ThePreviewShowsTheCurrentSecond");
        OpenModLinkButton.Content = T("Localization.OpenLink");

        ProgressTextBlock.Text = string.IsNullOrWhiteSpace(ProgressTextBlock.Text)
            ? T("Common.NoActiveCopyTask")
            : ProgressTextBlock.Text;
        AuthorTextBlock.Text = T("Localization.ToolAuthorUyujkk");

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
        PathSectionSubtitleTextBlock.Text = T("Localization.KeepTheModStorageFolderTarget");
        SourceLabelTextBlock.Text = T("Localization.ModStorage");
        TargetLabelTextBlock.Text = T("Localization.TargetFolder");
        OpenSourceButton.Content = T("Localization.OpenModStorage");
        OpenTargetButton.Content = T("Localization.OpenTargetFolder");
        PathHintTextBlock.Text = T("Localization.LinkModeSharesRepositoryFilesLinking");
        ImportZipButton.Content = T("Localization.ImportMod");

        FirstCountHintTextBlock.Text = T("Localization.NumberOfCategoriesInTheMod");

        ShortcutSectionTitleTextBlock.Text = T("Localization.ShortcutAndDescription");
        ShortcutSectionSubtitleTextBlock.Text = T("Localization.RecordAShortcutAndDescriptionFor");
        ShortcutHintTextBlock.Text = T("Localization.ClickABoxToRecordGame");
        RefreshPersistentSlotControls();
    }

    private void ApplyActionButtonPresentation()
    {
        CreateFirstLevelButton.Content = new FontIcon { Glyph = "\uE710", FontSize = 16 };
        RenameFirstLevelButton.Content = new FontIcon { Glyph = "\uE70F", FontSize = 16 };
        DeleteSecondLevelButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 };

        string createTip = T("Common.CreateFirstLevelFolder");
        string renameTip = T("Localization.RenameFirstLevelFolder");
        string deleteTip = T("Localization.DeleteSelectedMod");

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
            box.PlaceholderText = T("Common.Shortcut");
        }

        foreach (TextBox box in _shortcutActionBoxes)
        {
            box.PlaceholderText = T("Common.Description");
        }
    }

    private void SetDefaultStatus()
    {
        StatusTextBlock.Text = T("Localization.ChooseAModStorageFolderAnd", AppVersion);
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

    private string StateNotSelectedText => T("Common.NotSelected");

    private string StateNotConfiguredText => T("Localization.NotConfigured");

    private string StateCopiedText => T("Localization.Copied");

    private string StateLinkedText => T("Localization.Linked");

    private string StateMissingText => T("Localization.NotDeployed");
}
