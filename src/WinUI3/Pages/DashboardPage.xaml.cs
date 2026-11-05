using System.ComponentModel;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The dashboard (stage J4, docs/development/UI-Structure-Plan.md): the current repository, its paths,
/// the character overview, every repository and the quick actions. It labels and lays itself out,
/// shows <see cref="DashboardViewModel"/> and <see cref="RepositoryPathsViewModel"/>, and sends clicks
/// out as requests.
/// </summary>
public sealed partial class DashboardPage : UserControl
{
    private readonly AppServices _services;
    private readonly (TextBox Box, FontIcon Icon, bool ExpectFile)[] _pathStatusIcons;
    private bool _isApplyingRepositorySelection;
    private int _layoutMode = -1;
    private int _pathColumns = -1;

    internal DashboardPage(AppServices services)
    {
        _services = services;
        ViewModel = services.Dashboard;
        Paths = services.Paths;
        InitializeComponent();

        _pathStatusIcons =
        [
            (SourceTextBox, SourceStatusIcon, false),
            (TargetTextBox, TargetStatusIcon, false),
            (LauncherTextBox, LauncherStatusIcon, true)
        ];
        // The page lays itself out from its own size; a hidden page reports zero and lays out again when shown.
        DashboardScrollViewer.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width > 0) UpdateDashboardLayout();
        };
        Paths.PropertyChanged += (_, _) => UpdatePathStatusIcons();
        ViewModel.Refreshed += (_, _) => OnRefreshed();
        ViewModel.PropertyChanged += OnViewModelChanged;
        services.Language.LanguageChanged += (_, _) => ApplyLanguage();
        services.Appearance.ThemeChanged += (_, _) => UpdatePathStatusIcons();
        // The language is read from the settings after the page is made, so label again when it is shown.
        Loaded += (_, _) => ApplyLanguage();
        ApplyLanguage();
    }

    public DashboardViewModel ViewModel { get; }

    public RepositoryPathsViewModel Paths { get; }

    /// <summary>The source, target and launcher boxes, for the window's focus tracking.</summary>
    internal IEnumerable<TextBox> PathTextBoxes => [SourceTextBox, TargetTextBox, LauncherTextBox];

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private Brush ThemeBrush(string token) => ThemeTokens.Get(token, _services.Appearance.ThemeKey);

    private Grid[] PathRows => [SourcePathRow, TargetPathRow, LauncherPathRow];

    private void ApplyLanguage()
    {
        ToolTipService.SetToolTip(AddRepositoryButton, T("Localization.CreateRepository"));
        AutomationProperties.SetName(AddRepositoryButton, T("Localization.CreateRepository"));
        ToolTipService.SetToolTip(RenameRepositoryButton, T("Localization.EditCurrentRepository"));
        AutomationProperties.SetName(RenameRepositoryButton, T("Localization.EditCurrentRepository"));
        DashboardPageHeader.Title = T("Localization.RepositoryDashboard");
        DashboardPageHeader.Description = T("Localization.SwitchRepositoriesCreatePresetsReviewPath");
        DashboardAddRepositoryTextBlock.Text = T("Localization.NewRepository");
        DashboardEditRepositoryTextBlock.Text = T("Localization.EditCurrent");
        DashboardCurrentRepositoryLabelTextBlock.Text = T("Localization.ActiveRepository");
        DashboardOpenRepositoryButton.Content = T("Localization.OpenModManagement");
        DashboardRepositoriesTitleTextBlock.Text = T("Common.AllRepositories");
        DashboardRepositoriesHintTextBlock.Text = T("Localization.CardsMarkTheActiveRepositoryAnd");
        DashboardCharactersTitleTextBlock.Text = T("Dashboard.Characters");
        DashboardCharactersHintTextBlock.Text = T("Dashboard.ClickACharacterToManage");
        DashboardQuickActionsTitleTextBlock.Text = T("Dashboard.QuickActions");
        DashboardBrowseOnlineTextBlock.Text = T("Dashboard.BrowseOnline");
        DashboardCheckUpdatesTextBlock.Text = T("Dashboard.CheckModUpdates");
        DashboardRunLauncherTextBlock.Text = T("Dashboard.RunLauncher");
        ToolTipService.SetToolTip(DashboardRunLauncherButton, T("Dashboard.RunLauncherHint"));
        ToolTipService.SetToolTip(DashboardRepositoryComboBox, T("Localization.SwitchTheActiveRepository"));
        ToolTipService.SetToolTip(DashboardOpenRepositoryButton, T("Localization.OpenModManagementForTheActive"));

        // Paths.
        PathSectionTitleTextBlock.Text = T("Localization.PathsAndLauncher");
        PathSectionSubtitleTextBlock.Text = T("Localization.KeepTheModStorageFolderTarget");
        SourceLabelTextBlock.Text = T("Localization.ModStorage");
        TargetLabelTextBlock.Text = T("Localization.TargetFolder");
        LauncherLabelTextBlock.Text = T("Common.Launcher");
        PickSourceButton.Content = T("Localization.Browse");
        PickTargetButton.Content = T("Localization.Browse");
        PickLauncherButton.Content = T("Localization.BrowseEXE");
        OpenSourceButton.Content = T("Localization.OpenModStorage");
        OpenTargetButton.Content = T("Localization.OpenTargetFolder");
        OpenLauncherButton.Content = T("Localization.OpenLauncherFolder");
        LauncherTextBox.PlaceholderText = T("Localization.SelectXXMILauncherExeOrAnother");
        PathHintTextBlock.Text = T("Localization.LinkModeSharesRepositoryFilesLinking");
        LinkDeploymentToggleSwitch.Header = T("Localization.DeployModsWithDirectoryJunctions");
        LinkDeploymentToggleSwitch.OnContent = T("Localization.Link");
        LinkDeploymentToggleSwitch.OffContent = T("Localization.Copy");
        RefreshPathsHeader();
        RefreshBridgeButton();
        UpdatePathStatusIcons();
        OnRefreshed();
        UpdateDashboardLayout();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DashboardViewModel.CurrentRepositoryName):
                RefreshPathsHeader();
                break;
            case nameof(DashboardViewModel.BridgeLabel):
                RefreshBridgeButton();
                break;
        }
    }

    // The window refreshed the cards: the repository picker and the "New repository" menu follow them.
    private void OnRefreshed()
    {
        PopulateRepositorySwitcher();
        RefreshNewRepositoryMenu();
    }

    private void PopulateRepositorySwitcher()
    {
        _isApplyingRepositorySelection = true;
        try
        {
            DashboardRepositoryComboBox.Items.Clear();
            foreach (RepositoryCardViewModel repository in ViewModel.Repositories)
            {
                DashboardRepositoryComboBox.Items.Add(new ComboBoxItem { Content = repository.Name, Tag = repository.Id });
            }
            DashboardRepositoryComboBox.SelectedItem = DashboardRepositoryComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => ViewModel.Repositories.FirstOrDefault(card => card.Id == item.Tag as string)?.IsCurrent == true);
        }
        finally
        {
            _isApplyingRepositorySelection = false;
        }
    }

    // A blank repository, then the built-in game presets.
    private void RefreshNewRepositoryMenu()
    {
        NewRepositoryMenu.Items.Clear();
        foreach (NewRepositoryChoice choice in ViewModel.NewRepositoryChoices)
        {
            var item = new MenuFlyoutItem { Text = choice.Label, Tag = choice.PresetId, Icon = new FontIcon { Glyph = choice.Glyph } };
            if (choice.ToolTip is not null) ToolTipService.SetToolTip(item, choice.ToolTip);
            item.Click += (_, _) => ViewModel.Request(DashboardAction.NewRepository, choice.PresetId);
            NewRepositoryMenu.Items.Add(item);
            if (choice.PresetId is null) NewRepositoryMenu.Items.Add(new MenuFlyoutSeparator());
        }
    }

    private void RefreshPathsHeader()
    {
        string repositoryName = string.IsNullOrEmpty(ViewModel.CurrentRepositoryName)
            ? T("Common.NoRepositorySelected")
            : ViewModel.CurrentRepositoryName;
        DashboardPathsTitleTextBlock.Text = T("RefinedWorkspace.CurrentRepositoryPaths");
        DashboardPathsHintTextBlock.Text = T("RefinedWorkspace.ThesePathsBelongOnlyToSwitching", repositoryName);
    }

    private void RefreshBridgeButton()
    {
        BridgeOptionsButton.Content = new TextBlock { Text = ViewModel.BridgeLabel, TextWrapping = TextWrapping.Wrap };
        ToolTipService.SetToolTip(BridgeOptionsButton, T("EndfieldBridge.AppliesOnlyToThisEndfieldRepository"));
    }

    // One icon inside each path box shows whether the folder (or the launcher file) exists, so
    // "needs setup" points at the box to fix. The launcher is optional and shows nothing when empty.
    private void UpdatePathStatusIcons()
    {
        foreach ((TextBox box, FontIcon icon, bool expectFile) in _pathStatusIcons)
        {
            PathFieldStatus status = PathFieldStatuses.Evaluate(box.Text, expectFile);
            bool ready = status == PathFieldStatus.Ready;
            icon.Visibility = status == PathFieldStatus.Empty && expectFile ? Visibility.Collapsed : Visibility.Visible;
            icon.Glyph = ready ? "" : "";
            icon.Foreground = ThemeBrush(ready ? "AppSuccessBrush" : "AppDangerBrush");
            string tip = status switch
            {
                PathFieldStatus.Ready => T("Dashboard.PathFound"),
                PathFieldStatus.Missing => T("Dashboard.PathNotFound"),
                _ => T("Dashboard.PathNotSet")
            };
            ToolTipService.SetToolTip(icon, tip);
            AutomationProperties.SetName(icon, tip);
        }
    }

    // Clicks the window answers.
    private void OnDashboardRepositorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingRepositorySelection || DashboardRepositoryComboBox.SelectedItem is not ComboBoxItem { Tag: string repositoryId }) return;
        ViewModel.Request(DashboardAction.SwitchRepository, repositoryId);
    }

    private void OnEditRepositoryDetailsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.EditRepository);

    private void OnDashboardOpenRepositoryClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.OpenRepository);

    private void OnPickSourceClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.PickSource);

    private void OnPickTargetClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.PickTarget);

    private void OnPickLauncherClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.PickLauncher);

    private void OnOpenSourceClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.OpenSource);

    private void OnOpenTargetClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.OpenTarget);

    private void OnOpenLauncherClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.OpenLauncher);

    private void OnBridgeOptionsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.BridgeOptions);

    private void OnDashboardBrowseOnlineClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.BrowseOnline);

    private void OnDashboardCheckUpdatesClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.CheckModUpdates);

    private void OnRunLauncherClicked(object sender, RoutedEventArgs e) => ViewModel.Request(DashboardAction.RunLauncher);

    private void OnDashboardCharacterClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterChipViewModel character)
        {
            character.OpenCommand.Execute(null);
        }
    }

    // Layout: two columns (paths and characters beside the repositories) on wide pages, stacked otherwise.
    private double PageWidth()
        => DashboardScrollViewer.ActualWidth > 0 ? DashboardScrollViewer.ActualWidth : Math.Max(420, (XamlRoot?.Size.Width ?? 0) - 240);

    private void UpdateDashboardLayout()
    {
        double available = PageWidth();
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
        if (_pathColumns != pathColumns)
        {
            _pathColumns = pathColumns;
            foreach (Grid pathRow in PathRows)
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
        if (_layoutMode == mode)
        {
            return;
        }
        _layoutMode = mode;

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
