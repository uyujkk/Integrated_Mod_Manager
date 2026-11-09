using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The dashboard (stages J4, docs/development/UI-Structure-Plan.md and UI-Fluent-Design-Plan.md): the
/// current repository with its main actions, the quick actions, the paths in one expander, the
/// characters and every repository. It labels and lays itself out, shows <see cref="DashboardViewModel"/>
/// and <see cref="RepositoryPathsViewModel"/>, and sends clicks out as requests.
/// </summary>
public sealed partial class DashboardPage : UserControl
{
    private readonly AppServices _services;
    private readonly (TextBox Box, FontIcon Icon, bool ExpectFile)[] _pathStatusIcons;
    private int _layoutMode = -1;
    private string? _autoExpandedFor;
    // Each new step cancels the opening of the one before (it opens after the page has scrolled to its target).
    private int _gettingStartedTipVersion;

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
        Paths.PropertyChanged += (_, _) => RefreshPathsState();
        ViewModel.Refreshed += (_, _) => OnRefreshed();
        ViewModel.PropertyChanged += OnViewModelChanged;
        services.Language.LanguageChanged += (_, _) => ApplyLanguage();
        services.Appearance.ThemeChanged += (_, _) => RefreshPathsState();
        // The language is read from the settings after the page is made, so label again when it is shown.
        Loaded += (_, _) => ApplyLanguage();
        // The getting-started tips show while the page is on screen and wait while it is not.
        Loaded += (_, _) => ShowGettingStartedTip();
        Unloaded += (_, _) => GettingStartedTip.IsOpen = false;
        ApplyLanguage();
    }

    public DashboardViewModel ViewModel { get; }

    public RepositoryPathsViewModel Paths { get; }

    /// <summary>The source, target and launcher boxes, for the window's focus tracking.</summary>
    internal IEnumerable<TextBox> PathTextBoxes => [SourceTextBox, TargetTextBox, LauncherTextBox];

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private Brush ThemeBrush(string token) => ThemeTokens.Get(token, _services.Appearance.ThemeKey);

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
        ToolTipService.SetToolTip(DashboardOpenRepositoryButton, T("Localization.OpenModManagementForTheActive"));
        ToolTipService.SetToolTip(DashboardRepositorySwitcher, T("Localization.SwitchTheActiveRepository"));
        AutomationProperties.SetName(DashboardRepositorySwitcher, T("Localization.SwitchTheActiveRepository"));
        DashboardRepositoriesTitleTextBlock.Text = T("Common.AllRepositories");
        DashboardRepositoriesHintTextBlock.Text = T("Localization.CardsMarkTheActiveRepositoryAnd");
        DashboardCharactersTitleTextBlock.Text = T("Dashboard.Characters");
        DashboardCharactersHintTextBlock.Text = T("Dashboard.ClickACharacterToManage");

        // Quick actions: the card's title and one line of what it does; it is its own Narrator name.
        DashboardQuickActionsTitleTextBlock.Text = T("Dashboard.QuickActions");
        SetCard(DashboardBrowseOnlineButton, T("Dashboard.BrowseOnline"), T("Dashboard.BrowseOnlineHint"));
        SetCard(DashboardCheckUpdatesButton, T("Dashboard.CheckModUpdates"), T("Dashboard.CheckModUpdatesHint"));
        SetCard(DashboardRunLauncherButton, T("Dashboard.RunLauncher"), T("Dashboard.RunLauncherHint"));

        // Paths.
        DashboardPathsExpander.Header = T("RefinedWorkspace.CurrentRepositoryPaths");
        SetCard(SourcePathCard, T("Localization.ModStorage"), null);
        SetCard(TargetPathCard, T("Localization.TargetFolder"), null);
        SetCard(LauncherPathCard, T("Common.Launcher"), null);
        AutomationProperties.SetName(SourceTextBox, T("Localization.ModStorage"));
        AutomationProperties.SetName(TargetTextBox, T("Localization.TargetFolder"));
        AutomationProperties.SetName(LauncherTextBox, T("Common.Launcher"));
        PickSourceButton.Content = T("Localization.Browse");
        PickTargetButton.Content = T("Localization.Browse");
        PickLauncherButton.Content = T("Localization.BrowseEXE");
        OpenSourceButton.Content = T("Localization.OpenModStorage");
        OpenTargetButton.Content = T("Localization.OpenTargetFolder");
        OpenLauncherButton.Content = T("Localization.OpenLauncherFolder");
        LauncherTextBox.PlaceholderText = T("Localization.SelectXXMILauncherExeOrAnother");
        SetCard(LinkDeploymentSettingsCard, T("Localization.DeployModsWithDirectoryJunctions"), T("Localization.LinkModeSharesRepositoryFilesLinking"));
        LinkDeploymentToggleSwitch.OnContent = T("Localization.Link");
        LinkDeploymentToggleSwitch.OffContent = T("Localization.Copy");
        AutomationProperties.SetName(LinkDeploymentToggleSwitch, T("Localization.DeployModsWithDirectoryJunctions"));
        DashboardPathsInfoBar.Title = T("Dashboard.ModFolderNotSet");
        DashboardSetPathsButton.Content = T("Dashboard.SetPaths");
        RefreshPathsHeader();
        RefreshBridgeButton();
        RefreshPathsState();
        OnRefreshed();
        UpdateDashboardLayout();
    }

    // The header is also the card's Narrator name.
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
            case nameof(DashboardViewModel.CurrentRepositoryName):
                RefreshPathsHeader();
                RefreshPathsState();
                break;
            case nameof(DashboardViewModel.BridgeLabel):
                RefreshBridgeButton();
                break;
            case nameof(DashboardViewModel.GettingStarted):
                ShowGettingStartedTip();
                break;
        }
    }

    // The window refreshed the cards: the repository switcher and the "New repository" menu follow them.
    private void OnRefreshed()
    {
        RefreshRepositorySwitcher();
        RefreshNewRepositoryMenu();
    }

    // The current repository's name opens a menu of every repository, the current one checked.
    private void RefreshRepositorySwitcher()
    {
        RepositoryCardViewModel? current = ViewModel.Repositories.FirstOrDefault(card => card.IsCurrent);
        DashboardCurrentRepositoryNameTextBlock.Text = current?.Name ?? T("Common.NoRepositorySelected");
        ToolTipService.SetToolTip(DashboardCurrentRepositoryNameTextBlock, current?.Name);
        DashboardRepositoryMenu.Items.Clear();
        foreach (RepositoryCardViewModel repository in ViewModel.Repositories)
        {
            var item = new ToggleMenuFlyoutItem { Text = repository.Name, IsChecked = repository.IsCurrent };
            string id = repository.Id;
            item.Click += (_, _) => ViewModel.Request(DashboardAction.SwitchRepository, id);
            DashboardRepositoryMenu.Items.Add(item);
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
        DashboardPathsExpander.Description = T("RefinedWorkspace.ThesePathsBelongOnlyToSwitching", repositoryName);
    }

    private void RefreshBridgeButton()
    {
        SetCard(BridgeOptionsButton, ViewModel.BridgeLabel, null);
        ToolTipService.SetToolTip(BridgeOptionsButton, T("EndfieldBridge.AppliesOnlyToThisEndfieldRepository"));
    }

    // Each path's icon says whether the folder (or the launcher file) exists; the expander's header says how many
    // are set. A missing Mod folder or game folder opens the paths once per repository, and a missing Mod folder
    // also shows the warning at the top.
    private void RefreshPathsState()
    {
        foreach ((TextBox box, FontIcon icon, bool expectFile) in _pathStatusIcons)
        {
            PathFieldStatus status = PathFieldStatuses.Evaluate(box.Text, expectFile);
            bool ready = status == PathFieldStatus.Ready;
            icon.Glyph = status == PathFieldStatus.Empty && expectFile ? "" : ready ? "" : "";
            icon.Foreground = status == PathFieldStatus.Empty && expectFile
                ? ThemeBrush("AppSecondaryDefaultForegroundBrush")
                : ThemeBrush(ready ? "AppSuccessBrush" : "AppDangerBrush");
            string tip = status switch
            {
                PathFieldStatus.Ready => T("Dashboard.PathFound"),
                PathFieldStatus.Missing => T("Dashboard.PathNotFound"),
                _ => T("Dashboard.PathNotSet")
            };
            ToolTipService.SetToolTip(icon, tip);
            AutomationProperties.SetName(icon, tip);
        }

        int missing = Paths.MissingRequiredCount;
        DashboardPathsSummaryTextBlock.Text = missing > 0 ? T("Dashboard.PathsMissing", missing) : T("Dashboard.PathsReady", Paths.ReadyCount);
        DashboardPathsSummaryTextBlock.Foreground = ThemeBrush(missing > 0 ? "AppDangerBrush" : "AppSecondaryDefaultForegroundBrush");
        bool hasRepository = !string.IsNullOrEmpty(ViewModel.CurrentRepositoryName);
        DashboardPathsInfoBar.IsOpen = hasRepository && Paths.SourceStatus != PathFieldStatus.Ready;
        // Opened only once the page is on screen: opening the expander before it is loaded leaves the window blank.
        if (hasRepository && missing > 0 && IsLoaded && _autoExpandedFor != ViewModel.CurrentRepositoryName)
        {
            _autoExpandedFor = ViewModel.CurrentRepositoryName;
            DashboardPathsExpander.IsExpanded = true;
        }
    }

    private void OnSetPathsClicked(object sender, RoutedEventArgs e)
    {
        DashboardPathsExpander.IsExpanded = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            DashboardPathsExpander.StartBringIntoView();
            SourceTextBox.Focus(FocusState.Programmatic);
        });
    }

    // Clicks the window answers.
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

    // Layout: quick actions in a row and characters beside the repositories on wide pages, stacked on narrow ones.
    private double PageWidth()
        => DashboardScrollViewer.ActualWidth > 0 ? DashboardScrollViewer.ActualWidth : Math.Max(420, (XamlRoot?.Size.Width ?? 0) - 240);

    private void UpdateDashboardLayout()
    {
        double available = PageWidth();
        bool twoColumns = DashboardLayoutPolicy.UsesTwoColumns(available);
        bool oneRowStrip = DashboardLayoutPolicy.StripFitsOneRow(available);
        int mode = (twoColumns ? 2 : 0) + (oneRowStrip ? 1 : 0);
        if (_layoutMode == mode)
        {
            return;
        }
        _layoutMode = mode;

        Arrange(DashboardQuickActionsGrid, twoColumns ? 3 : 1);
        DashboardMainGrid.ColumnDefinitions.Clear();
        DashboardMainGrid.RowDefinitions.Clear();
        if (twoColumns)
        {
            DashboardMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            DashboardMainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            DashboardMainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DashboardMainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Place(DashboardCharactersCard, 0, 0);
        Place(DashboardRepositoriesCard, twoColumns ? 0 : 1, twoColumns ? 1 : 0);

        // On narrow pages the actions move under the repository's name.
        Grid.SetRow(DashboardStripActions, oneRowStrip ? 0 : 1);
        Grid.SetColumn(DashboardStripActions, oneRowStrip ? 2 : 1);
        DashboardStripGrid.RowSpacing = oneRowStrip ? 0 : 12;
        DashboardStripActions.HorizontalAlignment = oneRowStrip ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private static void Place(FrameworkElement child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
    }

    // Equal columns, filled row by row.
    private static void Arrange(Grid grid, int columns)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
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
            var child = (FrameworkElement)grid.Children[slot];
            Grid.SetRow(child, slot / columns);
            Grid.SetColumn(child, slot % columns);
            // Cards in one row take the height of the tallest.
            child.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    /// <summary>Opens the getting-started tip at the current step, or closes it when there is nothing to show.</summary>
    private void ShowGettingStartedTip()
    {
        if (ViewModel.GettingStarted is not { } tips || !tips.ShouldShow(IsLoaded, _services.Dialogs.IsDialogOpen))
        {
            GettingStartedTip.IsOpen = false;
            return;
        }

        (FrameworkElement target, string title, string body) = tips.Current switch
        {
            GettingStartedStep.NewRepository => ((FrameworkElement)AddRepositoryButton, T("GettingStarted.NewRepository.Title"), T("GettingStarted.NewRepository.Body")),
            GettingStartedStep.Paths => (DashboardPathsExpander, T("GettingStarted.Paths.Title"), T("GettingStarted.Paths.Body")),
            _ => (DashboardOpenRepositoryButton, T("GettingStarted.OpenMods.Title"), T("GettingStarted.OpenMods.Body"))
        };
        // Moving to another target: close first, so the tip opens again beside the new one.
        GettingStartedTip.IsOpen = false;
        GettingStartedTip.Title = title;
        GettingStartedTip.Subtitle = body;
        GettingStartedStepTextBlock.Text = T("GettingStarted.Step", tips.Number, GettingStartedTips.Steps.Count);
        GettingStartedSkipButton.Content = T("GettingStarted.Skip");
        GettingStartedNextButton.Content = tips.IsLast ? T("GettingStarted.Done") : T("GettingStarted.Next");
        AutomationProperties.SetName(GettingStartedTip, title);
        AutomationProperties.SetHelpText(GettingStartedTip, body);
        // The Next button is focused, so Narrator reads the step with it.
        string announcement = T("GettingStarted.Announcement", title, body);
        AutomationProperties.SetHelpText(GettingStartedNextButton, announcement);
        // The tip is placed against where its target is when it opens. So the page scrolls the target into view
        // first, without animation, the layout is brought up to date, and only then is the tip placed and opened.
        // Step 2 used to open while the page was still moving to the path settings, beside where they had been.
        int version = ++_gettingStartedTipVersion;
        bool belowLeft = tips.Current == GettingStartedStep.NewRepository;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _gettingStartedTipVersion || ViewModel.GettingStarted?.IsPending != true || XamlRoot is null) return;
            UpdateLayout();
            double top = target.TransformToVisual(DashboardRootPanel).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            // The target sits at least a tip's height below the top, so a tall one can be pointed at from above.
            double lead = Math.Max(DashboardScrollViewer.ViewportHeight * 0.3, GettingStartedTipHeight + 24);
            double offset = Math.Clamp(top - lead, 0, DashboardScrollViewer.ScrollableHeight);
            if (Math.Abs(offset - DashboardScrollViewer.VerticalOffset) >= 1)
            {
                DashboardScrollViewer.ChangeView(null, offset, null, disableAnimation: true);
                UpdateLayout();
            }
            PlaceGettingStartedTip(target, belowLeft);
            GettingStartedTip.IsOpen = true;
            _ = FocusGettingStartedTipAsync(announcement);
        });
    }

    // Below or above the target when the tip fits there, otherwise in the middle of the page pointing at nothing;
    // never beside a wide target, where it used to leave the window (J8 follow-up).
    private void PlaceGettingStartedTip(FrameworkElement target, bool belowLeft)
    {
        Windows.Foundation.Rect bounds = target.TransformToVisual(null)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, target.ActualWidth, target.ActualHeight));
        TipPlacement placement = TipPlacementPolicy.Choose(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            XamlRoot.Size.Width, XamlRoot.Size.Height, GettingStartedTipWidth, GettingStartedTipHeight, belowLeft);
        GettingStartedTip.Target = placement == TipPlacement.Center ? null : target;
        GettingStartedTip.PreferredPlacement = placement switch
        {
            TipPlacement.BottomLeft => TeachingTipPlacementMode.BottomLeft,
            TipPlacement.Bottom => TeachingTipPlacementMode.Bottom,
            TipPlacement.Top => TeachingTipPlacementMode.Top,
            _ => TeachingTipPlacementMode.Center
        };
    }

    // A teaching tip is at most 336 wide; with its tail and the longest (Russian) step text it stays within these.
    private const double GettingStartedTipWidth = 360;
    private const double GettingStartedTipHeight = 300;

    // The tip's popup is laid out a moment after it opens; until then its buttons cannot take the focus.
    private async Task FocusGettingStartedTipAsync(string announcement)
    {
        for (int attempt = 0; attempt < 20 && GettingStartedTip.IsOpen; attempt++)
        {
            await Task.Delay(100);
            if (GettingStartedNextButton.XamlRoot is not null && GettingStartedNextButton.Focus(FocusState.Programmatic)) break;
        }
        if (!GettingStartedTip.IsOpen) return;
        FrameworkElementAutomationPeer.FromElement(GettingStartedTip)?.RaiseNotificationEvent(AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent, announcement, "GettingStartedTip");
    }

    private void OnGettingStartedNextClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.GettingStarted is not { } tips) return;
        tips.Next();
        if (tips.IsPending) ShowGettingStartedTip();
        else FinishGettingStarted();
    }

    private void OnGettingStartedSkipClicked(object sender, RoutedEventArgs e) => CloseGettingStarted();

    private void OnGettingStartedTipKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        CloseGettingStarted();
    }

    // The tip's own close button (X); the page's own closes are programmatic and keep the tips.
    private void OnGettingStartedTipClosed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        if (args.Reason != TeachingTipCloseReason.Programmatic && ViewModel.GettingStarted is not null) CloseGettingStarted();
    }

    private void CloseGettingStarted()
    {
        ViewModel.GettingStarted?.Close();
        FinishGettingStarted();
    }

    private void FinishGettingStarted()
    {
        GettingStartedTip.IsOpen = false;
        ViewModel.Request(DashboardAction.GettingStartedTipsClosed);
    }
}
