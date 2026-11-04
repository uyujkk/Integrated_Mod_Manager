using IntegratedModManager.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private int _dashboardLayoutMode = -1;
    private int _dashboardPathColumns = -1;
    private bool _refinedWorkspaceReady;

    private Grid[] WorkspacePathRows => [SourcePathRow, TargetPathRow, LauncherPathRow];

    // The page frames are in MainWindow.xaml; this wires their layout and text. RepositoryWorkspace.cs
    // fills the repository cards afterwards.
    private void InitializeRefinedWorkspace()
    {
        InitializePathStatusIcons();
        InitializePageLayouts();
        _refinedWorkspaceReady = true;
        RootGrid.SizeChanged += (_, _) => UpdateRefinedWorkspaceLayout();
        UpdateRefinedWorkspaceLanguage();
        UpdateRefinedWorkspaceLayout();
    }

    private void UpdateRefinedWorkspaceLanguage()
    {
        if (!_refinedWorkspaceReady)
        {
            return;
        }
        SelectionEyebrowTextBlock.Text = T("RefinedWorkspace.CURRENTSELECTION");
        DeploymentStateLabelTextBlock.Text = T("RefinedWorkspace.DEPLOYMENTSTATUS");
        PreviewTab.Header = T("RefinedWorkspace.PreviewLink");
        ShortcutTab.Header = T("Common.Shortcuts");
        PersistentSlotsTab.Header = T("RefinedWorkspace.StateSlots");
        RefreshRefinedDashboardPathHeader();
        UpdateRepositoryWorkspaceLanguageV4();
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
