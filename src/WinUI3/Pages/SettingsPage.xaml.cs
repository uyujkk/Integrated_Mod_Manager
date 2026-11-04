using System.ComponentModel;
using IntegratedModManager.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Settings page (stage J2, docs/development/UI-Structure-Plan.md). It labels itself in the
/// current language, lays itself out and shows <see cref="SettingsViewModel"/>; what only the window
/// can do (checking for updates, editing the repository, diagnostics) goes out as a request.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    private readonly AppServices _services;
    private readonly Border[] _settingsCards;
    private readonly Button[] _settingsSectionButtons;
    private readonly TextBlock[] _settingsSectionLabels;
    private int _settingsLayoutMode = -1;
    private int _selectedSettingsSection;
    private bool _isApplyingInterval;

    internal SettingsPage(AppServices services)
    {
        _services = services;
        ViewModel = services.Settings;
        InitializeComponent();

        // The layout itself is in the XAML; these arrays pair each section button with its card.
        _settingsCards = [SettingsAppearanceCard, SettingsAppUpdatesCard, SettingsOnlineCard, SettingsDiagnosticsCard, ProjectResourcesCard];
        _settingsSectionButtons = [SettingsSectionButton0, SettingsSectionButton1, SettingsSectionButton2, SettingsSectionButton3, SettingsSectionButton4];
        _settingsSectionLabels = [SettingsSectionLabel0, SettingsSectionLabel1, SettingsSectionLabel2, SettingsSectionLabel3, SettingsSectionLabel4];
        SettingsNavigationCard.SizeChanged += (_, _) => UpdateSettingsLayout();
        AppUpdateActionsGrid.SizeChanged += (_, _) => UpdateAppUpdateActionsLayout();
        // The page lays itself out from its own size. A hidden page reports zero and lays out again when shown.
        SettingsScrollViewer.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width > 0) UpdateSettingsLayout();
        };
        ShowSettingsSection(0);

        services.Language.LanguageChanged += (_, _) => ApplyLanguage();
        services.Appearance.ThemeChanged += (_, _) => ShowSettingsSection(_selectedSettingsSection);
        ViewModel.PropertyChanged += OnViewModelChanged;
        // The language is read from the settings after the page is made, so label again when it is shown.
        Loaded += (_, _) => ApplyLanguage();
        ApplyLanguage();
    }

    public SettingsViewModel ViewModel { get; }

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private Brush ThemeBrush(string token) => ThemeTokens.Get(token, _services.Appearance.ThemeKey);

    private void ApplyLanguage()
    {
        bool selfUpdate = ViewModel.SelfUpdateEnabled;
        SettingsPageHeader.Title = T("Common.ApplicationSettings");
        SettingsPageHeader.Description = T("Settings.LanguageThemeRepositoryEntryPointsAnd");

        // Appearance and language.
        SettingsAppearanceTitleTextBlock.Text = T("Common.AppearanceAndLanguage");
        LanguageSettingRow.Title = T("Settings.DisplayLanguage");
        LanguageSettingRow.Description = T("Settings.SwitchTheEntireAppBetweenChinese");
        ThemeSettingRow.Title = T("Settings.AppTheme");
        ThemeSettingRow.Description = T("Settings.SwitchBetweenLightAndDarkFluent");
        MotionSettingRow.Title = T("Settings.ReduceMotion");
        MotionSettingRow.Description = T("Settings.DisablePageSlideInAndImage");
        DensitySettingRow.Title = T("Settings.InterfaceDensity");
        DensitySettingRow.Description = T("Settings.CompactModeShowsMoreContentOn");
        TraySettingRow.Title = T("Tray.MinimizeToSystemTray");
        TraySettingRow.Description = T("Tray.KeepTheAppRunningInThe");
        foreach (ToggleSwitch toggle in new[] { ReduceMotionToggleSwitch, MinimizeToTrayToggleSwitch, StartupUpdateToggleSwitch })
        {
            toggle.OnContent = T("Common.On2");
            toggle.OffContent = T("Common.Off2");
        }
        ViewModel.Appearance.SetOptionLabels(
            [T("Settings.FollowSystem"), T("Settings.Light"), T("Settings.Dark")],
            [T("Settings.Comfortable"), T("Settings.Compact")]);
        AutomationProperties.SetName(LanguageComboBox, LanguageSettingRow.Title);
        AutomationProperties.SetName(ThemeComboBox, ThemeSettingRow.Title);
        AutomationProperties.SetName(DensityComboBox, DensitySettingRow.Title);

        // App updates.
        SettingsProjectTitleTextBlock.Text = T("Common.ProjectLinksAndAppVersion");
        SettingsProjectHintTextBlock.Text = selfUpdate
            ? T("Common.ThisSectionKeepsTheGitHubRepository")
            : T("Settings.AppSelfUpdatesAreDisabledIn");
        StartupUpdateTitleTextBlock.Text = T("ProductivityFeatures.CheckForAppUpdatesAtStartup");
        StartupUpdateDescriptionTextBlock.Text = selfUpdate
            ? T("ProductivityFeatures.AskBeforeDownloadingANewVersion")
            : T("ProductivityFeatures.ThisLocalTestBuildDoesNot");
        UpdateCheckIntervalLabelTextBlock.Text = T("Settings.AppUpdateInterval");
        UpdateCheckIntervalComboBox.PlaceholderText = T("Settings.SelectUpdateCheckInterval");
        UpdateCheckIntervalHintTextBlock.Text = selfUpdate
            ? T("Settings.ThisOnlyAffectsAppVersionChecks")
            : T("Settings.AppUpdateChecksAndAutomaticInstallation");
        PopulateUpdateIntervalOptions();
        OpenGitHubButton.Content = T("Common.OpenGitHubRepository");
        ToolTipService.SetToolTip(OpenGitHubButton, T("Localization.OpenGitHubRepository"));
        AutomationProperties.SetName(OpenGitHubButton, T("Localization.OpenGitHubRepository"));
        ToolTipService.SetToolTip(CheckUpdatesButton, T("Localization.CheckForAppUpdates"));
        AutomationProperties.SetName(CheckUpdatesButton, T("Localization.CheckForAppUpdates"));
        RefreshUpdateButtonLabels();

        // Repository online information.
        SettingsPlaceholderTitleTextBlock.Text = T("Settings.RepositoryOnlineCategory");
        EditOnlineRepositoryButton.Content = T("Localization.EditCurrentRepositoryOnlineCategory");
        ToolTipService.SetToolTip(EditOnlineRepositoryButton, T("Localization.EditCurrentRepositoryOnlineCategory"));
        AutomationProperties.SetName(EditOnlineRepositoryButton, T("Localization.EditCurrentRepositoryOnlineCategory"));
        EditCharacterMappingsButton.Content = T("Common.CharacterCategoryMapping");

        // Diagnostics and feedback.
        DiagnosticsTitleTextBlock.Text = T("ProductivityFeatures.DiagnosticsAndIssueReport");
        DiagnosticsHintTextBlock.Text = T("ProductivityFeatures.GenerateASanitizedReportWithoutPrivate");
        ExportDiagnosticsButton.Content = T("ProductivityFeatures.ExportDiagnosticReport");
        SubmitDiagnosticsButton.Content = T("ProductivityFeatures.ReportOnGitHub");
        AutomationProperties.SetName(ExportDiagnosticsButton, ExportDiagnosticsButton.Content?.ToString() ?? string.Empty);
        AutomationProperties.SetName(SubmitDiagnosticsButton, SubmitDiagnosticsButton.Content?.ToString() ?? string.Empty);
        FeedbackGroupTitleTextBlock.Text = T("Settings.FeedbackGroup");
        FeedbackGroupNumberTextBlock.Text = T("Settings.FeedbackGroupNumber", SettingsViewModel.FeedbackQqGroup);
        CopyFeedbackGroupButton.Content = T("Settings.CopyGroupNumber");
        AutomationProperties.SetName(CopyFeedbackGroupButton, T("Settings.CopyGroupNumber") + " " + SettingsViewModel.FeedbackQqGroup);
        FeedbackDiscordTitleTextBlock.Text = T("Settings.FeedbackDiscord");
        FeedbackDiscordHintTextBlock.Text = T("Settings.FeedbackDiscordHint");
        OpenFeedbackDiscordButton.Content = T("Settings.OpenDiscord");
        AutomationProperties.SetName(OpenFeedbackDiscordButton, T("Settings.OpenDiscord"));

        RefreshProjectResourceText();

        _settingsSectionLabels[0].Text = T("RefinedWorkspace.AppearanceLanguage");
        _settingsSectionLabels[1].Text = T("RefinedWorkspace.AppUpdates");
        _settingsSectionLabels[2].Text = T("RefinedWorkspace.RepositoryOnline");
        _settingsSectionLabels[3].Text = T("RefinedWorkspace.Diagnostics");
        _settingsSectionLabels[4].Text = T("RefinedWorkspace.ProjectOpenSource");
        for (int index = 0; index < _settingsSectionButtons.Length; index++)
        {
            ToolTipService.SetToolTip(_settingsSectionButtons[index], _settingsSectionLabels[index].Text);
            AutomationProperties.SetName(_settingsSectionButtons[index], _settingsSectionLabels[index].Text);
        }
        ShowSettingsSection(_selectedSettingsSection);
        UpdateSettingsLayout();
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

    private void RefreshProjectResourceText()
    {
        ProjectResourcesTitleTextBlock.Text = T("Settings.ProjectAndOpenSourceComponents");
        ProjectResourcesHintTextBlock.Text = T("Settings.MajorOpenSourceProjectsUsedOr");
        // The button names say what the card holds; its explanation is the title's tooltip.
        ProjectResourcesHintTextBlock.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(ProjectResourcesTitleTextBlock, ProjectResourcesHintTextBlock.Text);

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
            // Name only, so the list takes two short rows; the address is in the tooltip.
            if (resourceButton.Content is StackPanel compact)
            {
                foreach (TextBlock address in compact.Children.OfType<TextBlock>().Skip(1))
                {
                    address.Visibility = Visibility.Collapsed;
                }
                // Long names ("SQLite / Microsoft.Data.Sqlite") wrap to a second line; a row's buttons share its height.
                if (compact.Children.OfType<TextBlock>().FirstOrDefault() is { } name)
                {
                    name.TextWrapping = TextWrapping.WrapWholeWords;
                    name.MaxLines = 2;
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                }
                resourceButton.MinHeight = 36;
                resourceButton.VerticalAlignment = VerticalAlignment.Stretch;
            }
            AutomationProperties.SetName(resourceButton, $"{resourceName} · {url}");
        }
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
        => ViewModel.Request(SettingsAction.OpenLink, (sender as Button)?.Tag as string);

    // Layout: a section list beside or above one card, or on wide pages every card in two columns.
    private void OnSettingsSectionClicked(object sender, RoutedEventArgs e)
    {
        ShowSettingsSection(Array.IndexOf(_settingsSectionButtons, sender));
    }

    private void ShowSettingsSection(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _settingsCards.Length)
        {
            return;
        }

        _selectedSettingsSection = sectionIndex;
        SettingsSectionScrollViewer.ChangeView(null, 0, null, true);
        for (int index = 0; index < _settingsCards.Length; index++)
        {
            bool selected = index == sectionIndex;
            _settingsCards[index].Visibility = _settingsLayoutMode == 3 || selected
                ? Visibility.Visible
                : Visibility.Collapsed;
            _settingsSectionButtons[index].Background = ThemeBrush(
                selected ? "AppSecondarySelectedBrush" : "AppSecondaryDefaultBrush");
            _settingsSectionButtons[index].BorderBrush = ThemeBrush(
                selected ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush");
            _settingsSectionButtons[index].Foreground = ThemeBrush(
                selected ? "AppSecondarySelectedForegroundBrush" : "AppSecondaryDefaultForegroundBrush");
        }
    }

    // A hidden page has no size yet, so it estimates from the window minus the navigation.
    private double PageWidth()
        => SettingsScrollViewer.ActualWidth > 0
            ? SettingsScrollViewer.ActualWidth
            : Math.Max(420, (XamlRoot?.Size.Width ?? 0) - 240);

    private double SettingsHeaderHeight()
        => SettingsPageHeader.ActualHeight > 0 ? SettingsPageHeader.ActualHeight : 56;

    private void UpdateSettingsLayout()
    {
        double available = PageWidth();
        SettingsRootPanel.Width = Math.Max(420, Math.Min(3200, available - 12));
        int mode = SettingsLayoutPolicy.Mode(available);
        foreach (TextBlock label in _settingsSectionLabels)
        {
            label.MaxWidth = mode == 2 ? 120 : Math.Max(50, available / 4 - 80);
        }
        ReflowCards(ProjectResourcesGrid, available >= 900 ? 3 : 2);
        ApplySettingsViewportSizing(mode);
        if (_settingsLayoutMode == mode)
        {
            return;
        }
        _settingsLayoutMode = mode;

        SettingsResponsiveGrid.ColumnDefinitions.Clear();
        SettingsResponsiveGrid.RowDefinitions.Clear();
        if (mode == 3)
        {
            ArrangeSettingsOverview();
            SettingsNavigationCard.Visibility = Visibility.Collapsed;
            SettingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SettingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(SettingsSectionScrollViewer, 0, 0);
        }
        else if (mode == 2)
        {
            ArrangeSettingsFocus();
            SettingsNavigationCard.Visibility = Visibility.Visible;
            SettingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
            SettingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SettingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(SettingsNavigationCard, 0, 0);
            Place(SettingsSectionScrollViewer, 0, 1);
            ReflowCards(SettingsNavigationGrid, 1);
        }
        else
        {
            ArrangeSettingsFocus();
            SettingsNavigationCard.Visibility = Visibility.Visible;
            SettingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SettingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SettingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(SettingsNavigationCard, 0, 0);
            Place(SettingsSectionScrollViewer, 1, 0);
            ReflowCards(SettingsNavigationGrid, mode == 1 ? 4 : 2);
        }

        ShowSettingsSection(_selectedSettingsSection);
        ApplySettingsViewportSizing(mode);
    }

    private void ApplySettingsViewportSizing(int mode)
    {
        UpdateNotesScrollViewer.MinHeight = 0;
        UpdateNotesScrollViewer.MaxHeight = 150;

        // The overview keeps every card at its own height and lets the whole page scroll;
        // the narrower modes show one card that scrolls inside the viewport.
        bool overview = mode == 3;
        SettingsScrollViewer.VerticalScrollMode = overview ? ScrollMode.Auto : ScrollMode.Disabled;
        SettingsScrollViewer.VerticalScrollBarVisibility = overview ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        double navigation = mode == 1 ? Math.Max(130, SettingsNavigationCard.ActualHeight) : 0;
        SettingsSectionScrollViewer.MaxHeight = overview
            ? double.PositiveInfinity
            : SettingsLayoutPolicy.FocusHeight(SettingsScrollViewer.ActualHeight, SettingsHeaderHeight(), navigation);
        SettingsSectionScrollViewer.VerticalScrollMode = overview ? ScrollMode.Disabled : ScrollMode.Auto;
        SettingsSectionScrollViewer.VerticalScrollBarVisibility = overview
            ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    private void ArrangeSettingsOverview()
    {
        SettingsFocusHost.Children.Clear();
        SettingsOverviewLeftColumn.Children.Clear();
        SettingsOverviewRightColumn.Children.Clear();
        // The resources card sits under the right column rather than across the bottom, which
        // balances the two columns and keeps a windowed overview within one screen.
        foreach (int index in new[] { 0, 2 })
        {
            SettingsOverviewLeftColumn.Children.Add(_settingsCards[index]);
        }
        foreach (int index in new[] { 1, 3, 4 })
        {
            SettingsOverviewRightColumn.Children.Add(_settingsCards[index]);
        }
        SettingsFocusHost.Visibility = Visibility.Collapsed;
        SettingsOverviewGrid.Visibility = Visibility.Visible;
    }

    private void ArrangeSettingsFocus()
    {
        SettingsOverviewLeftColumn.Children.Clear();
        SettingsOverviewRightColumn.Children.Clear();
        SettingsFocusHost.Children.Clear();
        foreach (Border card in _settingsCards)
        {
            Place(card, 0, 0);
            SettingsFocusHost.Children.Add(card);
        }
        SettingsOverviewGrid.Visibility = Visibility.Collapsed;
        SettingsFocusHost.Visibility = Visibility.Visible;
    }

    private void UpdateAppUpdateActionsLayout()
    {
        bool stackUpdateActions = AppUpdateActionsGrid.ActualWidth < 540;
        AppUpdateActionColumn1.Width = new GridLength(1, GridUnitType.Star);
        AppUpdateActionColumn2.Width = stackUpdateActions ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        AppUpdateActionColumn3.Width = stackUpdateActions ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(OpenGitHubButton, 0);
        Grid.SetRow(OpenGitHubButton, 0);
        Grid.SetColumn(CheckUpdatesButton, stackUpdateActions ? 0 : 1);
        Grid.SetRow(CheckUpdatesButton, stackUpdateActions ? 1 : 0);
        Grid.SetColumn(InstallUpdateButton, stackUpdateActions ? 0 : 2);
        Grid.SetRow(InstallUpdateButton, stackUpdateActions ? 2 : 0);
    }

    private static void Place(FrameworkElement child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        Grid.SetColumnSpan(child, 1);
    }

    private static void ReflowCards(Grid grid, int columns)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.RowSpacing = 8;
        grid.ColumnSpacing = 8;
        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        int count = grid.Children.Count;
        for (int row = 0; row < (count + columns - 1) / columns; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int slot = 0; slot < count; slot++)
        {
            FrameworkElement child = (FrameworkElement)grid.Children[slot];
            Grid.SetRow(child, slot / columns);
            Grid.SetColumn(child, slot % columns);
            Grid.SetColumnSpan(child, 1);
            if (child is Button button)
            {
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }
    }
}
