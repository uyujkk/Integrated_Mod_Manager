using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// The page's own labels (stage J finish, step 8c: until then the window set them), its shortcut row boxes and showing
// what the command palette picked.
public sealed partial class WorkspacePage
{
    /// <summary>The key boxes of the shortcut rows.</summary>
    internal List<TextBox> ShortcutKeyBoxes { get; } = [];

    /// <summary>The description boxes of the shortcut rows.</summary>
    internal List<TextBox> ShortcutActionBoxes { get; } = [];

    // The page's labels, the selected character or Mod in the new language, and its status line: when the language
    // changes and when the page is made (moved from the window, stage J finish, step 8c).
    internal void ApplyLanguage()
    {
        ToolTipService.SetToolTip(PreviewImage, T("Localization.ClickToViewFullSize"));
        AutomationProperties.SetName(PreviewImage, T("Localization.OpenFullSizeModPreview"));
        RefreshButton.Label = T("Localization.Refresh");
        ImportZipButton.Label = T("Localization.ImportMod");
        ToolTipService.SetToolTip(ImportZipButton, T("Localization.ImportIntoTheSelectedFolder"));
        RunLauncherButton.Label = T("Localization.RunLauncher");
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
        RefreshCombinationProfileText();

        ProgressTextBlock.Text = string.IsNullOrWhiteSpace(ProgressTextBlock.Text)
            ? T("Common.NoActiveCopyTask")
            : ProgressTextBlock.Text;
        AuthorTextBlock.Text = T("Localization.ToolAuthorUyujkk");

        ApplyShortcutPlaceholders();
        UpdateShortcutRowVisibility();
        UpdateLinkButtonState();
        _services.Workspace.RefreshCopyStates();
        ApplyTextOverrides();
        ApplyActionButtonPresentation();

        if (_services.Workspace.Characters.Selected is { } firstItem)
        {
            string? selectedPath = _services.Workspace.CurrentModPath;
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
            _services.Workspace.SetDefaultStatus();
        }
    }
    // The preset controls of the Presets view.
    private void RefreshCombinationProfileText()
    {
        ConfigurationProfilesTitleTextBlock.Text = T("Updates.ModCombinationPresets");
        ConfigurationProfilesHintTextBlock.Text = T("Updates.SaveEnabledModsAndPersistentState");
        CaptureCombinationStateCheckBox.Content = T("Updates.IncludeD3dxUserIniParametersWhen");
        BrowseCombinationUserIniButton.Content = T("Common.Browse");
        CreateConfigurationProfileButton.Content = T("Updates.NewProfile");
        UpdateConfigurationProfileButton.Content = T("Updates.UpdateProfile");
        ApplyConfigurationProfileButton.Content = T("Updates.PreviewAndRestore");
        DeleteConfigurationProfileButton.Content = new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "", FontSize = 16 };
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(DeleteConfigurationProfileButton, T("Updates.DeleteTheSelectedProfile"));
    }
    private void ApplyTextOverrides()
    {
        ImportZipButton.Label = T("Localization.ImportMod");

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
    internal void ApplyShortcutPlaceholders()
    {
        foreach (TextBox box in ShortcutKeyBoxes)
        {
            box.PlaceholderText = T("Common.Shortcut");
        }

        foreach (TextBox box in ShortcutActionBoxes)
        {
            box.PlaceholderText = T("Common.Description");
        }
    }

    /// <summary>The command palette's pick: clears the character and Mod searches, so the row is not hidden by a filter, and selects it.</summary>
    internal void ShowItemFromPalette(string? characterPath, string? modPath)
    {
        if (_repositoryWorkspaceBusy)
        {
            _services.Workspace.Status = T("CommandPalette.Busy");
            return;
        }
        // TextChanged is raised later, so apply each cleared filter right away as well.
        if (!string.IsNullOrEmpty(FirstLevelSearchTextBox.Text))
        {
            FirstLevelSearchTextBox.Text = string.Empty;
            ApplyFirstLevelFilter();
        }
        if (_repositoryModSearch is not null && !string.IsNullOrEmpty(_repositoryModSearch.Text))
        {
            _repositoryModSearch.Text = string.Empty;
            RefreshRepositoryModView();
        }
        if (_services.Workspace.ModList.View == RepositoryWorkspaceView.Presets) SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView.List);
        if (modPath is not null) SelectSecondLevelByPath(modPath);
        else SelectFirstLevelByPath(characterPath);
    }
}
