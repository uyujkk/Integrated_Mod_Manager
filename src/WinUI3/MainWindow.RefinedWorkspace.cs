using IntegratedModManager.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private Border[] _settingsCards = [];
    private Button[] _settingsSectionButtons = [];
    private TextBlock[] _settingsSectionLabels = [];
    private int _dashboardLayoutMode = -1;
    private int _dashboardPathColumns = -1;
    private int _settingsLayoutMode = -1;
    private int _selectedSettingsSection;

    private Grid[] WorkspacePathRows => [SourcePathRow, TargetPathRow, LauncherPathRow];

    // The page frames are in MainWindow.xaml; this wires their layout and text. RepositoryWorkspace.cs
    // fills the repository cards afterwards.
    private void InitializeRefinedWorkspace()
    {
        InitializePathStatusIcons();
        WorkspacePageHost.Visibility = _currentPrimarySection == PrimarySection.Repository
            ? Visibility.Visible
            : Visibility.Collapsed;
        InitializeResponsiveSettingsLayout();
        InitializePageLayouts();
        RootGrid.SizeChanged += (_, _) => UpdateRefinedWorkspaceLayout();
        UpdateRefinedWorkspaceLanguage();
        UpdateRefinedWorkspaceLayout();
    }

    private void UpdateRefinedWorkspaceLanguage()
    {
        if (_settingsCards.Length == 0)
        {
            return;
        }
        SelectionEyebrowTextBlock.Text = T("RefinedWorkspace.CURRENTSELECTION");
        DeploymentStateLabelTextBlock.Text = T("RefinedWorkspace.DEPLOYMENTSTATUS");
        PreviewTab.Header = T("RefinedWorkspace.PreviewLink");
        ShortcutTab.Header = T("Common.Shortcuts");
        PersistentSlotsTab.Header = T("RefinedWorkspace.StateSlots");
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
        RefreshRefinedDashboardPathHeader();
        UpdateRepositoryWorkspaceLanguageV4();
    }

    private void InitializeResponsiveSettingsLayout()
    {
        // Status lines take no room until there is something to report.
        foreach (TextBlock status in new[] { DiagnosticsStatusTextBlock, UpdateStatusTextBlock })
        {
            status.Visibility = string.IsNullOrWhiteSpace(status.Text) ? Visibility.Collapsed : Visibility.Visible;
            status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) =>
            {
                var block = (TextBlock)sender;
                block.Visibility = string.IsNullOrWhiteSpace(block.Text) ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        // The layout itself is in MainWindow.xaml; these arrays pair each section button with its card.
        _settingsCards = [SettingsAppearanceCard, SettingsAppUpdatesCard, SettingsOnlineCard, SettingsDiagnosticsCard, ProjectResourcesCard];
        _settingsSectionButtons = [SettingsSectionButton0, SettingsSectionButton1, SettingsSectionButton2, SettingsSectionButton3, SettingsSectionButton4];
        _settingsSectionLabels = [SettingsSectionLabel0, SettingsSectionLabel1, SettingsSectionLabel2, SettingsSectionLabel3, SettingsSectionLabel4];
        SettingsNavigationCard.SizeChanged += (_, _) => UpdateSettingsLayout();
        AppUpdateActionsGrid.SizeChanged += (_, _) => UpdateAppUpdateActionsLayout();
        ShowSettingsSection(0);
    }

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
            _settingsSectionButtons[index].Background = GetAppThemeBrush(
                selected ? "AppSecondarySelectedBrush" : "AppSecondaryDefaultBrush");
            _settingsSectionButtons[index].BorderBrush = GetAppThemeBrush(
                selected ? "AppNavSelectedBorderBrush" : "AppCardBorderBrush");
            _settingsSectionButtons[index].Foreground = GetAppThemeBrush(
                selected ? "AppSecondarySelectedForegroundBrush" : "AppSecondaryDefaultForegroundBrush");
        }
    }

    private void RefreshRefinedDashboardPathHeader()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string repositoryName = repository?.Name ?? T("Common.NoRepositorySelected");
        DashboardPathsTitleTextBlock.Text = T("RefinedWorkspace.CurrentRepositoryPaths");
        DashboardPathsHintTextBlock.Text = T("RefinedWorkspace.ThesePathsBelongOnlyToSwitching", repositoryName);
    }

    private void RefreshRefinedActionIcons()
    {
        SetActionIcon(CreateFirstLevelButton, "\uE710", T("RefinedWorkspace.NewCategory"));
        SetActionIcon(RenameFirstLevelButton, "\uE70F", T("RefinedWorkspace.RenameCategory"));
        SetActionIcon(DeleteSecondLevelButton, "\uE74D", T("RefinedWorkspace.DeleteSelectedMod"));
        UpdateCharacterHeaderActions();
    }

    private static void SetActionIcon(Button button, string glyph, string label)
    {
        button.Content = new FontIcon { Glyph = glyph, FontSize = 16 };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
    }

    private void UpdateRefinedWorkspaceLayout()
    {
        if (_repositoryV4Ready)
        {
            UpdateRepositoryWorkspaceLayoutV4();
        }
        UpdateDashboardLayout();
        UpdateSettingsLayout();
    }

    private void UpdateDashboardLayout()
    {
        double available = PageWidth(DashboardScrollViewer);
        bool twoColumns = DashboardLayoutPolicy.UsesTwoColumns(available);
        bool oneRowStrip = DashboardLayoutPolicy.StripFitsOneRow(available);

        // Two columns reach the bottom of the window: the character overview and the repository
        // list take the height left after the header and the strip, instead of leaving it empty.
        double above = DashboardPageHeader.ActualHeight + DashboardRepositorySelectorCard.ActualHeight
            + 2 * DashboardRootPanel.Spacing + DashboardRootPanel.Margin.Top + DashboardRootPanel.Margin.Bottom;
        DashboardMainGrid.MinHeight = twoColumns && DashboardScrollViewer.ActualHeight > 0
            ? Math.Max(0, DashboardScrollViewer.ActualHeight - above)
            : 0;

        // Each path row puts its label, box and both buttons on one line when the page is wide enough.
        int pathColumns = available >= 1040 ? 4 : available >= 620 ? 2 : 1;
        if (_dashboardPathColumns != pathColumns)
        {
            _dashboardPathColumns = pathColumns;
            foreach (Grid pathRow in WorkspacePathRows)
            {
                ReflowCards(pathRow, pathColumns);
                if (pathColumns == 4)
                {
                    pathRow.ColumnDefinitions[0].Width = new GridLength(112);
                    pathRow.ColumnDefinitions[2].Width = new GridLength(112);
                    pathRow.ColumnDefinitions[3].Width = new GridLength(180);
                }
            }
        }

        int mode = (twoColumns ? 2 : 0) + (oneRowStrip ? 1 : 0);
        if (_dashboardLayoutMode == mode)
        {
            return;
        }
        _dashboardLayoutMode = mode;

        // Paths and characters beside the repository list on wide pages, stacked above it otherwise.
        DashboardMainGrid.ColumnDefinitions.Clear();
        DashboardMainGrid.RowDefinitions.Clear();
        if (twoColumns)
        {
            DashboardMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            DashboardMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Place(DashboardLeftColumn, 0, 0);
            Place(DashboardRightColumn, 0, 1);
        }
        else
        {
            DashboardMainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DashboardMainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(DashboardLeftColumn, 0, 0);
            Place(DashboardRightColumn, 1, 0);
        }

        // On narrow pages the status and buttons wrap under the repository picker.
        DashboardStripGrid.RowSpacing = oneRowStrip ? 0 : 10;
        Grid.SetRow(DashboardStripStatus, oneRowStrip ? 0 : 1);
        Grid.SetColumn(DashboardStripStatus, oneRowStrip ? 2 : 1);
        Grid.SetRow(DashboardStripActions, oneRowStrip ? 0 : 1);
        Grid.SetColumn(DashboardStripActions, oneRowStrip ? 3 : 2);
        Grid.SetColumnSpan(DashboardStripActions, oneRowStrip ? 1 : 2);
    }

    private void UpdateSettingsLayout()
    {
        if (_settingsCards.Length == 0)
        {
            return;
        }

        double available = PageWidth(SettingsScrollViewer);
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

    private static void Place(FrameworkElement child, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        Grid.SetColumnSpan(child, columnSpan);
    }

    private static void ReflowCards(Grid grid, int columns, int[]? order = null)
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
            FrameworkElement child = (FrameworkElement)grid.Children[order is null ? slot : order[slot]];
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
