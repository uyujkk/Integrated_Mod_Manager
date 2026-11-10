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
        ToolTipService.SetToolTip(_workspaceView.PreviewImage, T("Localization.ClickToViewFullSize"));
        ToolTipService.SetToolTip(_onlineView.OnlineDetailHeroImage, T("Localization.ClickToViewFullSize"));
        AutomationProperties.SetName(_onlineView.RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        AutomationProperties.SetName(_onlineView.OnlinePrevPageButton, T("Localization.PreviousPage"));
        AutomationProperties.SetName(_onlineView.OnlineNextPageButton, T("Localization.NextPage"));
        AutomationProperties.SetName(_onlineView.OnlineSearchTextBox, T("Localization.SearchThisPageByCharacterMod"));
        AutomationProperties.SetName(_onlineView.OnlineSortComboBox, T("Localization.OnlineListSort"));
        AutomationProperties.SetName(_workspaceView.PreviewImage, T("Localization.OpenFullSizeModPreview"));
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


        _workspaceView.RefreshButton.Content = T("Localization.Refresh");
        _workspaceView.ImportZipButton.Content = T("Localization.ImportMod");
        ToolTipService.SetToolTip(_workspaceView.ImportZipButton, T("Localization.ImportIntoTheSelectedFolder"));
        _workspaceView.RunLauncherButton.Content = T("Localization.RunLauncher");
        _workspaceView.UpdateDeployButtonLabel();

        _workspaceView.FirstCountLabelTextBlock.Text = T("Localization.FirstLevelFolders");
        _workspaceView.FirstCountHintTextBlock.Text = T("Localization.NumberOfCategoriesInTheSource");
        _workspaceView.SecondCountLabelTextBlock.Text = T("Localization.SecondLevelFolders");
        _workspaceView.SecondCountHintTextBlock.Text = T("Localization.TotalModFoldersFoundInThe");
        _workspaceView.CurrentStateLabelTextBlock.Text = T("Localization.DeploymentStatus");
        _workspaceView.CurrentStateHintTextBlock.Text = T("Localization.DistinguishesNormalCopiesFromDirectoryJunctions");
        _workspaceView.CurrentFolderLabelTextBlock.Text = T("Localization.CurrentSecondLevelFolder");
        _workspaceView.CurrentFolderHintTextBlock.Text = T("Localization.ShowsTheCurrentlySelectedModName");
        _workspaceView.FirstLevelSearchTextBox.PlaceholderText = T("RepositoryWorkspace.SearchCharacters");
        _workspaceView.CreateFirstLevelButton.Content = T("Localization.NewFolder");
        _workspaceView.RenameFirstLevelButton.Content = T("Localization.Rename");
        _workspaceView.DeleteSecondLevelButton.Content = T("Localization.DeleteMod");

        _workspaceView.ShortcutSectionTitleTextBlock.Text = T("Localization.ShortcutNotes");
        _workspaceView.ShortcutSectionSubtitleTextBlock.Text = T("Localization.ShortcutNotesForTheSelectedMod");
        _workspaceView.AddShortcutRowButton.Content = T("Localization.AddRow");
        _workspaceView.ShortcutHintTextBlock.Text = T("Localization.ReadsKeySectionsFromModINIs");

        _workspaceView.PreviewSectionTitleTextBlock.Text = T("Localization.ImagePreview");
        _workspaceView.PreviewSectionSubtitleTextBlock.Text = T("Localization.PrefersPreviewCoverThumbnailImageAnd");
        _workspaceView.ModLinkSectionTitleTextBlock.Text = T("Localization.ModLink");
        _workspaceView.ModLinkSectionSubtitleTextBlock.Text = T("Localization.BindAWebLinkToThe");
        _workspaceView.ModLinkTextBox.PlaceholderText = T("Localization.PasteTheWebLinkForThe");
        _workspaceView.PreviewFooterTextBlock.Text = T("Localization.ThePreviewShowsTheCurrentSecond");
        _workspaceView.OpenModLinkButton.Content = T("Localization.OpenLink");

        _workspaceView.ProgressTextBlock.Text = string.IsNullOrWhiteSpace(_workspaceView.ProgressTextBlock.Text)
            ? T("Common.NoActiveCopyTask")
            : _workspaceView.ProgressTextBlock.Text;
        _workspaceView.AuthorTextBlock.Text = T("Localization.ToolAuthorUyujkk");

        ApplyShortcutPlaceholders();
        _workspaceView.UpdateShortcutRowVisibility();
        _workspaceView.UpdateLinkButtonState();
        RefreshLocalizedStates();
        ApplyTextOverrides();
        ApplyActionButtonPresentation();

        if (_characters.Selected is { } firstItem)
        {
            string? selectedPath = _currentSecondLevelPath;
            _workspaceView.PopulateSecondLevelList(firstItem, preserveStatus: true);
            _workspaceView.SelectSecondLevelByPath(selectedPath);
        }
        else if (_workspaceView.GetSelectedSecondLevelItem() is SecondLevelFolderItem secondItem)
        {
            _workspaceView.ShowSecondLevelDetails(secondItem, preserveStatus: true);
        }
        else
        {
            _workspaceView.ShowModSummary(null);
            _workspaceView.ClearPreview();
            SetDefaultStatus();
        }
    }

    private void ApplyTextOverrides()
    {
        _workspaceView.ImportZipButton.Content = T("Localization.ImportMod");

        _workspaceView.FirstCountHintTextBlock.Text = T("Localization.NumberOfCategoriesInTheMod");

        _workspaceView.ShortcutSectionTitleTextBlock.Text = T("Localization.ShortcutAndDescription");
        _workspaceView.ShortcutSectionSubtitleTextBlock.Text = T("Localization.RecordAShortcutAndDescriptionFor");
        _workspaceView.ShortcutHintTextBlock.Text = T("Localization.ClickABoxToRecordGame");
        RefreshPersistentSlotControls();
    }

    private void ApplyActionButtonPresentation()
    {
        _workspaceView.CreateFirstLevelButton.Content = new FontIcon { Glyph = "\uE710", FontSize = 16 };
        _workspaceView.RenameFirstLevelButton.Content = new FontIcon { Glyph = "\uE70F", FontSize = 16 };
        _workspaceView.DeleteSecondLevelButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 16 };

        string createTip = T("Common.CreateFirstLevelFolder");
        string renameTip = T("Localization.RenameFirstLevelFolder");
        string deleteTip = T("Localization.DeleteSelectedMod");

        ToolTipService.SetToolTip(_workspaceView.CreateFirstLevelButton, createTip);
        ToolTipService.SetToolTip(_workspaceView.RenameFirstLevelButton, renameTip);
        ToolTipService.SetToolTip(_workspaceView.DeleteSecondLevelButton, deleteTip);

        AutomationProperties.SetName(_workspaceView.CreateFirstLevelButton, createTip);
        AutomationProperties.SetName(_workspaceView.RenameFirstLevelButton, renameTip);
        AutomationProperties.SetName(_workspaceView.DeleteSecondLevelButton, deleteTip);
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
        _workspaceView.StatusTextBlock.Text = T("Localization.ChooseAModStorageFolderAnd", AppVersion);
    }

    private void RefreshLocalizedStates()
    {
        string targetDir = Paths.Target.Trim();
        foreach (FirstLevelFolderItem first in _characters.Visible)
        {
            foreach (SecondLevelFolderItem second in first.Children)
            {
                second.State = _workspaceView.GetFolderCopyState(targetDir, second.Path);
            }
        }
    }





}
