using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// What the window used to do to the Online page from outside (stage J finish, step 5b): the page is now made the first
// time it is shown, so it labels itself, sets up its lists and browse sessions when it is made, and the window only
// calls these while it exists.
public sealed partial class OnlinePage
{
    /// <summary>Right after the page is made: what the window did to it at startup, in the same order.</summary>
    internal void InitializeAfterCreation()
    {
        OnlineVirtualizedListView.ItemsSource = _services.Online.Page.VisibleMods;
        OnlineVirtualizedGridView.ItemsSource = _services.Online.Page.VisibleMods;
        ApplyInitialLabels();
        ApplyWindowLanguage();
        InitializeOnlineControls();
        InitializeBrowseSessionTracking();
        UpdateOnlineWorkspaceWidthConstraint();
        DispatcherQueue.TryEnqueue(UpdateOnlineWorkspaceWidthConstraint);
    }

    // Set once when the window was built (from the workspace's shortcut rows); the language labels below replace some.
    private void ApplyInitialLabels()
    {
        OnlineTitleTextBlock.Text = T("Common.OnlineModBrowser");
        OnlineSubtitleTextBlock.Text = T("ShortcutBindings.ThisPageNowFocusesOnThe");
        OnlinePreviewTitleTextBlock.Text = T("Common.OnlineModList");
        OnlinePreviewHintTextBlock.Text = T("ShortcutBindings.ThisPageLoadsDataFromAn");
        RefreshOnlineNavigationButtonLabels();
        OpenOnlineDetailPageButton.Content = T("Common.OpenOriginalPage");
        SelectOnlineDetailDownloadButton.Content = T("Common.SelectFile");
        DownloadOnlineDetailButton.Content = T("Common.DownloadLatestArchive");
        if (_activeOnlineDetailMod is null)
        {
            ResetOnlineDetailPaneToPlaceholder();
        }
        else
        {
            UpdateOnlineDetailDownloadControls(_activeOnlineDetailMod);
        }
        OnlineDetailRequirementTitleTextBlock.Text = T("Common.PossibleExtraAccessRequirements");
    }

    /// <summary>The page's labels in the current language (the window calls this when the language changes).</summary>
    internal void ApplyWindowLanguage()
    {
        ToolTipService.SetToolTip(RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        ToolTipService.SetToolTip(OnlinePrevPageButton, T("Localization.PreviousPage"));
        ToolTipService.SetToolTip(OnlineNextPageButton, T("Localization.NextPage"));
        ToolTipService.SetToolTip(OnlineSearchTextBox, T("Localization.SearchThisPageByCharacterMod2"));
        ToolTipService.SetToolTip(OnlineSortComboBox, T("Localization.ChangeTheSortOrderForThe"));
        ToolTipService.SetToolTip(OnlineDetailHeroImage, T("Localization.ClickToViewFullSize"));
        AutomationProperties.SetName(RefreshOnlineButton, T("Localization.RefreshOnlineModList"));
        AutomationProperties.SetName(OnlinePrevPageButton, T("Localization.PreviousPage"));
        AutomationProperties.SetName(OnlineNextPageButton, T("Localization.NextPage"));
        AutomationProperties.SetName(OnlineSearchTextBox, T("Localization.SearchThisPageByCharacterMod"));
        AutomationProperties.SetName(OnlineSortComboBox, T("Localization.OnlineListSort"));
        AutomationProperties.SetName(OnlineDetailHeroImage, T("Localization.OpenFullSizeOnlineModImage"));
        InitializeOnlineControls();
        OnlineTitleTextBlock.Text = T("Common.OnlineModBrowser");
        OnlineSubtitleTextBlock.Text = T("Localization.ThisPageKeepsTheOnlineMod");
        OnlinePreviewTitleTextBlock.Text = T("Common.OnlineModList");
        OnlinePreviewHintTextBlock.Text = T("Localization.ThisPageLoadsDataFromAn");
    }

    /// <summary>The repository changed: forget the browse session, requests and details of the old one.</summary>
    internal void ResetRepositoryContext()
    {
        _activeBrowseSessionKey = null;
        _pendingBrowsePosition = null;
        ++_browseRestoreRevision;
        CancelOnlineModListRequest();
        InvalidateOnlineDetailRequest();
        _onlineDownloadEnrichmentVersion++;
    }

    /// <summary>No Mod's details are open any more (the repository changed).</summary>
    internal void ClearOnlineDetails()
    {
        _activeOnlineDetailMod = null;
        ResetOnlineDetailPaneToPlaceholder();
    }

    /// <summary>The list is loaded again the next time it is shown.</summary>
    internal void ForgetLoadedList() => _lastLoadedOnlineConfigKey = null;

    /// <summary>The window is closing: the browse position is saved first, before the settings.</summary>
    internal void BeginClosing()
    {
        CaptureOnlineBrowseSession();
        _browseSaveTimer?.Stop();
        ++_browseRestoreRevision;
    }

    /// <summary>The window is closing: running requests and animations stop.</summary>
    internal void FinishClosing()
    {
        ++_onlineDetailAnimationVersion;
        InvalidateOnlineDetailRequest();
        CancelOnlineModListRequest();
        _onlineCharacterAvatarCancellation.Cancel();
        _onlineCharacterAvatarCancellation.Dispose();
    }

    /// <summary>The theme changed: open details are drawn again in it.</summary>
    internal void RedrawOpenDetails()
    {
        if (IsOnlineDetailOpen && _activeOnlineDetailMod is OnlineModCard mod)
            _ = ShowOnlineModDetailsAsync(mod);
    }

    /// <summary>A character's favourite changed (Settings or the workspace): the list box and strip follow it.</summary>
    internal void RefreshFavoriteCharacters()
    {
        _onlineCharacterStripSignature = null;
        RefreshOnlineCharacterAvatarStrip();
        PopulateOnlineCharacterOptions();
    }
}
