using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private Expander? _workspacePathsExpander;
    private Grid? _workspaceActions;
    private Grid? _workspacePageHost;
    private Grid? _workspaceContentGrid;
    private Grid? _workspaceWorkbench;
    private Grid? _workspaceDetailHost;
    private Border? _categoryCard;
    private Border? _modCard;
    private Border? _previewCard;
    private Border? _shortcutCard;
    private TabViewItem? _previewTab;
    private TabViewItem? _shortcutTab;
    private TextBlock? _workspaceTitleText;
    private TextBlock? _selectionEyebrowText;
    private TextBlock? _modernFolderText;
    private TextBlock? _modernStateLabel;
    private TextBlock? _modernStateText;
    private TextBlock? _categoriesBadgeLabel;
    private TextBlock? _modsBadgeLabel;
    private TextBlock? _dashboardPathTitleText;
    private TextBlock? _dashboardPathHintText;
    private StackPanel? _dashboardRootPanel;
    private Grid? _dashboardSummaryGrid;
    private Grid? _dashboardMainGrid;
    private Border? _dashboardRepositorySelectorCard;
    private Grid? _dashboardMetricsGrid;
    private Border? _dashboardPathCard;
    private Border? _dashboardPresetsCard;
    private Grid? _dashboardPresetsGrid;
    private Grid? _settingsResponsiveGrid;
    private StackPanel? _settingsRootPanel;
    private Border? _settingsHeaderCard;
    private Grid? _settingsNavigationGrid;
    private Border? _settingsNavigationCard;
    private Grid? _settingsContentHost;
    private Grid? _settingsOverviewGrid;
    private Grid? _settingsOverviewLeftColumn;
    private Grid? _settingsOverviewRightColumn;
    private Border[] _settingsCards = [];
    private Button[] _settingsSectionButtons = [];
    private TextBlock[] _settingsSectionLabels = [];
    private Grid[] _workspacePathRows = [];
    private int _modernWorkspaceMode = -1;
    private int _modernActionColumns = -1;
    private int _dashboardLayoutMode = -1;
    private int _settingsLayoutMode = -1;
    private int _selectedSettingsSection;

    // Recompose the original controls instead of replacing them. All existing handlers,
    // drag targets, keyboard shortcuts, and persistence paths continue to be used.
    private void InitializeRefinedWorkspace()
    {
        if (WorkspaceScrollViewer.Content is not StackPanel oldWorkspace
            || oldWorkspace.Children.Count < 4
            || oldWorkspace.Children[0] is not Border pathsCard
            || pathsCard.Child is not Grid pathsGrid
            || oldWorkspace.Children[2] is not Grid panelGrid)
        {
            return;
        }

        Border[] panelCards = panelGrid.Children.OfType<Border>().ToArray();
        if (panelCards.Length != 4)
        {
            return;
        }
        _categoryCard = panelCards[0];
        _modCard = panelCards[1];
        _shortcutCard = panelCards[2];
        _previewCard = panelCards[3];
        foreach (Border card in panelCards)
        {
            panelGrid.Children.Remove(card);
            card.Margin = new Thickness(0);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
        }

        _workspaceActions = (Grid)pathsGrid.Children.Last();
        pathsGrid.Children.Remove(_workspaceActions);
        Grid.SetRow(_workspaceActions, 0);
        _workspaceActions.RowSpacing = 8;

        _workspacePathRows = pathsGrid.Children.OfType<Grid>()
            .Where(grid => grid.ColumnDefinitions.Count == 4).ToArray();
        pathsCard.Child = null;
        pathsGrid.Padding = new Thickness(4, 8, 4, 4);
        pathsGrid.VerticalAlignment = VerticalAlignment.Top;
        _workspacePathsExpander = new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsExpanded = true,
            Content = pathsGrid
        };

        if (DashboardScrollViewer.Content is StackPanel dashboardPanel)
        {
            _dashboardPathTitleText = new TextBlock
            {
                Style = (Style)Application.Current.Resources["SectionTitleTextStyle"]
            };
            _dashboardPathHintText = new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap
            };
            var dashboardPathHeading = new StackPanel { Spacing = 4 };
            dashboardPathHeading.Children.Add(_dashboardPathTitleText);
            dashboardPathHeading.Children.Add(_dashboardPathHintText);
            var dashboardPathContent = new StackPanel { Spacing = 10 };
            dashboardPathContent.Children.Add(dashboardPathHeading);
            dashboardPathContent.Children.Add(_workspacePathsExpander);
            _dashboardPathCard = new Border
            {
                Style = (Style)Application.Current.Resources["CardBorderStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = dashboardPathContent
            };
            dashboardPanel.Children.Insert(2, _dashboardPathCard);
            InitializeDashboardLayout(dashboardPanel);
        }

        Border statusCard = (Border)oldWorkspace.Children[3];
        oldWorkspace.Children.Remove(statusCard);

        _selectionEyebrowText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        };
        _modernFolderText = CreateMirroredTextBlock(CurrentFolderTextBlock, 24, true);
        _modernFolderText.MaxLines = 1;
        _modernFolderText.TextTrimming = TextTrimming.CharacterEllipsis;
        _modernStateLabel = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _modernStateText = CreateMirroredTextBlock(CurrentStateTextBlock, 18, true);
        _modernStateText.HorizontalAlignment = HorizontalAlignment.Right;
        var selectionText = new StackPanel { Spacing = 3 };
        selectionText.Children.Add(_selectionEyebrowText);
        selectionText.Children.Add(_modernFolderText);
        var statePanel = new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        statePanel.Children.Add(_modernStateLabel);
        statePanel.Children.Add(_modernStateText);
        var selectionGrid = new Grid { ColumnSpacing = 18 };
        selectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selectionGrid.Children.Add(selectionText);
        Grid.SetColumn(statePanel, 1);
        selectionGrid.Children.Add(statePanel);
        var selectionSummaryCard = new Border
        {
            Style = (Style)Application.Current.Resources["SelectionSummaryBorderStyle"],
            Child = selectionGrid
        };

        _previewTab = new TabViewItem { Content = _previewCard, IsClosable = false };
        _shortcutTab = new TabViewItem { Content = _shortcutCard, IsClosable = false };
        var detailTabs = new TabView
        {
            IsAddTabButtonVisible = false,
            CanDragTabs = false,
            TabWidthMode = TabViewWidthMode.Equal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        detailTabs.TabItems.Add(_previewTab);
        detailTabs.TabItems.Add(_shortcutTab);
        detailTabs.SelectedIndex = 0;

        _workspaceDetailHost = new Grid { RowSpacing = 12 };
        _workspaceDetailHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workspaceDetailHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _workspaceDetailHost.Children.Add(selectionSummaryCard);
        Grid.SetRow(detailTabs, 1);
        _workspaceDetailHost.Children.Add(detailTabs);

        _workspaceWorkbench = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        _workspaceWorkbench.Children.Add(_categoryCard);
        _workspaceWorkbench.Children.Add(_modCard);
        _workspaceWorkbench.Children.Add(_workspaceDetailHost);

        _workspaceTitleText = new TextBlock
        {
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(_workspaceTitleText);

        StackPanel categoryIndicator = CreateCountIndicator("\uE8B7", out _categoriesBadgeLabel,
            CreateMirroredTextBlock(FirstCountTextBlock, 14, true));
        StackPanel modIndicator = CreateCountIndicator("\uE8A5", out _modsBadgeLabel,
            CreateMirroredTextBlock(SecondCountTextBlock, 14, true));
        var counts = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        counts.Children.Add(categoryIndicator);
        counts.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Opacity = 0.7,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppCardBorderBrush"]
        });
        counts.Children.Add(modIndicator);
        var headingLine = new Grid { ColumnSpacing = 12 };
        headingLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headingLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headingLine.Children.Add(heading);
        Grid.SetColumn(counts, 1);
        headingLine.Children.Add(counts);

        var toolbarLayout = new Grid { RowSpacing = 10 };
        toolbarLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbarLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbarLayout.Children.Add(headingLine);
        Grid.SetRow(_workspaceActions, 1);
        toolbarLayout.Children.Add(_workspaceActions);
        var toolbarCard = new Border
        {
            Style = (Style)Application.Current.Resources["CommandSurfaceBorderStyle"],
            Child = toolbarLayout
        };

        _workspaceContentGrid = new Grid
        {
            RowSpacing = 10,
            Margin = new Thickness(0, 0, 8, 4),
            VerticalAlignment = VerticalAlignment.Top
        };
        _workspaceContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _workspaceContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workspaceContentGrid.Children.Add(_workspaceWorkbench);
        Grid.SetRow(statusCard, 1);
        _workspaceContentGrid.Children.Add(statusCard);
        WorkspaceScrollViewer.Content = _workspaceContentGrid;
        WorkspaceScrollViewer.VerticalScrollMode = ScrollMode.Disabled;
        WorkspaceScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        WorkspaceScrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
        WorkspaceScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        var host = new Grid { RowSpacing = 12 };
        _workspacePageHost = host;
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        int pageIndex = PagesHostGrid.Children.IndexOf(WorkspaceScrollViewer);
        PagesHostGrid.Children.Remove(WorkspaceScrollViewer);
        PagesHostGrid.Children.Insert(pageIndex, host);
        host.Children.Add(toolbarCard);
        Grid.SetRow(WorkspaceScrollViewer, 1);
        host.Children.Add(WorkspaceScrollViewer);
        WorkspaceScrollViewer.Visibility = Visibility.Visible;
        host.Visibility = _currentPrimarySection == PrimarySection.Repository
            ? Visibility.Visible
            : Visibility.Collapsed;

        InitializeResponsiveSettingsLayout();
        CopyProgressBar.Height = 3;
        RootGrid.SizeChanged += (_, _) => UpdateRefinedWorkspaceLayout();
        UpdateRefinedWorkspaceLanguage();
        UpdateRefinedWorkspaceLayout();
    }

    private static TextBlock CreateMirroredTextBlock(TextBlock source, double fontSize, bool semiBold)
    {
        var target = new TextBlock
        {
            Text = source.Text,
            Foreground = source.Foreground,
            FontSize = fontSize,
            FontWeight = semiBold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };
        source.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => target.Text = source.Text);
        source.RegisterPropertyChangedCallback(TextBlock.ForegroundProperty, (_, _) => target.Foreground = source.Foreground);
        return target;
    }

    private static StackPanel CreateCountIndicator(string glyph, out TextBlock label, TextBlock value)
    {
        label = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        value.FontSize = 14;
        value.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        value.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Opacity = 0.85 });
        row.Children.Add(label);
        row.Children.Add(value);
        return row;
    }

    private void UpdateRefinedWorkspaceLanguage()
    {
        if (_workspacePathsExpander is null)
        {
            return;
        }
        _workspacePathsExpander.Header = L("路径、启动器与部署方式", "Paths, launcher, and deployment mode");
        _workspaceTitleText!.Text = L("Mod 工作台", "Mod Workspace");
        _selectionEyebrowText!.Text = L("当前选择", "CURRENT SELECTION");
        _modernStateLabel!.Text = L("部署状态", "DEPLOYMENT STATUS");
        _categoriesBadgeLabel!.Text = L("分类", "Categories");
        _modsBadgeLabel!.Text = "Mods";
        _previewTab!.Header = L("预览与链接", "Preview & Link");
        _shortcutTab!.Header = L("快捷键", "Shortcuts");
        if (_settingsSectionLabels.Length == 4)
        {
            _settingsSectionLabels[0].Text = L("界面与语言", "Appearance & Language");
            _settingsSectionLabels[1].Text = L("软件更新", "App Updates");
            _settingsSectionLabels[2].Text = L("仓库在线", "Repository Online");
            _settingsSectionLabels[3].Text = L("诊断工具", "Diagnostics");
            ShowSettingsSection(_selectedSettingsSection);
        }
        RefreshRefinedDashboardPathHeader();
    }

    private void InitializeDashboardLayout(StackPanel dashboardPanel)
    {
        if (dashboardPanel.Children.Count < 6
            || dashboardPanel.Children[0] is not Border header
            || dashboardPanel.Children[1] is not Border repositorySelector
            || dashboardPanel.Children[3] is not Grid metrics
            || dashboardPanel.Children[4] is not Border presets
            || dashboardPanel.Children[5] is not Border repositories
            || _dashboardPathCard is null)
        {
            return;
        }

        _dashboardRootPanel = dashboardPanel;
        _dashboardRepositorySelectorCard = repositorySelector;
        _dashboardMetricsGrid = metrics;
        _dashboardPresetsCard = presets;
        _dashboardRepositorySelectorCard.VerticalAlignment = VerticalAlignment.Stretch;
        _dashboardMetricsGrid.VerticalAlignment = VerticalAlignment.Stretch;
        _dashboardPresetsCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        _dashboardPresetsCard.VerticalAlignment = VerticalAlignment.Stretch;
        _dashboardPresetsGrid = DashboardPresetsGrid;

        dashboardPanel.Children.Clear();
        dashboardPanel.Margin = new Thickness(2, 2, 8, 20);
        dashboardPanel.Spacing = 12;

        _dashboardSummaryGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            VerticalAlignment = VerticalAlignment.Top
        };
        _dashboardSummaryGrid.Children.Add(repositorySelector);
        _dashboardSummaryGrid.Children.Add(metrics);

        _dashboardMainGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            VerticalAlignment = VerticalAlignment.Top
        };
        _dashboardMainGrid.Children.Add(_dashboardPathCard);
        _dashboardMainGrid.Children.Add(presets);

        dashboardPanel.Children.Add(header);
        dashboardPanel.Children.Add(_dashboardSummaryGrid);
        dashboardPanel.Children.Add(_dashboardMainGrid);
        dashboardPanel.Children.Add(repositories);
        DashboardScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private void InitializeResponsiveSettingsLayout()
    {
        if (SettingsScrollViewer.Content is not StackPanel settingsPanel || settingsPanel.Children.Count < 5)
        {
            return;
        }

        if (settingsPanel.Children[1] is not Border appearance
            || settingsPanel.Children[2] is not Border updates
            || settingsPanel.Children[3] is not Border online
            || settingsPanel.Children[4] is not Border diagnostics)
        {
            return;
        }

        for (int index = 4; index >= 1; index--)
        {
            settingsPanel.Children.RemoveAt(index);
        }

        _settingsCards = [appearance, updates, online, diagnostics];
        _settingsRootPanel = settingsPanel;
        _settingsHeaderCard = settingsPanel.Children[0] as Border;
        foreach (Border card in _settingsCards)
        {
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Top;
            card.Padding = new Thickness(14);
            if (card.Child is StackPanel cardPanel)
            {
                cardPanel.Spacing = 10;
            }
        }

        string[] glyphs = ["\uE790", "\uE895", "\uE774", "\uE9D9"];
        _settingsSectionButtons = new Button[_settingsCards.Length];
        _settingsSectionLabels = new TextBlock[_settingsCards.Length];
        _settingsNavigationGrid = new Grid { RowSpacing = 8, ColumnSpacing = 8 };
        for (int index = 0; index < _settingsCards.Length; index++)
        {
            var label = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _settingsSectionLabels[index] = label;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            content.Children.Add(new FontIcon { Glyph = glyphs[index], FontSize = 17 });
            content.Children.Add(label);
            var button = new Button
            {
                Tag = index,
                Content = content,
                MinHeight = 48,
                Padding = new Thickness(14, 10, 14, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"]
            };
            button.Click += OnSettingsSectionClicked;
            _settingsSectionButtons[index] = button;
            _settingsNavigationGrid.Children.Add(button);
        }

        _settingsNavigationCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Padding = new Thickness(10),
            VerticalAlignment = VerticalAlignment.Top,
            Child = _settingsNavigationGrid
        };

        _settingsContentHost = new Grid { VerticalAlignment = VerticalAlignment.Top };
        foreach (Border card in _settingsCards)
        {
            _settingsContentHost.Children.Add(card);
        }

        _settingsOverviewLeftColumn = CreateSettingsOverviewColumn();
        _settingsOverviewRightColumn = CreateSettingsOverviewColumn();
        _settingsOverviewGrid = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Top };
        _settingsOverviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _settingsOverviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _settingsOverviewGrid.Children.Add(_settingsOverviewLeftColumn);
        Grid.SetColumn(_settingsOverviewRightColumn, 1);
        _settingsOverviewGrid.Children.Add(_settingsOverviewRightColumn);

        _settingsResponsiveGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        _settingsResponsiveGrid.Children.Add(_settingsNavigationCard);
        _settingsResponsiveGrid.Children.Add(_settingsContentHost);

        settingsPanel.Spacing = 12;
        settingsPanel.Margin = new Thickness(2, 2, 8, 20);
        settingsPanel.MaxWidth = 3200;
        settingsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        settingsPanel.Children.Add(_settingsResponsiveGrid);
        SettingsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ShowSettingsSection(0);
    }

    private static Grid CreateSettingsOverviewColumn()
    {
        var column = new Grid
        {
            RowSpacing = 10,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        return column;
    }

    private void OnSettingsSectionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int sectionIndex })
        {
            ShowSettingsSection(sectionIndex);
        }
    }

    private void ShowSettingsSection(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _settingsCards.Length)
        {
            return;
        }

        _selectedSettingsSection = sectionIndex;
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
        if (_dashboardPathTitleText is null || _dashboardPathHintText is null || _workspacePathsExpander is null)
        {
            return;
        }

        WorkspaceRepository? repository = GetSelectedRepository();
        string repositoryName = repository?.Name ?? L("未选择仓库", "No repository selected");
        _dashboardPathTitleText.Text = L("当前仓库路径", "Current Repository Paths");
        _dashboardPathHintText.Text = L(
            $"以下路径只属于“{repositoryName}”；切换仓库后会显示另一组独立配置。",
            $"These paths belong only to “{repositoryName}”. Switching repositories shows its independent configuration.");
        _workspacePathsExpander.Header = L($"配置 {repositoryName}", $"Configure {repositoryName}");
    }

    private void RefreshRefinedActionIcons()
    {
        SetActionIcon(CreateFirstLevelButton, "\uE710", L("新建分类", "New category"));
        SetActionIcon(RenameFirstLevelButton, "\uE70F", L("重命名分类", "Rename category"));
        SetActionIcon(DeleteSecondLevelButton, "\uE74D", L("删除选中的 Mod", "Delete selected mod"));
    }

    private static void SetActionIcon(Button button, string glyph, string label)
    {
        button.Content = new FontIcon { Glyph = glyph, FontSize = 16 };
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
    }

    private void UpdateRefinedWorkspaceLayout()
    {
        if (_workspaceActions is null || _workspaceWorkbench is null || _workspaceDetailHost is null)
        {
            return;
        }

        double available = WorkspaceScrollViewer.ActualWidth;
        if (available <= 0)
        {
            available = Math.Max(420, Bounds.Width - 240);
        }
        int mode = available >= 1180 ? 2 : available >= 760 ? 1 : 0;
        if (_modernWorkspaceMode != mode)
        {
            _modernWorkspaceMode = mode;
            _workspaceWorkbench.ColumnDefinitions.Clear();
            _workspaceWorkbench.RowDefinitions.Clear();
            if (mode == 2)
            {
                _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star), MinWidth = 240 });
                _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star), MinWidth = 310 });
                _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star), MinWidth = 430 });
                _workspaceWorkbench.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Place(_categoryCard!, 0, 0);
                Place(_modCard!, 0, 1);
                Place(_workspaceDetailHost, 0, 2);
            }
            else if (mode == 1)
            {
                _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
                _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                _workspaceWorkbench.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.8, GridUnitType.Star) });
                _workspaceWorkbench.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) });
                Place(_categoryCard!, 0, 0);
                Place(_modCard!, 0, 1);
                Place(_workspaceDetailHost, 1, 0, 2);
            }
            else
            {
                _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (int row = 0; row < 3; row++)
                {
                    _workspaceWorkbench.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(row == 2 ? 1.1 : row == 0 ? 0.65 : 0.75, GridUnitType.Star)
                    });
                }
                Place(_categoryCard!, 0, 0);
                Place(_modCard!, 1, 0);
                Place(_workspaceDetailHost, 2, 0);
            }
        }

        int actionColumns = available >= 940 ? 4 : available >= 520 ? 2 : 1;
        if (_modernActionColumns != actionColumns)
        {
            _modernActionColumns = actionColumns;
            ReflowCards(_workspaceActions, actionColumns, [1, 2, 3, 4, 0]);
            Grid.SetColumnSpan((FrameworkElement)_workspaceActions.Children[0], actionColumns);
        }

        double pathAvailable = _workspacePathsExpander?.ActualWidth ?? available;
        if (pathAvailable <= 0)
        {
            pathAvailable = available;
        }
        foreach (Grid pathRow in _workspacePathRows)
        {
            int pathColumns = pathAvailable >= 1040 ? 4 : pathAvailable >= 620 ? 2 : 1;
            ReflowCards(pathRow, pathColumns);
            if (pathColumns == 4)
            {
                pathRow.ColumnDefinitions[0].Width = new GridLength(112);
                pathRow.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                pathRow.ColumnDefinitions[2].Width = new GridLength(112);
                pathRow.ColumnDefinitions[3].Width = new GridLength(180);
            }
        }

        double workspaceViewportHeight = WorkspaceScrollViewer.ActualHeight > 0
            ? WorkspaceScrollViewer.ActualHeight
            : RootGrid.ActualHeight - 300;
        double contentHeight = Math.Max(460, workspaceViewportHeight - 4);
        if (_workspaceContentGrid is not null)
        {
            _workspaceContentGrid.Height = contentHeight;
        }
        double statusHeight = 54;
        double workbenchHeight = Math.Max(380, contentHeight - statusHeight - 10);
        _workspaceWorkbench.Height = workbenchHeight;
        double panelHeight = mode == 2
            ? workbenchHeight
            : mode == 1
                ? (workbenchHeight - 12) * 0.4
                : (workbenchHeight - 24) * 0.26;
        double listHeight = Math.Clamp(panelHeight - 138, 150, 940);
        FirstLevelListView.Height = listHeight;
        SecondLevelListView.Height = listHeight;
        _previewCard!.MinHeight = 0;
        _shortcutCard!.MinHeight = 0;
        UpdateDashboardLayout();
        UpdateSettingsLayout();
    }

    private void UpdateDashboardLayout()
    {
        if (_dashboardSummaryGrid is null
            || _dashboardMainGrid is null
            || _dashboardRepositorySelectorCard is null
            || _dashboardMetricsGrid is null
            || _dashboardPathCard is null
            || _dashboardPresetsCard is null)
        {
            return;
        }

        double available = DashboardScrollViewer.ActualWidth;
        if (available <= 0)
        {
            available = Math.Max(420, Bounds.Width - 240);
        }
        int mode = available >= 1460 ? 2 : available >= 860 ? 1 : 0;
        if (_dashboardLayoutMode == mode)
        {
            return;
        }
        _dashboardLayoutMode = mode;
        _dashboardRepositorySelectorCard.VerticalAlignment = mode == 2
            ? VerticalAlignment.Stretch
            : VerticalAlignment.Top;
        _dashboardMetricsGrid.VerticalAlignment = mode == 2
            ? VerticalAlignment.Stretch
            : VerticalAlignment.Top;
        _dashboardPathCard.VerticalAlignment = mode == 2
            ? VerticalAlignment.Stretch
            : VerticalAlignment.Top;
        _dashboardPresetsCard.VerticalAlignment = mode == 2
            ? VerticalAlignment.Stretch
            : VerticalAlignment.Top;

        _dashboardSummaryGrid.ColumnDefinitions.Clear();
        _dashboardSummaryGrid.RowDefinitions.Clear();
        _dashboardMainGrid.ColumnDefinitions.Clear();
        _dashboardMainGrid.RowDefinitions.Clear();

        if (mode == 2)
        {
            _dashboardSummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            _dashboardSummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.9, GridUnitType.Star) });
            _dashboardSummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_dashboardRepositorySelectorCard, 0, 0);
            Place(_dashboardMetricsGrid, 0, 1);

            _dashboardMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
            _dashboardMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
            _dashboardMainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_dashboardPathCard, 0, 0);
            Place(_dashboardPresetsCard, 0, 1);
        }
        else
        {
            _dashboardSummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _dashboardSummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _dashboardSummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_dashboardRepositorySelectorCard, 0, 0);
            Place(_dashboardMetricsGrid, 1, 0);

            _dashboardMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _dashboardMainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _dashboardMainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_dashboardPathCard, 0, 0);
            Place(_dashboardPresetsCard, 1, 0);
        }

        ReflowCards(_dashboardMetricsGrid, mode == 0 && available < 620 ? 1 : 3);
        if (_dashboardPresetsGrid is not null)
        {
            int presetColumns = mode == 2
                ? 1
                : mode == 0 && available < 680
                    ? 1
                    : mode == 0
                        ? 2
                        : 3;
            ReflowCards(_dashboardPresetsGrid, presetColumns);
            if (mode == 2)
            {
                foreach (RowDefinition row in _dashboardPresetsGrid.RowDefinitions)
                {
                    row.Height = new GridLength(1, GridUnitType.Star);
                }
            }
        }
    }

    private void UpdateSettingsLayout()
    {
        if (_settingsResponsiveGrid is null
            || _settingsNavigationGrid is null
            || _settingsNavigationCard is null
            || _settingsContentHost is null)
        {
            return;
        }

        double available = SettingsScrollViewer.ActualWidth;
        if (available <= 0)
        {
            available = Math.Max(420, Bounds.Width - 240);
        }
        if (_settingsRootPanel is not null)
        {
            _settingsRootPanel.Width = Math.Max(420, Math.Min(3200, available - 12));
        }
        int mode = available >= 1080 ? 3 : available >= 760 ? 2 : 1;
        ApplySettingsViewportSizing(mode, available);
        if (_settingsLayoutMode == mode)
        {
            return;
        }
        _settingsLayoutMode = mode;

        _settingsResponsiveGrid.ColumnDefinitions.Clear();
        _settingsResponsiveGrid.RowDefinitions.Clear();
        if (mode == 3)
        {
            ArrangeSettingsOverview();
            _settingsNavigationCard.Visibility = Visibility.Collapsed;
            _settingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _settingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_settingsContentHost, 0, 0);
        }
        else if (mode == 2)
        {
            ArrangeSettingsFocus();
            _settingsNavigationCard.Visibility = Visibility.Visible;
            _settingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(238) });
            _settingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _settingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_settingsNavigationCard, 0, 0);
            Place(_settingsContentHost, 0, 1);
            ReflowCards(_settingsNavigationGrid, 1);
        }
        else
        {
            ArrangeSettingsFocus();
            _settingsNavigationCard.Visibility = Visibility.Visible;
            _settingsResponsiveGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _settingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _settingsResponsiveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_settingsNavigationCard, 0, 0);
            Place(_settingsContentHost, 1, 0);
            ReflowCards(_settingsNavigationGrid, mode == 1 ? 4 : 2);
        }

        ShowSettingsSection(_selectedSettingsSection);
        ApplySettingsViewportSizing(mode, available);
    }

    private void ApplySettingsViewportSizing(int mode, double availableWidth)
    {
        if (_settingsOverviewGrid is null)
        {
            return;
        }

        _settingsOverviewGrid.MinHeight = 0;
        for (int index = 0; index < _settingsCards.Length; index++)
        {
            Border card = _settingsCards[index];
            card.MinHeight = 0;
            card.Padding = new Thickness(14);
            if (card.Child is StackPanel panel)
            {
                panel.Spacing = 10;
                panel.VerticalAlignment = VerticalAlignment.Top;
            }
        }
        UpdateNotesScrollViewer.MinHeight = 0;
        UpdateNotesScrollViewer.MaxHeight = 150;

        bool fillWideViewport = mode == 3 && availableWidth >= 1600;
        if (!fillWideViewport || SettingsScrollViewer.ActualHeight <= 0)
        {
            return;
        }

        double headerHeight = _settingsHeaderCard?.ActualHeight > 0
            ? _settingsHeaderCard.ActualHeight
            : 88;
        double usableHeight = Math.Max(720, SettingsScrollViewer.ActualHeight - headerHeight - 46);
        double overviewHeight = Math.Max(720, usableHeight * 0.86);
        _settingsOverviewGrid.MinHeight = overviewHeight;
        foreach (Border card in _settingsCards)
        {
            card.Padding = new Thickness(18);
            if (card.Child is StackPanel panel)
            {
                panel.Spacing = 14;
            }
        }
        if (_settingsCards[0].Child is StackPanel appearancePanel)
        {
            appearancePanel.VerticalAlignment = VerticalAlignment.Center;
        }
        if (_settingsCards[3].Child is StackPanel diagnosticsPanel)
        {
            diagnosticsPanel.VerticalAlignment = VerticalAlignment.Center;
        }
        UpdateNotesScrollViewer.MinHeight = 180;
        UpdateNotesScrollViewer.MaxHeight = 300;
    }

    private void ArrangeSettingsOverview()
    {
        if (_settingsContentHost is null
            || _settingsOverviewGrid is null
            || _settingsOverviewLeftColumn is null
            || _settingsOverviewRightColumn is null)
        {
            return;
        }

        _settingsContentHost.Children.Clear();
        _settingsOverviewLeftColumn.Children.Clear();
        _settingsOverviewRightColumn.Children.Clear();
        foreach (Border card in _settingsCards)
        {
            card.VerticalAlignment = VerticalAlignment.Stretch;
        }
        Place(_settingsCards[0], 0, 0);
        _settingsOverviewLeftColumn.Children.Add(_settingsCards[0]);
        Place(_settingsCards[2], 1, 0);
        _settingsOverviewLeftColumn.Children.Add(_settingsCards[2]);
        Place(_settingsCards[1], 0, 0);
        _settingsOverviewRightColumn.Children.Add(_settingsCards[1]);
        Place(_settingsCards[3], 1, 0);
        _settingsOverviewRightColumn.Children.Add(_settingsCards[3]);
        _settingsContentHost.Children.Add(_settingsOverviewGrid);
    }

    private void ArrangeSettingsFocus()
    {
        if (_settingsContentHost is null
            || _settingsOverviewLeftColumn is null
            || _settingsOverviewRightColumn is null)
        {
            return;
        }

        _settingsOverviewLeftColumn.Children.Clear();
        _settingsOverviewRightColumn.Children.Clear();
        _settingsContentHost.Children.Clear();
        foreach (Border card in _settingsCards)
        {
            card.VerticalAlignment = VerticalAlignment.Top;
            _settingsContentHost.Children.Add(card);
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
