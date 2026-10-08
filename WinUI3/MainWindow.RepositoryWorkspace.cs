using System.Collections.ObjectModel;
using System.Collections.Specialized;
using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private bool _repositoryV4Ready;
    private bool _syncingRepositorySelection;
    private bool _repositoryRefreshQueued;
    private bool _repositoryCoverLayoutQueued;
    private bool _refreshingRepositoryPicker;
    private bool _refreshingCombinationWorkspace;
    private bool _repositoryDetailDialogOpen;
    private bool _repositoryWorkspaceBusy;
    private SecondLevelFolderItem? _repositorySelectedMod;
    private RepositoryWorkspaceView _repositoryWorkspaceView;
    private readonly BatchObservableCollection<SecondLevelFolderItem> _repositoryVisibleMods = [];
    private int _repositoryCatalogRevision;
    private int _repositoryViewRevision = -1;
    private readonly HashSet<SecondLevelFolderItem> _repositoryCoversLoading = [];
    private Grid? _repositoryToolbar;
    private Grid? _repositoryHeader;
    private Grid? _repositoryToolbarCommands;
    private StackPanel? _repositoryViewButtons;
    private ToggleButton[] _repositoryModeButtons = [];
    private ComboBox? _repositoryWorkspacePicker;
    private TextBlock? _repositoryCountText;
    private TextBlock? _repositoryCategoryTitle;
    private TextBlock? _repositoryModTitle;
    private TextBlock? _repositoryModCount;
    private TextBox? _repositoryModSearch;
    private TextBlock? _repositoryEmptyMessage;
    private ScrollViewer? _repositoryCoverGrid;
    private ItemsRepeater? _repositoryCoverRepeater;
    private UniformGridLayout? _repositoryCoverWrap;
    private Button? _repositoryOpenDetails;
    private Button? _repositoryOpenModFolder;
    private Button? _repositoryOpenPresets;
    private TextBlock? _repositorySelectedPath;
    private TextBlock? _repositoryDeploymentHint;
    private TextBlock? _repositoryTaskStatusText;
    private FontIcon? _repositoryTaskStatusIcon;
    private Grid? _combinationWorkspace;
    private ListView? _combinationPresetList;
    private StackPanel? _combinationMemberPanel;
    private TextBlock? _combinationWorkspaceTitle;
    private TextBlock? _combinationMemberTitle;
    private TextBlock? _combinationRecoveryTitle;
    private TextBlock? _combinationRecoveryHint;
    private TextBlock? _combinationNoProfiles;
    private Border? _combinationRecoveryCard;

    // Reuse the real controls and handlers. No parallel deployment or persistence implementation.
    private void InitializeRepositoryWorkspaceV4()
    {
        if (_workspacePageHost is null || _workspaceWorkbench is null || _workspaceDetailHost is null
            || _workspaceContentGrid is null || _categoryCard is null || _modCard is null) return;

        _repositoryCategoryTitle = RepositoryText(15, true);
        _repositoryModTitle = RepositoryText(16, true);
        _repositoryModCount = RepositoryText(12);
        _repositoryModSearch = new TextBox { Height = 36, MinWidth = 0 };
        _repositoryModSearch.TextChanged += (_, _) => RefreshRepositoryModView();
        AutomationProperties.SetName(_repositoryModSearch, L("搜索 Mod", "Search Mods"));
        var categoryGrid = RepositoryRows(GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star));
        var categoryHeading = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
        categoryHeading.Children.Add(_repositoryCategoryTitle);
        RepositoryMove(CreateFirstLevelButton, categoryHeading, 0, 1);
        RepositoryMove(RenameFirstLevelButton, categoryHeading, 0, 2);
        foreach (var button in new[] { CreateFirstLevelButton, RenameFirstLevelButton })
        { button.Width = 32; button.Height = 32; button.MinHeight = 32; button.Padding = new Thickness(6); }
        RepositoryDecorateButton(CreateFirstLevelButton, "\uE710", true);
        RepositoryDecorateButton(RenameFirstLevelButton, "\uE70F", true);
        categoryGrid.Children.Add(categoryHeading);
        RepositoryMove(FirstLevelSearchTextBox, categoryGrid, 1);
        FirstLevelSearchTextBox.Padding = new Thickness(8, 6, 8, 6);
        FirstLevelSearchTextBox.Height = 34;
        FirstLevelSearchTextBox.MinWidth = 0;
        RepositoryMove(FirstLevelListView, categoryGrid, 2);
        FirstLevelListView.Height = double.NaN;
        FirstLevelListView.MinHeight = 0;
        _categoryCard.Child = categoryGrid;
        _categoryCard.Padding = new Thickness(12);
        _categoryCard.CornerRadius = new CornerRadius(6);

        var modGrid = RepositoryRows(GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        var modHeading = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
        modHeading.Children.Add(_repositoryModTitle);
        Grid.SetColumn(_repositoryModCount, 1); modHeading.Children.Add(_repositoryModCount);
        RepositoryMove(DeleteSecondLevelButton, modHeading, 0, 2);
        DeleteSecondLevelButton.Width = 32; DeleteSecondLevelButton.Height = 32;
        DeleteSecondLevelButton.MinHeight = 32; DeleteSecondLevelButton.Padding = new Thickness(6);
        RepositoryDecorateButton(DeleteSecondLevelButton, "\uE74D", true);
        modGrid.Children.Add(modHeading);
        Grid.SetRow(_repositoryModSearch, 1); modGrid.Children.Add(_repositoryModSearch);

        _repositoryCoverWrap = new UniformGridLayout
        {
            // Fill each row left-to-right; the enclosing viewer scrolls vertically.
            Orientation = Orientation.Horizontal, MinItemWidth = 236, MinItemHeight = 240,
            ItemsStretch = UniformGridLayoutItemsStretch.Fill,
            ItemsJustification = UniformGridLayoutItemsJustification.Start,
            MinRowSpacing = 8, MinColumnSpacing = 8
        };
        _repositoryCoverRepeater = new ItemsRepeater
        {
            ItemsSource = _repositoryVisibleMods,
            ItemTemplate = (DataTemplate)RootGrid.Resources["RepositoryCoverTemplate"],
            Layout = _repositoryCoverWrap,
            // A finite positive width is required before the first measure pass.
            Width = 236,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _repositoryCoverRepeater.ElementPrepared += (_, args) =>
        {
            if (_repositoryVisibleMods.ElementAtOrDefault(args.Index) is { } mod)
            {
                if (args.Element is FrameworkElement element) element.DataContext = mod;
                LoadRepositoryCoverV4(mod);
            }
        };
        _repositoryCoverGrid = new ScrollViewer
        {
            Content = _repositoryCoverRepeater,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _repositoryCoverGrid.Loaded += (_, _) =>
        {
            QueueRepositoryCoverLayoutV4();
        };
        _repositoryCoverGrid.SizeChanged += (_, _) =>
        {
            QueueRepositoryCoverLayoutV4();
        };
        SecondLevelListView.Height = double.NaN;
        SecondLevelListView.MinHeight = 0;
        SecondLevelListView.ItemsSource = _repositoryVisibleMods;
        var modViews = new Grid();
        RepositoryMove(SecondLevelListView, modViews);
        modViews.Children.Add(_repositoryCoverGrid);
        _repositoryEmptyMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16), IsHitTestVisible = false, Opacity = 0.7
        };
        modViews.Children.Add(_repositoryEmptyMessage);
        SecondLevelDropBorder.Child = modViews;
        SecondLevelDropBorder.Padding = new Thickness(0);
        RepositoryMove(SecondLevelDropBorder, modGrid, 2);
        _repositoryOpenPresets = RepositoryButton("\uE8F1", (_, _) => SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView.Presets));
        Grid.SetRow(_repositoryOpenPresets, 3);
        _repositoryOpenPresets.HorizontalAlignment = HorizontalAlignment.Stretch;
        // The same entry is always available in the top view switcher.
        _repositoryOpenPresets.Visibility = Visibility.Collapsed;
        modGrid.Children.Add(_repositoryOpenPresets);
        _modCard.Child = modGrid;
        _modCard.Padding = new Thickness(12);
        _modCard.CornerRadius = new CornerRadius(6);

        InitializeRepositoryInspectorV4();
        InitializeCombinationWorkspaceV4();
        InitializeRepositoryToolbarV4();
        // The repository is initially hidden on a clean install. Its first real
        // viewport is measured only when the user opens it, not when RootGrid loads.
        WorkspaceScrollViewer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WorkspaceScrollViewer.VerticalContentAlignment = VerticalAlignment.Stretch;
        WorkspaceScrollViewer.SizeChanged += (_, _) => UpdateRepositoryWorkspaceLayoutV4();
        _secondLevelItems.CollectionChanged += OnRepositoryCatalogChanged;
        _repositoryV4Ready = true;
        RootGrid.ActualThemeChanged += (_, _) => UpdateRepositoryWorkspaceLanguageV4();
        UpdateRepositoryWorkspaceLanguageV4();
        RefreshRepositoryModView();
        SetRepositoryWorkspaceViewV4(_repositoryWorkspaceView, save: false);
    }

    private void InitializeRepositoryToolbarV4()
    {
        _repositoryToolbar = RepositoryRows(GridLength.Auto, GridLength.Auto);
        _repositoryHeader = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
        var title = new StackPanel { Spacing = 12, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        RepositoryDetach(_workspaceTitleText!);
        _workspaceTitleText!.FontSize = 22;
        title.Children.Add(_workspaceTitleText);
        _repositoryWorkspacePicker = new ComboBox { MinWidth = 190, MaxWidth = 300, MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left };
        _repositoryWorkspacePicker.SelectionChanged += (_, _) =>
        {
            if (_refreshingRepositoryPicker || _repositoryWorkspaceBusy) return;
            if (_repositoryWorkspacePicker.SelectedItem is ComboBoxItem { Tag: string id }
                && !string.Equals(id, _selectedRepositoryId, StringComparison.Ordinal))
                SwitchRepository(id, openManager: true);
        };
        title.Children.Add(_repositoryWorkspacePicker);
        _repositoryHeader.Children.Add(title);
        _repositoryToolbarCommands = RepositoryColumns(GridLength.Auto, GridLength.Auto, GridLength.Auto);
        _repositoryToolbarCommands.HorizontalAlignment = HorizontalAlignment.Right;
        _repositoryToolbarCommands.VerticalAlignment = VerticalAlignment.Center;
        RepositoryMove(RefreshButton, _repositoryToolbarCommands);
        RepositoryMove(ImportZipButton, _repositoryToolbarCommands, 0, 1);
        RepositoryMove(RunLauncherButton, _repositoryToolbarCommands, 0, 2);
        foreach (var button in new[] { RefreshButton, ImportZipButton, RunLauncherButton })
        { button.HorizontalAlignment = HorizontalAlignment.Left; button.MinHeight = 36; }
        RepositoryDecorateButton(RefreshButton, "\uE72C", true);
        RepositoryDecorateButton(ImportZipButton, "\uE8E5");
        RepositoryDecorateButton(RunLauncherButton, "\uE768");
        Grid.SetColumn(_repositoryToolbarCommands, 1); _repositoryHeader.Children.Add(_repositoryToolbarCommands);
        _repositoryToolbar.Children.Add(_repositoryHeader);

        var switchRow = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
        _repositoryViewButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _repositoryModeButtons = Enumerable.Range(0, 3).Select(index =>
        {
            var button = new ToggleButton { MinHeight = 34, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(4), Tag = (RepositoryWorkspaceView)index };
            button.Click += (_, _) => SetRepositoryWorkspaceViewV4((RepositoryWorkspaceView)button.Tag);
            _repositoryViewButtons.Children.Add(button);
            return button;
        }).ToArray();
        _repositoryOpenDetails = RepositoryButton("\uE8A5", async (_, _) => await ShowRepositoryDetailsV4Async());
        _repositoryViewButtons.Children.Add(_repositoryOpenDetails);
        switchRow.Children.Add(_repositoryViewButtons);
        _repositoryCountText = RepositoryText(12);
        Grid.SetColumn(_repositoryCountText, 1); switchRow.Children.Add(_repositoryCountText);
        Grid.SetRow(switchRow, 1); _repositoryToolbar.Children.Add(switchRow);
        var toolbar = RepositoryCard(_repositoryToolbar);
        toolbar.Style = (Style)Application.Current.Resources["CommandSurfaceBorderStyle"];
        toolbar.Padding = new Thickness(14, 10, 14, 10);
        _workspacePageHost!.Children.RemoveAt(0);
        _workspacePageHost.Children.Insert(0, toolbar);

        InitializeRepositoryStatusV4();
        FirstCountTextBlock.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => RefreshRepositoryCountsV4());
        SecondCountTextBlock.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => RefreshRepositoryCountsV4());
    }

    private void InitializeRepositoryStatusV4()
    {
        foreach (var element in new FrameworkElement[] { CopyProgressBar, ProgressTextBlock, StatusTextBlock, AuthorTextBlock })
            RepositoryDetach(element);

        var footer = RepositoryRows(GridLength.Auto, GridLength.Auto);
        footer.RowSpacing = 3;
        var line = RepositoryColumns(GridLength.Auto, GridLength.Auto,
            new GridLength(1, GridUnitType.Star), GridLength.Auto);
        line.MinHeight = 22;
        line.ColumnSpacing = 12;
        var task = RepositoryColumns(GridLength.Auto, GridLength.Auto);
        task.ColumnSpacing = 7;
        _repositoryTaskStatusIcon = new FontIcon { Glyph = "\uE73E", FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        _repositoryTaskStatusText = RepositoryText(12, true);
        _repositoryTaskStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
        _repositoryTaskStatusText.TextWrapping = TextWrapping.NoWrap;
        task.Children.Add(_repositoryTaskStatusIcon);
        Grid.SetColumn(_repositoryTaskStatusText, 1); task.Children.Add(_repositoryTaskStatusText);
        line.Children.Add(task);

        var separator = RepositoryText(13);
        separator.Text = "|";
        separator.Opacity = .3;
        Grid.SetColumn(separator, 1); line.Children.Add(separator);
        StatusTextBlock.FontSize = 13;
        StatusTextBlock.TextWrapping = TextWrapping.NoWrap;
        StatusTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        StatusTextBlock.VerticalAlignment = VerticalAlignment.Center;
        StatusTextBlock.IsTextSelectionEnabled = true;
        Grid.SetRow(StatusTextBlock, 0); Grid.SetColumn(StatusTextBlock, 2); line.Children.Add(StatusTextBlock);
        AuthorTextBlock.FontSize = 12;
        AuthorTextBlock.Opacity = .7;
        Grid.SetRow(AuthorTextBlock, 0); Grid.SetColumn(AuthorTextBlock, 3); line.Children.Add(AuthorTextBlock);
        footer.Children.Add(line);

        CopyProgressBar.Height = 2;
        CopyProgressBar.MinHeight = 0;
        Grid.SetRow(CopyProgressBar, 1); footer.Children.Add(CopyProgressBar);
        // Keep the existing progress source and handlers, but display a compact idle label.
        ProgressTextBlock.Visibility = Visibility.Collapsed;
        footer.Children.Add(ProgressTextBlock);
        WorkspaceStatusCard.Child = footer;
        WorkspaceStatusCard.Padding = new Thickness(12, 6, 12, 6);
        WorkspaceStatusCard.CornerRadius = new CornerRadius(4);
        ProgressTextBlock.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => RefreshRepositoryStatusV4());
        StatusTextBlock.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => RefreshRepositoryStatusV4());
        CopyProgressBar.RegisterPropertyChangedCallback(RangeBase.ValueProperty, (_, _) => RefreshRepositoryStatusV4());
        RefreshRepositoryStatusV4();
    }

    private void RefreshRepositoryStatusV4()
    {
        if (_repositoryTaskStatusText is null || _repositoryTaskStatusIcon is null) return;
        string message = ProgressTextBlock.Text ?? string.Empty;
        bool noTask = string.IsNullOrWhiteSpace(message)
            || message == "当前无复制任务" || message == "No active copy task";
        _repositoryTaskStatusText.Text = noTask
            ? (_repositoryWorkspaceBusy ? L("正在处理", "Working") : L("空闲", "Idle"))
            : message;
        _repositoryTaskStatusIcon.Glyph = _repositoryWorkspaceBusy ? "\uE895" : "\uE73E";
        ToolTipService.SetToolTip(_repositoryTaskStatusText, message);
        ToolTipService.SetToolTip(StatusTextBlock, StatusTextBlock.Text);
        CopyProgressBar.IsIndeterminate = _repositoryWorkspaceBusy && CopyProgressBar.Value <= 0;
        CopyProgressBar.Visibility = _repositoryWorkspaceBusy ? Visibility.Visible : Visibility.Collapsed;
        double width = WorkspaceScrollViewer.ActualWidth;
        _repositoryTaskStatusText.MaxWidth = width > 0 && width < 620 ? 140 : 300;
        AuthorTextBlock.Visibility = width > 0 && width < 620 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void InitializeRepositoryInspectorV4()
    {
        _workspaceDetailHost!.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workspaceDetailHost.RowSpacing = 8;
        if (_workspaceDetailHost.Children[0] is Border summary)
        {
            summary.Style = (Style)Application.Current.Resources["CardBorderStyle"];
            summary.CornerRadius = new CornerRadius(6); summary.Padding = new Thickness(12);
            _modernFolderText!.FontSize = 18;
            _modernStateText!.FontSize = 12;
        }
        _previewCard!.Padding = new Thickness(12);
        PreviewSectionTitleTextBlock.Visibility = Visibility.Collapsed;
        PreviewSectionSubtitleTextBlock.Visibility = Visibility.Collapsed;
        ModLinkSectionSubtitleTextBlock.Visibility = Visibility.Collapsed;
        PreviewFooterTextBlock.Visibility = Visibility.Collapsed;
        PreviewDropBorder.Height = double.NaN;
        PreviewDropBorder.MinHeight = 130;
        PreviewDropBorder.CornerRadius = new CornerRadius(4);
        if (_previewCard.Child is Grid previewGrid) previewGrid.RowSpacing = 8;
        _repositorySelectedPath = RepositoryText(12);
        _repositorySelectedPath.MaxLines = 1;
        _repositorySelectedPath.TextTrimming = TextTrimming.CharacterEllipsis;
        _repositoryOpenModFolder = RepositoryButton("\uE8B7", (_, _) =>
        {
            if (GetSelectedSecondLevelItem() is { } mod) OpenDirectory(mod.Path, L("Mod 文件夹", "Mod folder"));
        });
        RepositoryDecorateButton(OpenModLinkButton, "\uE774");
        var detailActions = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
        RepositoryMove(_repositoryOpenModFolder, detailActions);
        RepositoryMove(OpenModLinkButton, detailActions, 0, 1);
        if (_previewCard.Child is Grid preview)
        {
            Grid.SetRow(detailActions, 5); preview.Children.Add(detailActions);
        }
        _repositoryDeploymentHint = RepositoryText(12);
        _repositoryDeploymentHint.TextWrapping = TextWrapping.Wrap;
        var footer = RepositoryRows(GridLength.Auto, GridLength.Auto, GridLength.Auto);
        footer.RowSpacing = 7;
        footer.Children.Add(_repositorySelectedPath);
        Grid.SetRow(_repositoryDeploymentHint, 1); footer.Children.Add(_repositoryDeploymentHint);
        RepositoryMove(ToggleCopyButton, footer, 2);
        ToggleCopyButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToggleCopyButton.MinHeight = 38;
        RepositoryDecorateButton(ToggleCopyButton, "\uE71B");
        var footerCard = RepositoryCard(footer);
        footerCard.Padding = new Thickness(12);
        Grid.SetRow(footerCard, 2); _workspaceDetailHost.Children.Add(footerCard);
        _shortcutCard!.Padding = new Thickness(12);
        ShortcutSectionSubtitleTextBlock.Visibility = Visibility.Collapsed;
        ShortcutHintTextBlock.Visibility = Visibility.Collapsed;
        _previewTab!.Header = L("预览", "Preview");
    }

    private void InitializeCombinationWorkspaceV4()
    {
        _combinationWorkspace = new Grid { ColumnSpacing = 12, RowSpacing = 12, Visibility = Visibility.Collapsed };
        _combinationPresetList = new ListView { SelectionMode = ListViewSelectionMode.Single, SingleSelectionFollowsFocus = false, MinHeight = 0 };
        _combinationPresetList.SelectionChanged += (_, _) =>
        {
            if (_refreshingCombinationWorkspace || _repositoryWorkspaceBusy) return;
            if (_combinationPresetList.SelectedItem is ListViewItem { Tag: string id })
                ConfigurationProfileComboBox.SelectedItem = ConfigurationProfileComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == id);
        };
        var list = RepositoryRows(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        _combinationWorkspaceTitle = RepositoryText(16, true); list.Children.Add(_combinationWorkspaceTitle);
        Grid.SetRow(_combinationPresetList, 1); list.Children.Add(_combinationPresetList);
        RepositoryMove(CreateConfigurationProfileButton, list, 2);
        CreateConfigurationProfileButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        RepositoryDecorateButton(CreateConfigurationProfileButton, "\uE710");
        InitializeCombinationBundleActions();
        var presetCard = RepositoryCard(list); Grid.SetRow(presetCard, 1);
        _combinationWorkspace.Children.Add(presetCard);

        var member = RepositoryRows(GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star));
        var heading = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
        _combinationMemberTitle = RepositoryText(17, true); heading.Children.Add(_combinationMemberTitle);
        RepositoryMove(UpdateConfigurationProfileButton, heading, 0, 1);
        RepositoryMove(DeleteConfigurationProfileButton, heading, 0, 2);
        RepositoryDecorateButton(UpdateConfigurationProfileButton, "\uE74E", true);
        RepositoryDecorateButton(DeleteConfigurationProfileButton, "\uE74D", true);
        member.Children.Add(heading);
        RepositoryMove(ConfigurationProfileSummaryTextBlock, member, 1);
        _combinationMemberPanel = new StackPanel { Spacing = 8 };
        var members = new ScrollViewer
        {
            Content = _combinationMemberPanel, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(members, 2); member.Children.Add(members);
        var memberCard = RepositoryCard(member); Grid.SetColumn(memberCard, 1); Grid.SetRow(memberCard, 1); _combinationWorkspace.Children.Add(memberCard);

        var recovery = RepositoryRows(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        _combinationRecoveryTitle = RepositoryText(16, true); recovery.Children.Add(_combinationRecoveryTitle);
        var recoveryBody = new StackPanel { Spacing = 10 };
        _combinationRecoveryHint = RepositoryText(12); _combinationRecoveryHint.TextWrapping = TextWrapping.Wrap;
        recoveryBody.Children.Add(_combinationRecoveryHint);
        RepositoryMove(CaptureCombinationStateCheckBox, recoveryBody);
        var path = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
        RepositoryMove(CombinationUserIniTextBox, path);
        CombinationUserIniTextBox.PlaceholderText = "d3dx_user.ini";
        RepositoryMove(BrowseCombinationUserIniButton, path, 0, 1);
        RepositoryDecorateButton(BrowseCombinationUserIniButton, "\uE8B7", true);
        recoveryBody.Children.Add(path);
        _combinationNoProfiles = RepositoryText(12); _combinationNoProfiles.TextWrapping = TextWrapping.Wrap;
        recoveryBody.Children.Add(_combinationNoProfiles);
        var recoveryScroll = new ScrollViewer
        {
            Content = recoveryBody, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(recoveryScroll, 1); recovery.Children.Add(recoveryScroll);
        RepositoryMove(ApplyConfigurationProfileButton, recovery, 2);
        ApplyConfigurationProfileButton.MinHeight = 40; ApplyConfigurationProfileButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        RepositoryDecorateButton(ApplyConfigurationProfileButton, "\uE777");
        _combinationRecoveryCard = RepositoryCard(recovery);
        Grid.SetColumn(_combinationRecoveryCard, 2); Grid.SetRow(_combinationRecoveryCard, 1); _combinationWorkspace.Children.Add(_combinationRecoveryCard);

        // Keep the original selection control as the sole selected-profile authority.
        RepositoryDetach(ConfigurationProfileComboBox);
        ConfigurationProfileComboBox.Visibility = Visibility.Collapsed;
        list.Children.Add(ConfigurationProfileComboBox);
        var oldCard = UpdatesSupportPanel.Children.OfType<Border>().FirstOrDefault(card => card.Child is StackPanel panel
            && panel.Children.Contains(ConfigurationProfilesTitleTextBlock));
        if (oldCard is not null) UpdatesSupportPanel.Children.Remove(oldCard);
        var shortcut = RepositoryButton("\uE8F1", (_, _) => OnOpenCombinationPresetsClicked(this, new RoutedEventArgs()));
        shortcut.Content = RepositoryIconLabel("\uE8F1", L("打开组合预设工作区", "Open Combination Workspace"));
        UpdatesSupportPanel.Children.Insert(1, RepositoryCard(shortcut));
        _workspaceContentGrid!.Children.Add(_combinationWorkspace);
    }

    private void SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView view, bool save = true)
    {
        if (!_repositoryV4Ready || _repositoryWorkspaceBusy || _repositoryDetailDialogOpen) return;
        _repositoryWorkspaceView = view;
        _workspaceWorkbench!.Visibility = view == RepositoryWorkspaceView.Presets ? Visibility.Collapsed : Visibility.Visible;
        _combinationWorkspace!.Visibility = view == RepositoryWorkspaceView.Presets ? Visibility.Visible : Visibility.Collapsed;
        SecondLevelListView.Visibility = view == RepositoryWorkspaceView.Covers ? Visibility.Collapsed : Visibility.Visible;
        _repositoryCoverGrid!.Visibility = view == RepositoryWorkspaceView.Covers ? Visibility.Visible : Visibility.Collapsed;
        for (int index = 0; index < _repositoryModeButtons.Length; index++) _repositoryModeButtons[index].IsChecked = index == (int)view;
        UpdateRepositoryWorkspaceLanguageV4();
        SyncRepositorySelectionV4(GetSelectedSecondLevelItem());
        UpdateRepositoryWorkspaceLayoutV4();
        if (save) SaveShellConfig();
    }

    private void UpdateRepositoryWorkspaceLanguageV4()
    {
        if (!_repositoryV4Ready) return;
        _workspaceTitleText!.Text = _repositoryWorkspaceView == RepositoryWorkspaceView.Presets ? L("组合预设", "Combination Presets") : L("Mod 工作台", "Mod Workspace");
        _repositoryCategoryTitle!.Text = L("角色", "Characters");
        _repositoryModTitle!.Text = FirstLevelListView.SelectedItem is FirstLevelFolderItem role ? role.Name : L("Mod 文件", "Mod Files");
        _repositoryModSearch!.PlaceholderText = L("搜索当前角色的 Mod", "Search this character's Mods");
        AutomationProperties.SetName(_repositoryModSearch, L("搜索 Mod", "Search Mods"));
        FirstLevelSearchTextBox.PlaceholderText = L("搜索角色", "Search characters");
        string[] labels = [L("文件列表", "Files"), L("封面视图", "Covers"), L("组合预设", "Presets")];
        string[] glyphs = ["\uEA37", "\uE80A", "\uE8F1"];
        for (int index = 0; index < _repositoryModeButtons.Length; index++)
        {
            _repositoryModeButtons[index].Content = RepositoryIconLabel(glyphs[index], labels[index]);
            AutomationProperties.SetName(_repositoryModeButtons[index], labels[index]);
            foreach (string state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
            {
                _repositoryModeButtons[index].Resources["ToggleButtonBackground" + state] = GetAppThemeBrush("AppSecondarySelectedBrush");
                _repositoryModeButtons[index].Resources["ToggleButtonForeground" + state] = GetAppThemeBrush("AppSecondarySelectedForegroundBrush");
                _repositoryModeButtons[index].Resources["ToggleButtonBorderBrush" + state] = GetAppThemeBrush("AppNavSelectedBorderBrush");
            }
        }
        _repositoryOpenDetails!.Content = RepositoryIconLabel("\uE8A5", L("详情", "Details"));
        AutomationProperties.SetName(_repositoryOpenDetails, L("Mod 详情", "Mod Details"));
        _repositoryOpenPresets!.Content = RepositoryIconLabel("\uE8F1", L("组合与状态预设", "Combination & State Presets"));
        _repositoryOpenModFolder!.Content = RepositoryIconLabel("\uE8B7", L("打开文件夹", "Open Folder"));
        AutomationProperties.SetName(_repositoryOpenModFolder, L("打开 Mod 文件夹", "Open Mod Folder"));
        _previewTab!.Header = L("预览", "Preview");
        _selectionEyebrowText!.Text = L("当前 Mod", "CURRENT MOD");
        _modernFolderText!.Foreground = GetAppThemeBrush("AppNavDefaultForegroundBrush");
        _modernStateLabel!.Visibility = Visibility.Collapsed;
        _combinationWorkspaceTitle!.Text = L("我的预设", "My Presets");
        AutomationProperties.SetName(DeleteConfigurationProfileButton, L("删除预设", "Delete Preset"));
        ToolTipService.SetToolTip(DeleteConfigurationProfileButton, L("删除预设", "Delete Preset"));
        AutomationProperties.SetName(_repositoryWorkspacePicker!, L("当前仓库", "Current Repository"));
        _combinationRecoveryTitle!.Text = L("保存与恢复", "Save & Restore");
        _combinationRecoveryHint!.Text = L("1  退出游戏与加载器\n2  备份并恢复 Mod 组合\n3  回写已保存的持久参数", "1  Close the game and loader\n2  Back up and restore the Mod set\n3  Write saved persistent values");
        _combinationNoProfiles!.Text = L("只管理当前仓库的可识别部署。\n不改写 Mod INI；其他 Mod 的参数保持不变。\n\n保存参数前，请在游戏中按 F10 并等待保存完成。", "Only recognized deployments in this repository are managed.\nMod INIs and other Mods' values remain unchanged.\n\nBefore capturing values, press F10 in game and wait for the file to be saved.");
        foreach (var mod in _secondLevelItems) mod.CoverPlaceholder = L("未设置预览图", "No preview image");
        RefreshRepositoryPickerV4();
        RefreshRepositoryCountsV4();
        RefreshCombinationWorkspaceV4();
        UpdateRepositorySelectedMetadataV4();
    }

    private void UpdateRepositoryWorkspaceLayoutV4()
    {
        if (!_repositoryV4Ready) return;
        double width = WorkspaceScrollViewer.ActualWidth;
        if (width <= 0) width = Math.Max(320, Bounds.Width - 220);
        RefreshRepositoryStatusV4();
        var layout = RepositoryWorkspacePolicy.Layout(width);
        _workspaceWorkbench!.ColumnDefinitions.Clear();
        _workspaceWorkbench.RowDefinitions.Clear();
        _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.CategoryWidth) });
        _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _workspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.DetailWidth) });
        _workspaceWorkbench.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Place(_categoryCard!, 0, 0); Place(_modCard!, 0, 1); Place(_workspaceDetailHost!, 0, 2);
        if (!_repositoryDetailDialogOpen) _workspaceDetailHost!.Visibility = layout.InlineDetails ? Visibility.Visible : Visibility.Collapsed;
        _repositoryOpenDetails!.Visibility = layout.InlineDetails || _repositoryWorkspaceView == RepositoryWorkspaceView.Presets ? Visibility.Collapsed : Visibility.Visible;
        _workspaceContentGrid!.VerticalAlignment = VerticalAlignment.Stretch;
        double height = WorkspaceScrollViewer.ActualHeight;
        if (height > 0) _workspaceContentGrid.Height = height;
        _workspaceWorkbench.Height = double.NaN;
        FirstLevelListView.Height = double.NaN; SecondLevelListView.Height = double.NaN;
        _previewCard!.MinHeight = 0; _shortcutCard!.MinHeight = 0;
        UpdateRepositoryCoverLayoutV4();
        _repositoryHeader!.RowDefinitions.Clear();
        _repositoryHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _repositoryHeader.RowDefinitions.Add(new RowDefinition { Height = width < 720 ? GridLength.Auto : new GridLength(0) });
        _repositoryHeader.RowSpacing = width < 720 ? 8 : 0;
        Grid.SetRow(_repositoryToolbarCommands!, width < 720 ? 1 : 0);
        Grid.SetColumn(_repositoryToolbarCommands!, width < 720 ? 0 : 1);
        Grid.SetColumnSpan(_repositoryToolbarCommands!, width < 720 ? 2 : 1);
        _repositoryToolbarCommands!.HorizontalAlignment = width < 720 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _repositoryCountText!.Visibility = width < 620 ? Visibility.Collapsed : Visibility.Visible;
        _combinationWorkspace!.ColumnDefinitions.Clear(); _combinationWorkspace.RowDefinitions.Clear();
        bool inlineRecovery = RepositoryWorkspacePolicy.InlineCombinationRecovery(width, height);
        _combinationWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width >= 1100 ? 220 : 160) });
        _combinationWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _combinationWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = inlineRecovery ? new GridLength(width >= 1100 ? 320 : 260) : new GridLength(0) });
        _combinationWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _combinationWorkspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (!inlineRecovery)
        {
            double recoveryHeight = RepositoryWorkspacePolicy.StackedRecoveryHeight(height);
            _combinationWorkspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(recoveryHeight) });
            Place(_combinationRecoveryCard!, 2, 0, 2);
        }
        else Place(_combinationRecoveryCard!, 1, 2);
    }

    private void UpdateRepositoryCoverLayoutV4()
    {
        QueueRepositoryCoverLayoutV4();
    }

    private void QueueRepositoryCoverLayoutV4()
    {
        if (_repositoryCoverLayoutQueued) return;
        _repositoryCoverLayoutQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _repositoryCoverLayoutQueued = false;
            ApplyRepositoryCoverLayoutV4();
        });
    }

    private void ApplyRepositoryCoverLayoutV4()
    {
        if (_repositoryCoverWrap is null || _repositoryCoverGrid!.ActualWidth <= 0) return;
        using var timing = MeasureRepositoryUi(RepositoryPerformanceOperation.CoverLayout, _repositoryVisibleMods.Count);
        double actualGallery = Math.Max(140, _repositoryCoverGrid.ActualWidth - 24);
        if (double.IsNaN(_repositoryCoverRepeater!.Width) || Math.Abs(_repositoryCoverRepeater.Width - actualGallery) > .5)
            _repositoryCoverRepeater.Width = actualGallery;
        int columns = Math.Clamp((int)Math.Floor((actualGallery + 8) / 244), 1, 8);
        // The layout owns card stretching. Keep a fixed minimum instead of deriving
        // it from a stretched slot (which loses a column after scrollbar rounding).
        double minimumWidth = Math.Min(236, actualGallery);
        if (Math.Abs(_repositoryCoverWrap.MinItemWidth - minimumWidth) > .5) _repositoryCoverWrap.MinItemWidth = minimumWidth;
        double itemHeight = Math.Clamp(_repositoryCoverGrid.ActualHeight - 8, 140, 240);
        if (Math.Abs(_repositoryCoverWrap.MinItemHeight - itemHeight) > .5) _repositoryCoverWrap.MinItemHeight = itemHeight;
        var previewHeight = new GridLength(Math.Clamp(itemHeight - 86, 50, 140));
        foreach (var mod in _repositoryVisibleMods)
        {
            mod.CoverPreviewHeight = previewHeight;
        }
        if (_repositoryCoverWrap.MaximumRowsOrColumns != columns) _repositoryCoverWrap.MaximumRowsOrColumns = columns;
    }

    private void OnRepositoryCatalogChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        _repositoryCatalogRevision++;
        if (_repositoryRefreshQueued) return;
        _repositoryRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _repositoryRefreshQueued = false;
            // Explicit population/search may already have applied this snapshot.
            if (_repositoryViewRevision != _repositoryCatalogRevision) RefreshRepositoryModView();
        })) _repositoryRefreshQueued = false;
    }

    private void RefreshRepositoryModView()
    {
        if (!_repositoryV4Ready) return;
        using var timing = MeasureRepositoryUi(RepositoryPerformanceOperation.Filter, _secondLevelItems.Count);
        string? current = _repositorySelectedMod?.Path ?? _currentSecondLevelPath;
        string? path = RepositoryWorkspacePolicy.PreserveSelection(_secondLevelItems.Select(mod => mod.Path), current);
        _repositorySelectedMod = _secondLevelItems.FirstOrDefault(mod => string.Equals(mod.Path, path, StringComparison.OrdinalIgnoreCase));
        var visible = _secondLevelItems.Where(mod => RepositoryWorkspacePolicy.MatchesQuery(mod.Name,
            Path.GetFileName(Path.GetDirectoryName(mod.Path)) ?? string.Empty, _repositoryModSearch!.Text)).ToArray();
        _syncingRepositorySelection = true;
        try
        {
            foreach (var mod in visible) mod.CoverPlaceholder = L("未设置预览图", "No preview image");
            _repositoryVisibleMods.ReplaceAll(visible);
            SecondLevelListView.SelectedItem = visible.Contains(_repositorySelectedMod) ? _repositorySelectedMod : null;
            UpdateRepositoryCoverSelectionV4();
        }
        finally { _syncingRepositorySelection = false; }
        _repositoryViewRevision = _repositoryCatalogRevision;
        _repositoryModTitle!.Text = FirstLevelListView.SelectedItem is FirstLevelFolderItem role ? role.Name : L("Mod 文件", "Mod Files");
        _repositoryModCount!.Text = L($"{visible.Length} / {_secondLevelItems.Count} 个 Mod", $"{visible.Length} / {_secondLevelItems.Count} Mods");
        _repositoryEmptyMessage!.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _repositoryEmptyMessage.Text = _secondLevelItems.Count > 0
            ? L("未找到匹配的 Mod。请调整搜索内容。", "No matching Mods. Try another search.")
            : L("此角色还没有 Mod。可以导入压缩包或拖入文件夹。", "No Mods for this character. Import an archive or drop a folder here.");
        QueueRepositoryCoverLayoutV4();
        UpdateRepositorySelectedMetadataV4();
    }

    private void SyncRepositorySelectionV4(SecondLevelFolderItem? item)
    {
        if (!_repositoryV4Ready) return;
        _repositorySelectedMod = item;
        _syncingRepositorySelection = true;
        try
        {
            SecondLevelListView.SelectedItem = _repositoryVisibleMods.Contains(item!) ? item : null;
            UpdateRepositoryCoverSelectionV4();
        }
        finally { _syncingRepositorySelection = false; }
        UpdateRepositorySelectedMetadataV4();
    }

    private void UpdateRepositoryCoverSelectionV4()
    {
        foreach (var mod in _repositoryVisibleMods)
            mod.CoverSelectionThickness = new Thickness(ReferenceEquals(mod, _repositorySelectedMod) ? 2 : 0);
    }

    private void OnRepositoryCoverClicked(object sender, RoutedEventArgs args)
    {
        if (_syncingRepositorySelection || _repositoryWorkspaceBusy) return;
        if (sender is FrameworkElement { DataContext: SecondLevelFolderItem mod }) SecondLevelListView.SelectedItem = mod;
    }

    private async void LoadRepositoryCoverV4(SecondLevelFolderItem mod)
    {
        if (mod.CoverImageSource is not null || !_repositoryCoversLoading.Add(mod)) return;
        try
        {
            string? path = FindPreviewImage(mod.Files);
            if (path is null || !File.Exists(path) || new FileInfo(path).Length > MaxOnlinePreviewImageBytes) return;
            BitmapImage bitmap = await LoadLocalBitmapAsync(path, decodePixelWidth: 420);
            mod.CoverImageSource = bitmap;
        }
        catch (Exception exception) { LogApplicationIssue("Repository cover", exception); }
        finally { _repositoryCoversLoading.Remove(mod); }
    }

    private void UpdateRepositorySelectedMetadataV4()
    {
        if (_repositorySelectedPath is null) return;
        var mod = GetSelectedSecondLevelItem();
        _repositorySelectedPath.Text = mod?.Path ?? L("选择一个 Mod", "Select a Mod");
        ToolTipService.SetToolTip(_repositorySelectedPath, mod?.Path);
        _repositoryOpenModFolder!.IsEnabled = mod is not null && !_repositoryWorkspaceBusy;
        _repositoryOpenDetails!.IsEnabled = mod is not null && !_repositoryWorkspaceBusy;
        _repositoryDeploymentHint!.Text = GetSelectedRepository()?.UseDirectoryLinks == true
            ? L("链接部署；替换同角色旧链接，不删除仓库源文件。", "Link deployment replaces the same character's old link; source files are kept.")
            : L("复制部署；安装与移除前保留可恢复备份。", "Copy deployment keeps a recoverable backup before installation or removal.");
    }

    private void RefreshRepositoryCountsV4()
    {
        if (_repositoryCountText is not null)
            _repositoryCountText.Text = L($"{FirstCountTextBlock.Text} 个角色  ·  {SecondCountTextBlock.Text} 个 Mod", $"{FirstCountTextBlock.Text} characters  ·  {SecondCountTextBlock.Text} Mods");
    }

    private void RefreshRepositoryPickerV4()
    {
        if (_repositoryWorkspacePicker is null) return;
        _refreshingRepositoryPicker = true;
        try
        {
            _repositoryWorkspacePicker.Items.Clear();
            foreach (var repository in _repositories) _repositoryWorkspacePicker.Items.Add(new ComboBoxItem { Content = repository.Name, Tag = repository.Id });
            _repositoryWorkspacePicker.SelectedItem = _repositoryWorkspacePicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == _selectedRepositoryId);
            _repositoryWorkspacePicker.PlaceholderText = L("选择仓库", "Select Repository");
        }
        finally { _refreshingRepositoryPicker = false; }
    }

    private void RefreshCombinationWorkspaceV4()
    {
        if (_combinationPresetList is null || _refreshingCombinationWorkspace) return;
        _refreshingCombinationWorkspace = true;
        try
        {
            _combinationPresetList.Items.Clear();
            foreach (var profile in GetCurrentRepositoryProfiles())
            {
                var label = new StackPanel { Spacing = 5, Margin = new Thickness(2, 8, 2, 8) };
                var name = RepositoryColumns(GridLength.Auto, new GridLength(1, GridUnitType.Star));
                name.Children.Add(new FontIcon { Glyph = "\uE8F1", FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
                var nameText = new TextBlock { Text = profile.Name, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(nameText, 1); name.Children.Add(nameText);
                label.Children.Add(name);
                label.Children.Add(new TextBlock { Text = L($"{profile.ModRelativePaths.Count} 个 Mod · {(profile.IncludesPersistentState ? "组合 + 状态" : "仅组合")}", $"{profile.ModRelativePaths.Count} Mods · {(profile.IncludesPersistentState ? "set + state" : "set only")}"), FontSize = 12 });
                _combinationPresetList.Items.Add(new ListViewItem { Content = label, Tag = profile.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            _combinationPresetList.SelectedItem = _combinationPresetList.Items.OfType<ListViewItem>().FirstOrDefault(item => (string?)item.Tag == _selectedConfigurationProfileId);
            var selected = GetSelectedConfigurationProfile();
            _combinationMemberTitle!.Text = selected?.Name ?? L("预设中的 Mod", "Mods in Preset");
            _combinationMemberPanel!.Children.Clear();
            if (selected is null)
            {
                var empty = new TextBlock { Text = L("还没有组合预设。\n部署需要的 Mod 后，点击左下角保存当前组合。", "No combination presets yet.\nDeploy your Mods, then save the current set using the button below."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 20, 8, 8) };
                _combinationMemberPanel.Children.Add(empty);
            }
            else foreach (string relative in selected.ModRelativePaths)
            {
                int values = selected.PersistentStates.FirstOrDefault(state => string.Equals(state.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))?.Values.Count ?? 0;
                var body = new StackPanel { Spacing = 5 };
                body.Children.Add(new TextBlock { Text = Path.GetFileName(relative), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new TextBlock { Text = Path.GetDirectoryName(relative), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new TextBlock { Text = selected.IncludesPersistentState ? L($"已保存 {values} 个持久参数", $"{values} saved persistent values") : L("仅恢复部署组合", "Deployment set only"), FontSize = 12 });
                var row = RepositoryColumns(GridLength.Auto, new GridLength(1, GridUnitType.Star));
                row.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(body, 1); row.Children.Add(body);
                _combinationMemberPanel.Children.Add(RepositoryCard(row));
            }
            _combinationPresetList.IsEnabled = !_repositoryWorkspaceBusy;
        }
        finally { _refreshingCombinationWorkspace = false; }
    }

    private void SetRepositoryWorkspaceBusyV4(bool busy)
    {
        _repositoryWorkspaceBusy = busy;
        RefreshRepositoryStatusV4();
        if (!_repositoryV4Ready) return;
        _repositoryCoverGrid!.IsEnabled = !busy;
        _repositoryModSearch!.IsEnabled = !busy;
        _repositoryWorkspacePicker!.IsEnabled = !busy;
        _repositoryOpenPresets!.IsEnabled = !busy;
        _combinationPresetList!.IsEnabled = !busy;
        foreach (var button in _repositoryModeButtons) button.IsEnabled = !busy;
        UpdateRepositorySelectedMetadataV4();
    }

    private async Task ShowRepositoryDetailsV4Async()
    {
        if (_repositoryDetailDialogOpen || _repositoryWorkspaceBusy || GetSelectedSecondLevelItem() is null) return;
        _repositoryDetailDialogOpen = true;
        RepositoryDetach(_workspaceDetailHost!);
        _workspaceDetailHost!.Visibility = Visibility.Visible;
        _workspaceDetailHost.Height = Math.Clamp(RootGrid.ActualHeight - 150, 320, 600);
        // The dialog can be short; scroll only its preview body, not the pinned deploy action.
        var previewContent = _previewCard!.Child;
        _previewCard.Child = null;
        var previewScroll = new ScrollViewer
        {
            Content = previewContent,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _previewCard.Child = previewScroll;
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Content = _workspaceDetailHost, CloseButtonText = L("关闭", "Close"), RequestedTheme = RootGrid.ActualTheme };
        try { await dialog.ShowAsync(); }
        finally
        {
            dialog.Content = null;
            previewScroll.Content = null;
            _previewCard.Child = previewContent;
            _workspaceDetailHost.Height = double.NaN;
            _workspaceWorkbench!.Children.Add(_workspaceDetailHost);
            _repositoryDetailDialogOpen = false;
            UpdateRepositoryWorkspaceLayoutV4();
        }
    }

    private static Grid RepositoryRows(params GridLength[] rows)
    {
        var grid = new Grid { RowSpacing = 10 };
        foreach (var row in rows) grid.RowDefinitions.Add(new RowDefinition { Height = row });
        return grid;
    }

    private static Grid RepositoryColumns(params GridLength[] columns)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        foreach (var column in columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column });
        return grid;
    }

    private static TextBlock RepositoryText(double size, bool semibold = false) => new()
    {
        FontSize = size, FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border RepositoryCard(UIElement child) => new()
    {
        Style = (Style)Application.Current.Resources["CardBorderStyle"], Child = child,
        Padding = new Thickness(12), CornerRadius = new CornerRadius(6)
    };

    private static StackPanel RepositoryIconLabel(string glyph, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private static Button RepositoryButton(string glyph, RoutedEventHandler click)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["SecondaryButtonStyle"], MinHeight = 34,
            CornerRadius = new CornerRadius(4), Content = new FontIcon { Glyph = glyph, FontSize = 16 } };
        button.Click += click;
        return button;
    }

    private static void RepositoryDecorateButton(Button button, string glyph, bool iconOnly = false)
    {
        void Decorate()
        {
            if (button.Content is not string label) return;
            button.Content = iconOnly ? new FontIcon { Glyph = glyph, FontSize = 16 }
                : RepositoryIconLabel(glyph, label);
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label);
        }
        button.CornerRadius = new CornerRadius(4);
        button.RegisterPropertyChangedCallback(ContentControl.ContentProperty, (_, _) => Decorate());
        Decorate();
    }

    private void RepositoryDetach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel when panel.Children.Contains(element): panel.Children.Remove(element); return;
            case Border border when ReferenceEquals(border.Child, element): border.Child = null; return;
            case ContentControl control when ReferenceEquals(control.Content, element): control.Content = null; return;
        }
        // During construction, WinUI may not expose Parent for an unloaded subtree.
        // Detach through the owned logical children, before assigning a second parent.
        foreach (var root in new FrameworkElement?[] { RootGrid, _categoryCard, _modCard, _workspaceActions,
            _workspaceDetailHost, _previewCard, _shortcutCard, UpdatesSupportPanel, WorkspaceStatusCard })
            if (root is not null && RepositoryRemoveOwnedChild(root, element)) return;
    }

    private static bool RepositoryRemoveOwnedChild(FrameworkElement root, FrameworkElement element)
    {
        if (root is Panel panel)
        {
            if (panel.Children.Contains(element)) { panel.Children.Remove(element); return true; }
            foreach (var child in panel.Children.OfType<FrameworkElement>().ToArray())
                if (RepositoryRemoveOwnedChild(child, element)) return true;
        }
        if (root is Border border && border.Child is FrameworkElement borderChild)
        {
            if (ReferenceEquals(borderChild, element)) { border.Child = null; return true; }
            if (RepositoryRemoveOwnedChild(borderChild, element)) return true;
        }
        if (root is ContentControl control && control.Content is FrameworkElement content)
        {
            if (ReferenceEquals(content, element)) { control.Content = null; return true; }
            if (RepositoryRemoveOwnedChild(content, element)) return true;
        }
        if (root is TabView tabs)
            foreach (var tab in tabs.TabItems.OfType<FrameworkElement>())
                if (RepositoryRemoveOwnedChild(tab, element)) return true;
        return false;
    }

    private void RepositoryMove(FrameworkElement element, Panel parent, int row = 0, int column = 0)
    {
        RepositoryDetach(element);
        Grid.SetRow(element, row); Grid.SetColumn(element, column); Grid.SetColumnSpan(element, 1); Grid.SetRowSpan(element, 1);
        parent.Children.Add(element);
    }
}
