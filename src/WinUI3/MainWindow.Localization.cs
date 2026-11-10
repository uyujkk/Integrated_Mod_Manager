using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private bool IsChineseUi => _services.Language.IsChinese;

    // Text lookups forward to the language service, so pages can use it without the window.
    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private string T(UiMessage message) => _services.Language.T(message);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    // For text pairs built at runtime, such as stored status messages.
    private string L(string zh, string en)
    {
        return BetaLocalization.Resolve(!IsChineseUi, zh, en);
    }

    private string NormalizeUiText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return !IsChineseUi
            ? value
            : BetaLocalization.NormalizeChineseText(value);
    }

    private void ApplyLanguage()
    {
        Title = T("Common.IntegratedModManager", AppVersion);

        ApplyPrimaryNavigationContent();

        ToolTipService.SetToolTip(_onlineView.RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        ToolTipService.SetToolTip(_onlineView.OnlinePrevPageButton, null);
        ToolTipService.SetToolTip(_onlineView.OnlineNextPageButton, null);
        ToolTipService.SetToolTip(_onlineView.OnlineSearchTextBox, T("Localization.SearchThisPageByCharacterMod2"));
        ToolTipService.SetToolTip(_onlineView.OnlineSortComboBox, T("Localization.ChangeTheSortOrderForThe"));
        ToolTipService.SetToolTip(PreviewImage, T("Localization.ClickToViewFullSize"));
        ToolTipService.SetToolTip(_onlineView.OnlineDetailHeroImage, T("Localization.ClickToViewFullSize"));
        AutomationProperties.SetName(_onlineView.RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        AutomationProperties.SetName(_onlineView.OnlinePrevPageButton, T("Localization.PreviousPage"));
        AutomationProperties.SetName(_onlineView.OnlineNextPageButton, T("Localization.NextPage"));
        AutomationProperties.SetName(_onlineView.OnlineSearchTextBox, T("Localization.SearchThisPageByCharacterMod"));
        AutomationProperties.SetName(_onlineView.OnlineSortComboBox, T("Localization.OnlineListSort"));
        AutomationProperties.SetName(PreviewImage, T("Localization.OpenFullSizeModPreview"));
        AutomationProperties.SetName(_onlineView.OnlineDetailHeroImage, T("Localization.OpenFullSizeOnlineModImage"));
        _onlineView.InitializeOnlineControls();

        // The dashboard labels itself (Pages/DashboardPage.xaml.cs).
        RefreshDashboard();

        _onlineView.OnlineTitleTextBlock.Text = T("Common.OnlineModBrowser");
        _onlineView.OnlineSubtitleTextBlock.Text = T("Localization.ThisPageKeepsTheOnlineMod");
        _onlineView.OnlinePreviewTitleTextBlock.Text = T("Common.OnlineModList");
        _onlineView.OnlinePreviewHintTextBlock.Text = T("Localization.ThisPageLoadsDataFromAn");

        // The Settings page labels itself (Pages/SettingsPage.xaml.cs).
        RefreshAppearanceSettings();
        RefreshSettingsOnlineConfigSummary();

        AppTitleBar.Title = T("Localization.IntegratedModManager");
        AppTitleBar.Subtitle = AppVersion;


        RefreshButton.Content = T("Localization.Refresh");
        ImportZipButton.Content = T("Localization.ImportMod");
        ToolTipService.SetToolTip(ImportZipButton, T("Localization.ImportIntoTheSelectedFolder"));
        RunLauncherButton.Content = T("Localization.RunLauncher");
        UpdateDeployButtonLabel();

        FirstCountLabelTextBlock.Text = T("Localization.FirstLevelFolders");
        FirstCountHintTextBlock.Text = T("Localization.NumberOfCategoriesInTheSource");
        SecondCountLabelTextBlock.Text = T("Localization.SecondLevelFolders");
        SecondCountHintTextBlock.Text = T("Localization.TotalModFoldersFoundInThe");
        CurrentStateLabelTextBlock.Text = T("Localization.DeploymentStatus");
        CurrentStateHintTextBlock.Text = T("Localization.DistinguishesNormalCopiesFromDirectoryJunctions");
        CurrentFolderLabelTextBlock.Text = T("Localization.CurrentSecondLevelFolder");
        CurrentFolderHintTextBlock.Text = T("Localization.ShowsTheCurrentlySelectedModName");
        FirstLevelSearchTextBox.PlaceholderText = T("RepositoryWorkspace.SearchCharacters");
        CreateFirstLevelButton.Content = T("Localization.NewFolder");
        RenameFirstLevelButton.Content = T("Localization.Rename");
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

        if (_characters.Selected is { } firstItem)
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
            ShowModSummary(null);
            ClearPreview();
            SetDefaultStatus();
        }
    }

    private void ApplyTextOverrides()
    {
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
        string targetDir = Paths.Target.Trim();
        foreach (FirstLevelFolderItem first in _characters.Visible)
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
