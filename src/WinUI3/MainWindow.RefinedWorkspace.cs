using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private bool _refinedWorkspaceReady;

    // The page frames are in MainWindow.xaml; this wires their layout and text. RepositoryWorkspace.cs
    // fills the repository cards afterwards.
    private void InitializeRefinedWorkspace()
    {
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
        UpdateRepositoryWorkspaceLanguageV4();
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
