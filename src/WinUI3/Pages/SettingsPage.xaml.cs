using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using IntegratedModManager.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Settings page (stage J2, docs/development/UI-Structure-Plan.md): one column of settings cards.
/// It labels itself in the current language and shows <see cref="SettingsViewModel"/>; what only the
/// window can do (checking for updates, editing the repository, diagnostics) goes out as a request.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    private readonly AppServices _services;
    private bool _isApplyingInterval;

    internal SettingsPage(AppServices services)
    {
        _services = services;
        ViewModel = services.Settings;
        InitializeComponent();

        services.Language.LanguageChanged += (_, _) => ApplyLanguage();
        ViewModel.PropertyChanged += OnViewModelChanged;
        // The language is read from the settings after the page is made, so label again when it is shown.
        Loaded += (_, _) => ApplyLanguage();
        // Two columns once the page itself is wide enough; the window size alone says little with the navigation pane.
        SettingsScrollViewer.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width > 0) UpdateLayoutState(args.NewSize.Width);
            if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 0.5) QueueBalanceColumns();
        };
        // A page made while hidden lays its groups out later; balance again once their heights are known.
        SettingsMainPanel.SizeChanged += (_, args) => { if (Math.Abs(args.NewSize.Height - args.PreviousSize.Height) > 0.5) QueueBalanceColumns(); };
        SettingsRailPanel.SizeChanged += (_, args) => { if (Math.Abs(args.NewSize.Height - args.PreviousSize.Height) > 0.5) QueueBalanceColumns(); };
        ApplyLanguage();
    }

    public SettingsViewModel ViewModel { get; }

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private void ApplyLanguage()
    {
        // New text changes the group heights.
        QueueBalanceColumns();
        bool selfUpdate = ViewModel.SelfUpdateEnabled;
        SettingsPageHeader.Title = T("Common.ApplicationSettings");
        SettingsPageHeader.Description = T("Settings.LanguageThemeRepositoryEntryPointsAnd");

        // Appearance and language.
        SettingsAppearanceTitleTextBlock.Text = T("Common.AppearanceAndLanguage");
        SetCard(LanguageSettingsCard, T("Settings.DisplayLanguage"), T("Settings.SwitchTheEntireAppBetweenChinese"));
        SetCard(ThemeSettingsCard, T("Settings.AppTheme"), T("Settings.SwitchBetweenLightAndDarkFluent"));
        SetCard(DensitySettingsCard, T("Settings.InterfaceDensity"), T("Settings.CompactModeShowsMoreContentOn"));
        SetCard(MotionSettingsCard, T("Settings.ReduceMotion"), T("Settings.DisablePageSlideInAndImage"));
        SetCard(TitleBarSearchSettingsCard, T("Settings.ShowTitleBarSearch"), T("Settings.ShowTitleBarSearchDescription"));
        SetCard(TraySettingsCard, T("Tray.MinimizeToSystemTray"), T("Tray.KeepTheAppRunningInThe"));
        foreach (ToggleSwitch toggle in new[] { ReduceMotionToggleSwitch, TitleBarSearchToggleSwitch, MinimizeToTrayToggleSwitch, StartupUpdateToggleSwitch })
        {
            toggle.OnContent = T("Common.On2");
            toggle.OffContent = T("Common.Off2");
        }
        ViewModel.Appearance.SetOptionLabels(
            [T("Settings.FollowSystem"), T("Settings.Light"), T("Settings.Dark")],
            [T("Settings.Comfortable"), T("Settings.Compact")]);
        AutomationProperties.SetName(LanguageComboBox, T("Settings.DisplayLanguage"));
        AutomationProperties.SetName(ThemeComboBox, T("Settings.AppTheme"));
        AutomationProperties.SetName(DensityComboBox, T("Settings.InterfaceDensity"));
        AutomationProperties.SetName(ReduceMotionToggleSwitch, T("Settings.ReduceMotion"));
        AutomationProperties.SetName(TitleBarSearchToggleSwitch, T("Settings.ShowTitleBarSearch"));
        AutomationProperties.SetName(MinimizeToTrayToggleSwitch, T("Tray.MinimizeToSystemTray"));

        // App updates.
        SettingsAppUpdatesTitleTextBlock.Text = T("RefinedWorkspace.AppUpdates");
        SettingsUpdateOptionsTitleTextBlock.Text = T("Settings.UpdateOptions");
        // Without self-update the status card says why the buttons do nothing here.
        AppUpdateStatusCard.Description = selfUpdate ? null! : T("Settings.AppSelfUpdatesAreDisabledIn");
        SetCard(StartupUpdateSettingsCard, T("ProductivityFeatures.CheckForAppUpdatesAtStartup"), selfUpdate
            ? T("ProductivityFeatures.AskBeforeDownloadingANewVersion")
            : T("ProductivityFeatures.ThisLocalTestBuildDoesNot"));
        AutomationProperties.SetName(StartupUpdateToggleSwitch, T("ProductivityFeatures.CheckForAppUpdatesAtStartup"));
        SetCard(UpdateCheckIntervalSettingsCard, T("Settings.AppUpdateInterval"), selfUpdate
            ? T("Settings.ThisOnlyAffectsAppVersionChecks")
            : T("Settings.AppUpdateChecksAndAutomaticInstallation"));
        UpdateCheckIntervalComboBox.PlaceholderText = T("Settings.SelectUpdateCheckInterval");
        AutomationProperties.SetName(UpdateCheckIntervalComboBox, T("Settings.AppUpdateInterval"));
        PopulateUpdateIntervalOptions();
        OpenGitHubButton.Header = T("Common.OpenGitHubRepository");
        AutomationProperties.SetName(OpenGitHubButton, T("Localization.OpenGitHubRepository"));
        ToolTipService.SetToolTip(CheckUpdatesButton, T("Localization.CheckForAppUpdates"));
        AutomationProperties.SetName(CheckUpdatesButton, T("Localization.CheckForAppUpdates"));
        RefreshUpdateButtonLabels();

        // Repository online information.
        SettingsPlaceholderTitleTextBlock.Text = T("Settings.RepositoryOnlineCategory");
        SettingsOnlineExpander.Header = T("Settings.RepositoryOnlineInformation");
        SetCard(EditOnlineRepositoryButton, T("Localization.EditCurrentRepositoryOnlineCategory"), null);
        SetCard(EditCharacterMappingsButton, T("Common.CharacterCategoryMapping"), null);

        // Diagnostics and feedback.
        DiagnosticsTitleTextBlock.Text = T("ProductivityFeatures.DiagnosticsAndIssueReport");
        SetCard(ExportDiagnosticsButton, T("ProductivityFeatures.ExportDiagnosticReport"), T("ProductivityFeatures.GenerateASanitizedReportWithoutPrivate"));
        SetCard(SubmitDiagnosticsButton, T("ProductivityFeatures.ReportOnGitHub"), null);
        FeedbackGroupSettingsCard.Header = T("Settings.FeedbackGroup");
        FeedbackGroupNumberTextBlock.Text = T("Settings.FeedbackGroupNumber", SettingsViewModel.FeedbackQqGroup);
        CopyFeedbackGroupButton.Content = T("Settings.CopyGroupNumber");
        AutomationProperties.SetName(CopyFeedbackGroupButton, T("Settings.CopyGroupNumber") + " " + SettingsViewModel.FeedbackQqGroup);
        SetCard(FeedbackDiscordSettingsCard, T("Settings.FeedbackDiscord"), T("Settings.FeedbackDiscordHint"));
        OpenFeedbackDiscordButton.Content = T("Settings.OpenDiscord");
        AutomationProperties.SetName(OpenFeedbackDiscordButton, T("Settings.OpenDiscord"));

        // About.
        ProjectResourcesTitleTextBlock.Text = T("Settings.ProjectAndOpenSourceComponents");
        AboutExpander.Header = T("Localization.IntegratedModManager");
        AboutExpander.Description = T("Updates.CurrentVersion", ViewModel.AppVersion);
        ToolTipService.SetToolTip(AboutExpander, T("Settings.MajorOpenSourceProjectsUsedOr"));
    }

    // The header is also the card's Narrator name, so a card and its control are announced the same way.
    private static void SetCard(SettingsCard card, string header, string? description)
    {
        card.Header = header;
        // No description (null) hides the line under the header.
        card.Description = description!;
        AutomationProperties.SetName(card, header);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.InstallAction):
            case nameof(SettingsViewModel.IsCheckingUpdates):
            case nameof(SettingsViewModel.SelfUpdateEnabled):
                RefreshUpdateButtonLabels();
                break;
            case nameof(SettingsViewModel.UpdateIntervalIndex):
                SelectUpdateInterval();
                break;
            case nameof(SettingsViewModel.AppVersion):
                AboutExpander.Description = T("Updates.CurrentVersion", ViewModel.AppVersion);
                break;
        }
    }

    private void RefreshUpdateButtonLabels()
    {
        CheckUpdatesButton.Content = !ViewModel.SelfUpdateEnabled ? T("Settings.BetaAppUpdatesDisabled")
            : ViewModel.IsCheckingUpdates ? T("Common.Checking")
            : T("Common.CheckAppUpdates");
        InstallUpdateButton.Content = ViewModel.InstallAction switch
        {
            AppUpdateInstallAction.ViewPublicReleases => T("Settings.ViewPublicReleases"),
            AppUpdateInstallAction.InstallLocalPackage => T("Settings.InstallLocalUpdateV", ViewModel.LocalPackageVersion),
            AppUpdateInstallAction.CheckAndGet => T("Settings.CheckAndGetUpdate"),
            AppUpdateInstallAction.DownloadAndInstall => T("Settings.DownloadAndInstallUpdate"),
            _ => T("Settings.OpenReleasePage")
        };
    }

    private void PopulateUpdateIntervalOptions()
    {
        string[] labels =
        [
            T("ModUpdates.ManualOnly"),
            T("ModUpdates.CheckDailyOnStartup"),
            T("ModUpdates.CheckEvery3DaysOnStartup"),
            T("ModUpdates.CheckWeeklyOnStartup")
        ];
        // Relabel in place: replacing the items of an open list in its SelectionChanged would break it.
        if (UpdateCheckIntervalComboBox.Items.Count == 0)
        {
            foreach (string label in labels) UpdateCheckIntervalComboBox.Items.Add(new ComboBoxItem { Content = label });
        }
        bool relabeled = false;
        for (int index = 0; index < labels.Length; index++)
        {
            var item = (ComboBoxItem)UpdateCheckIntervalComboBox.Items[index];
            if (Equals(item.Content, labels[index])) continue;
            item.Content = labels[index];
            relabeled = true;
        }
        // A closed combo box keeps showing the old text of a relabeled selection until it is selected again.
        if (relabeled && !UpdateCheckIntervalComboBox.IsDropDownOpen)
        {
            _isApplyingInterval = true;
            UpdateCheckIntervalComboBox.SelectedItem = null;
            _isApplyingInterval = false;
        }
        SelectUpdateInterval();
    }

    private void SelectUpdateInterval()
    {
        if (UpdateCheckIntervalComboBox.SelectedIndex == ViewModel.UpdateIntervalIndex) return;
        _isApplyingInterval = true;
        UpdateCheckIntervalComboBox.SelectedIndex = ViewModel.UpdateIntervalIndex;
        _isApplyingInterval = false;
    }

    private void OnUpdateCheckIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingInterval || UpdateCheckIntervalComboBox.SelectedIndex < 0) return;
        ViewModel.UpdateIntervalIndex = UpdateCheckIntervalComboBox.SelectedIndex;
    }

    // Clicks the window answers.
    private void OnOpenGitHubClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.OpenGitHub);

    private void OnCheckUpdatesClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.CheckUpdates);

    private void OnInstallUpdateClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.InstallUpdate);

    private void OnEditOnlineRepositoryClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.EditOnlineRepository);

    private void OnEditCharacterMappingsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.EditCharacterMappings);

    private void OnExportDiagnosticsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.ExportDiagnostics);

    private void OnSubmitDiagnosticsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.SubmitDiagnostics);

    private void OnCopyFeedbackGroupClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.CopyFeedbackGroup);

    private void OnOpenFeedbackDiscordClicked(object sender, RoutedEventArgs e) => ViewModel.Request(SettingsAction.OpenDiscord);

    private void OnExternalResourceClicked(object sender, RoutedEventArgs e)
        => ViewModel.Request(SettingsAction.OpenLink, (sender as FrameworkElement)?.Tag as string);

    /// <summary>
    /// The page width from which the settings and the information rail sit side by side: the rail's 360 DIP,
    /// the 24 DIP between them, and a main column of about 800 DIP, which keeps Russian titles and their
    /// controls on one row. 1400×900 windows (about 1300 DIP of page) get two columns, 1000×800 windows one.
    /// </summary>
    public const double TwoColumnWidth = 1180;

    private bool? _twoColumns;

    private void UpdateLayoutState(double pageWidth)
    {
        bool twoColumns = pageWidth >= TwoColumnWidth;
        if (_twoColumns == twoColumns) return;
        _twoColumns = twoColumns;
        VisualStateManager.GoToState(this, twoColumns ? "WideLayout" : "NarrowLayout", false);
        // The rail is too narrow for the status and both update buttons on one row (Russian), so there the
        // buttons go below the status, one per row. Set here: a VisualState setter cannot convert ContentAlignment.
        AppUpdateStatusCard.ContentAlignment = twoColumns ? ContentAlignment.Vertical : ContentAlignment.Right;
        AppUpdateStatusCard.HorizontalContentAlignment = twoColumns ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        AppUpdateButtonsPanel.Orientation = twoColumns ? Orientation.Vertical : Orientation.Horizontal;
        QueueBalanceColumns();
    }

    private double _balancedWidth = -1;
    private double _balancedContent;
    private int _balanceRounds;

    // After layout, so the heights are current.
    private void QueueBalanceColumns()
        => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, BalanceColumns);

    // One column keeps every group in the side panel in its own order. Two columns move the side column's later
    // groups (online, diagnostics, project) that best even out the column bottoms to the end of the main column, in
    // their own order; Tab follows the panels, so it still goes down the main column and then down the side column.
    private void BalanceColumns()
    {
        StackPanel[] movable = [SettingsOnlineGroup, SettingsDiagnosticsGroup, SettingsProjectGroup];
        bool[] move = new bool[movable.Length];
        if (_twoColumns == true)
        {
            // Heights are only known once the groups have been laid out; until then the panels' SizeChanged brings
            // the balance back here. The same width and content heights give the same answer, so a move that only
            // changes a group's wrapping does not start another round (at most a few rounds per width).
            if (movable.Any(group => group.ActualHeight <= 0) || SettingsMainPanel.DesiredSize.Height <= 0) return;
            double content = SettingsMainPanel.DesiredSize.Height + SettingsRailPanel.DesiredSize.Height;
            double width = Math.Round(SettingsScrollViewer.ActualWidth);
            if (width != _balancedWidth) { _balancedWidth = width; _balanceRounds = 0; }
            else if (Math.Abs(content - _balancedContent) < 40 || _balanceRounds >= 3) return;
            _balancedContent = content;
            _balanceRounds++;
            double movedHeight = movable.Where(group => SettingsMainPanel.Children.Contains(group)).Sum(group => group.ActualHeight + SettingsMainPanel.Spacing);
            move = SettingsLayoutPolicy.BalanceColumns(
                // Its content height: the panel itself stretches to the row, which the side column may set.
                SettingsMainPanel.DesiredSize.Height - movedHeight,
                SettingsAppUpdatesGroup.ActualHeight,
                movable.Select(group => group.ActualHeight).ToArray(),
                SettingsRailPanel.Spacing);
        }

        for (int i = 0; i < movable.Length; i++)
        {
            StackPanel target = move[i] ? SettingsMainPanel : SettingsRailPanel;
            if (target.Children.Contains(movable[i])) continue;
            ((Panel)movable[i].Parent).Children.Remove(movable[i]);
            // Groups keep their order within each column.
            int index = target.Children.Count;
            for (int j = i + 1; j < movable.Length; j++)
            {
                int at = target.Children.IndexOf(movable[j]);
                if (at >= 0) { index = at; break; }
            }
            target.Children.Insert(index, movable[i]);
        }
    }
}
