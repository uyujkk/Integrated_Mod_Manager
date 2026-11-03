using System.Collections.Specialized;
using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Repository;
using IntegratedModManager.Core.Shortcuts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

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
    private readonly ModListViewModel _modList = new();
    private readonly ModDetailsViewModel _modDetails = new();
    private readonly HashSet<SecondLevelFolderItem> _repositoryCoversLoading = [];
    private ToggleButton[] _repositoryModeButtons = [];
    private TextBox? _repositoryModSearch;
    private EmptyState? _repositoryEmptyState;
    private StackPanel? _repositoryEmptyButtons;
    private HyperlinkButton? _repositoryEmptyAdopt;
    private Border? _repositorySetupCard;
    private TextBlock? _repositorySetupTitle;
    private TextBlock? _repositorySetupProgress;
    private SetupStepRow[] _repositorySetupSteps = [];
    private ToggleButton[] _repositoryStateChips = [];
    private TextBlock[] _repositoryInfoLabels = [];
    private HyperlinkButton? _repositoryInfoSource;
    private TextBlock? _repositoryInfoSourceEmpty;
    private Button? _repositoryEditSource;
    private Button? _repositoryAddTags;
    private StackPanel? _repositoryInfoTags;
    private TextBlock? _repositoryInfoNoTags;
    private TextBlock? _repositoryInfoSize;
    private TextBlock? _repositoryLinkEditorTitle;
    private TextBlock? _repositoryPreviewCaption;
    private Border? _repositoryPreviewCaptionHost;
    private readonly Dictionary<SecondLevelFolderItem, Task<long?>> _modSizeTasks = [];
    private TextBlock? _repositoryTargetStatus;
    private int _repositorySizeRevision;

    // One step of the first-run checklist: done mark or step number, what to set, and the button that sets it.
    private sealed record SetupStepRow(FontIcon Done, Border Number, TextBlock Title, TextBlock Detail, Button Action);
    private Button? _repositoryEmptyImport;
    private Button? _repositoryEmptyOnline;
    private ScrollViewer? _repositoryCoverGrid;
    private ItemsRepeater? _repositoryCoverRepeater;
    private UniformGridLayout? _repositoryCoverWrap;
    private Button? _repositoryOpenDetails;
    private Button? _repositoryCopyModPath;
    private TextBlock? _repositorySelectedPath;
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
        _repositoryModSearch = new TextBox { Height = 36, MinWidth = 0 };
        _repositoryModSearch.TextChanged += (_, _) => RefreshRepositoryModView();
        AutomationProperties.SetName(_repositoryModSearch, T("RepositoryWorkspace.SearchMods"));
        RepositoryDecorateButton(CreateFirstLevelButton, "\uE710", true);
        UpdateCharacterHeaderActions();

        RepositoryDecorateButton(DeleteSecondLevelButton, "\uE74D", true);
        InitializeLocalTagControls(ModCardGrid);
        _repositoryStateChips = Enum.GetValues<ModStateFilter>().Select(filter =>
        {
            var chip = new ToggleButton
            {
                MinHeight = 28, Padding = new Thickness(12, 2, 12, 2), CornerRadius = new CornerRadius(14),
                Tag = filter, IsChecked = filter == _modList.StateFilter
            };
            chip.Click += (_, _) => SetRepositoryStateFilter((ModStateFilter)chip.Tag);
            RepositoryStateChipsPanel.Children.Add(chip);
            return chip;
        }).ToArray();

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
            ItemsSource = _modList.VisibleMods,
            ItemTemplate = (DataTemplate)RootGrid.Resources["RepositoryCoverTemplate"],
            Layout = _repositoryCoverWrap,
            // A finite positive width is required before the first measure pass.
            Width = 236,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _repositoryCoverRepeater.ElementPrepared += (_, args) =>
        {
            if (_modList.VisibleMods.ElementAtOrDefault(args.Index) is { } mod)
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
        SecondLevelListView.ItemsSource = _modList.VisibleMods;
        // List rows show the same cover thumbnail as the cover view, loaded as rows come into view.
        SecondLevelListView.ContainerContentChanging += (list, args) =>
        {
            if (args.InRecycleQueue || args.Item is not SecondLevelFolderItem mod) return;
            LoadRepositoryCoverV4(mod);
            _ = EnsureModSizeAsync(mod);
            _ = EnsureModShortcutCountAsync(mod);
        };
        RepositoryModViewsGrid.Children.Add(_repositoryCoverGrid);
        RepositoryModViewsGrid.Children.Add(CreateRepositoryEmptyState());

        InitializeRepositoryInspectorV4();
        InitializeCombinationWorkspaceV4();
        InitializeRepositoryToolbarV4();
        // The repository is initially hidden on a clean install. Its first real
        // viewport is measured only when the user opens it, not when RootGrid loads.
        WorkspaceScrollViewer.SizeChanged += (_, _) => UpdateRepositoryWorkspaceLayoutV4();
        _secondLevelItems.CollectionChanged += OnRepositoryCatalogChanged;
        _repositoryV4Ready = true;
        RootGrid.ActualThemeChanged += (_, _) => UpdateRepositoryWorkspaceLanguageV4();
        UpdateRepositoryWorkspaceLanguageV4();
        RefreshRepositoryModView();
        SetRepositoryWorkspaceViewV4(_modList.View, save: false);
    }

    private void OnCharacterMoreClicked(object sender, RoutedEventArgs e)
    {
        if (_characters.Selected is { } item)
            CreateFirstLevelMenu(item).ShowAt((FrameworkElement)sender,
                new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
    }

    private void OnRepositoryWorkspacePickerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingRepositoryPicker || _repositoryWorkspaceBusy) return;
        if (RepositoryWorkspacePicker.SelectedItem is ComboBoxItem { Tag: string id }
            && !string.Equals(id, _selectedRepositoryId, StringComparison.Ordinal))
            SwitchRepository(id, openManager: true);
    }

    private void OnRepositoryOpenModFolderClicked(object sender, RoutedEventArgs e)
    {
        if (GetSelectedSecondLevelItem() is { } mod) OpenDirectory(mod.Path, T("Common.ModFolder"));
    }

    private void InitializeRepositoryToolbarV4()
    {
        RepositoryDecorateButton(RefreshButton, "\uE72C", true);
        RepositoryDecorateButton(ImportZipButton, "\uE8E5");
        RepositoryDecorateButton(RunLauncherButton, "\uE768");
        _repositoryModeButtons = Enumerable.Range(0, 3).Select(index =>
        {
            var button = new ToggleButton { MinHeight = 34, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(4), Tag = (RepositoryWorkspaceView)index };
            AutomationProperties.SetAutomationId(button, "RepositoryView" + (RepositoryWorkspaceView)index);
            button.Click += (_, _) => SetRepositoryWorkspaceViewV4((RepositoryWorkspaceView)button.Tag);
            RepositoryViewButtonsPanel.Children.Add(button);
            return button;
        }).ToArray();
        _repositoryOpenDetails = RepositoryButton("\uE8A5", async (_, _) => await ShowRepositoryDetailsV4Async());
        RepositoryViewButtonsPanel.Children.Add(_repositoryOpenDetails);
        InitializeExistingModImportCommands(RepositoryViewButtonsPanel);

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
            new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
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
        _repositoryTargetStatus = RepositoryText(12);
        _repositoryTargetStatus.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_repositoryTargetStatus, 3); line.Children.Add(_repositoryTargetStatus);
        TargetTextBox.TextChanged += (_, _) => RefreshRepositoryStatusV4();
        Grid.SetRow(AuthorTextBlock, 0); Grid.SetColumn(AuthorTextBlock, 4); line.Children.Add(AuthorTextBlock);
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
            ? (_repositoryWorkspaceBusy ? T("RepositoryWorkspace.Working") : T("RepositoryWorkspace.Idle"))
            : message;
        _repositoryTaskStatusIcon.Glyph = _repositoryWorkspaceBusy ? "\uE895" : "\uE73E";
        ToolTipService.SetToolTip(_repositoryTaskStatusText, message);
        ToolTipService.SetToolTip(StatusTextBlock, StatusTextBlock.Text);
        CopyProgressBar.IsIndeterminate = _repositoryWorkspaceBusy && CopyProgressBar.Value <= 0;
        CopyProgressBar.Visibility = _repositoryWorkspaceBusy ? Visibility.Visible : Visibility.Collapsed;
        double width = WorkspaceScrollViewer.ActualWidth;
        _repositoryTaskStatusText.MaxWidth = width > 0 && width < 620 ? 140 : 300;
        AuthorTextBlock.Visibility = width > 0 && width < 620 ? Visibility.Collapsed : Visibility.Visible;
        if (_repositoryTargetStatus is not null)
        {
            string target = (TargetTextBox.Text ?? string.Empty).Trim();
            bool connected = target.Length > 0 && Directory.Exists(target);
            _repositoryTargetStatus.Text = connected ? T("RepositoryWorkspace.TargetConnected") : T("RepositoryWorkspace.TargetMissing");
            _repositoryTargetStatus.Foreground = GetAppThemeBrush(connected ? "AppSuccessBrush" : "AppDangerBrush");
        }
    }

    private void InitializeRepositoryInspectorV4()
    {
        PreviewCard.Padding = new Thickness(12);
        PreviewSectionTitleTextBlock.Visibility = Visibility.Collapsed;
        PreviewSectionSubtitleTextBlock.Visibility = Visibility.Collapsed;
        ModLinkSectionSubtitleTextBlock.Visibility = Visibility.Collapsed;
        PreviewFooterTextBlock.Visibility = Visibility.Collapsed;
        PreviewDropBorder.Height = double.NaN;
        // A small minimum only: on a medium-height window the row was shorter than the old 130,
        // and the overflow clipped the centred hint. A short area drops the icon instead (below).
        PreviewDropBorder.MinHeight = 64;
        PreviewDropBorder.CornerRadius = new CornerRadius(4);
        if (PreviewCard.Child is Grid previewGrid) previewGrid.RowSpacing = 8;

        // An empty preview shows an image icon above the existing hint, and hides with it.
        // Parent is not reliable before load; go through the owning controls instead.
        if (PreviewDropBorder.Child is Grid previewHost && previewHost.Children.Contains(PreviewHintTextBlock))
        {
            var previewIcon = new FontIcon { Glyph = "\uEB9F", FontSize = 28, Opacity = 0.6 };
            var previewEmpty = new StackPanel
            {
                Spacing = 4, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            previewHost.Children.Remove(PreviewHintTextBlock);
            PreviewHintTextBlock.Margin = new Thickness(18, 0, 18, 0);
            previewEmpty.Children.Add(previewIcon);
            previewEmpty.Children.Add(PreviewHintTextBlock);
            previewHost.Children.Add(previewEmpty);
            void UpdatePreviewIcon() => previewIcon.Visibility =
                PreviewHintTextBlock.Visibility == Visibility.Visible && PreviewDropBorder.ActualHeight >= 120
                    ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreviewIcon();
            PreviewHintTextBlock.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdatePreviewIcon());
            PreviewDropBorder.SizeChanged += (_, _) => UpdatePreviewIcon();
        }

        // The preview keeps a fixed frame (16:9 of the column, within limits) instead of taking all
        // spare height; under it, a table says where the Mod came from, where it is, its tags and size.
        // The tab scrolls, so a short window never clips the table.
        if (PreviewCard.Child is Grid preview
            && preview.Children.OfType<Border>().FirstOrDefault(border => ReferenceEquals(border.Child, ModLinkTextBox)) is { } linkBorder)
        {
            preview.RowDefinitions[2].Height = GridLength.Auto;
            ModLinkSectionTitleTextBlock.Visibility = Visibility.Collapsed;
            // The link box and its open button move into the flyout of the source row's edit button.
            var linkRow = RepositoryColumns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            linkRow.Width = 320;
            RepositoryMove(linkBorder, linkRow);
            RepositoryMove(OpenModLinkButton, linkRow, 0, 1);
            OpenModLinkButton.MinWidth = 38; OpenModLinkButton.Padding = new Thickness(8);
            _repositoryLinkEditorTitle = RepositoryText(12);
            _repositoryLinkEditorTitle.Opacity = 0.75;
            var editor = new StackPanel { Spacing = 8 };
            editor.Children.Add(_repositoryLinkEditorTitle);
            editor.Children.Add(linkRow);
            var info = CreateRepositoryInfoTable(new Flyout { Content = editor, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
            Grid.SetRow(info, 4); preview.Children.Add(info);
        }
        RepositoryDecorateButton(OpenModLinkButton, "\uE774", true);
        PreviewCard.SizeChanged += (_, _) => UpdatePreviewFrameHeight();
        WorkspaceDetailHost.SizeChanged += (_, _) => UpdatePreviewFrameHeight();
        PreviewDropBorder.Height = 198;
        if (PreviewDropBorder.Child is Grid previewFrame)
        {
            _repositoryPreviewCaption = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
            _repositoryPreviewCaptionHost = new Border
            {
                Margin = new Thickness(10), Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x73, 0, 0, 0)), Child = _repositoryPreviewCaption,
                Visibility = Visibility.Collapsed
            };
            previewFrame.Children.Add(_repositoryPreviewCaptionHost);
        }
        if (PreviewTab.Content is UIElement previewContent)
        {
            PreviewTab.Content = null;
            PreviewTab.Content = new ScrollViewer
            {
                Content = previewContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        RepositoryDecorateButton(ToggleCopyButton, "\uE71B");
        ShortcutCard.Padding = new Thickness(12);
        ShortcutSectionSubtitleTextBlock.Visibility = Visibility.Collapsed;
        ShortcutHintTextBlock.Visibility = Visibility.Collapsed;
        PreviewTab.Header = T("RepositoryWorkspace.Preview");
    }

    // A 16:9 frame at the column width, but never more than about a quarter of the workspace
    // height, so the table under it stays in view on medium windows.
    private void UpdatePreviewFrameHeight()
    {
        double width = PreviewCard?.ActualWidth ?? 0;
        if (width <= 0) return;
        double byHeight = WorkspaceScrollViewer.ActualHeight > 0 ? WorkspaceScrollViewer.ActualHeight * 0.28 : 240;
        PreviewDropBorder.Height = Math.Clamp(Math.Round(Math.Min((width - 24) * 9 / 16, byHeight)), 120, 240);
    }

    // Source, location, tags and size, each with the action the design puts beside it.
    private Grid CreateRepositoryInfoTable(Flyout linkEditor)
    {
        var info = RepositoryColumns(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        info.ColumnSpacing = 10;
        info.RowSpacing = 6;
        info.Margin = new Thickness(0, 4, 0, 0);
        _repositoryInfoLabels = new TextBlock[4];
        for (int row = 0; row < 4; row++)
        {
            info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var label = RepositoryText(12);
            label.Opacity = 0.7;
            label.MinWidth = 44;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(label, row);
            info.Children.Add(label);
            _repositoryInfoLabels[row] = label;
        }

        Button RowButton(string glyph, int row, RoutedEventHandler click)
        {
            Button button = RepositoryButton(glyph, click);
            button.Width = 28; button.Height = 28; button.MinWidth = 28; button.MinHeight = 28; button.Padding = new Thickness(5);
            ((FontIcon)button.Content).FontSize = 13;
            Grid.SetRow(button, row); Grid.SetColumn(button, 2);
            info.Children.Add(button);
            return button;
        }

        void Value(FrameworkElement element, int row)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(element, row); Grid.SetColumn(element, 1);
            info.Children.Add(element);
        }

        _repositoryInfoSource = new HyperlinkButton
        {
            Padding = new Thickness(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }
        };
        _repositoryInfoSource.Click += (_, args) => OnOpenModLinkClicked(OpenModLinkButton, args);
        Value(_repositoryInfoSource, 0);
        _repositoryInfoSourceEmpty = RepositoryText(12);
        _repositoryInfoSourceEmpty.Opacity = 0.6;
        Value(_repositoryInfoSourceEmpty, 0);
        _repositoryEditSource = RowButton("\uE70F", 0, (sender, _) => linkEditor.ShowAt((FrameworkElement)sender));

        _repositorySelectedPath = RepositoryText(12);
        _repositorySelectedPath.MaxLines = 1;
        _repositorySelectedPath.Opacity = 0.85;
        _repositorySelectedPath.TextTrimming = TextTrimming.CharacterEllipsis;
        Value(_repositorySelectedPath, 1);
        _repositoryCopyModPath = RowButton("\uE8C8", 1, (_, _) => CopySelectedModPath());

        _repositoryInfoTags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Value(_repositoryInfoTags, 2);
        _repositoryInfoNoTags = RepositoryText(12);
        _repositoryInfoNoTags.Opacity = 0.6;
        Value(_repositoryInfoNoTags, 2);
        _repositoryAddTags = RowButton("\uE710", 2, async (_, _) =>
        {
            if (GetSelectedSecondLevelItem() is { } mod) await RunUiActionAsync("EditLocalTagsAsync", () => EditLocalTagsAsync(mod));
        });

        _repositoryInfoSize = RepositoryText(12);
        _repositoryInfoSize.Opacity = 0.85;
        Value(_repositoryInfoSize, 3);

        ModLinkTextBox.TextChanged += (_, _) => UpdateRepositoryInfoSource();
        return info;
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
        shortcut.Content = RepositoryIconLabel("\uE8F1", T("Common.OpenCombinationWorkspace"));
        _updatesCombinationShortcut = shortcut;
        // Restoring a preset is a recovery action: keep it beside "Undo last change" instead of a lone card.
        shortcut.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(shortcut, 1);
        UpdatesRecoveryActionsGrid.Children.Add(shortcut);
        WorkspaceContentGrid.Children.Add(_combinationWorkspace);
    }

    // The Updates page's shortcut to the combination workspace; its label follows the language.
    private Button? _updatesCombinationShortcut;

    private void SetRepositoryWorkspaceViewV4(RepositoryWorkspaceView view, bool save = true)
    {
        if (!_repositoryV4Ready || _repositoryWorkspaceBusy || _repositoryDetailDialogOpen) return;
        _modList.View = view;
        WorkspaceWorkbench.Visibility = view == RepositoryWorkspaceView.Presets ? Visibility.Collapsed : Visibility.Visible;
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
        WorkspaceTitleTextBlock.Text = _modList.View == RepositoryWorkspaceView.Presets ? T("RepositoryWorkspace.CombinationPresets") : T("Common.ModWorkspace");
        RepositoryCategoryTitleTextBlock.Text = T("RepositoryWorkspace.Characters");
        RepositoryModTitleTextBlock.Text = _characters.Selected is { } role ? role.Name : T("RepositoryWorkspace.ModFiles");
        _repositoryModSearch!.PlaceholderText = T("RepositoryWorkspace.SearchThisCharacterSMods");
        if (_localTagsButton is not null) _localTagsButton.Content = T("Common.Tags");
        if (_updatesCombinationShortcut is not null)
            _updatesCombinationShortcut.Content = RepositoryIconLabel("", T("Common.OpenCombinationWorkspace"));
        AutomationProperties.SetName(_repositoryModSearch, T("RepositoryWorkspace.SearchMods"));
        FirstLevelSearchTextBox.PlaceholderText = T("RepositoryWorkspace.SearchCharacters");
        string[] labels = [T("RepositoryWorkspace.Files"), T("RepositoryWorkspace.Covers"), T("RepositoryWorkspace.Presets")];
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
        _repositoryOpenDetails!.Content = RepositoryIconLabel("\uE8A5", T("RepositoryWorkspace.Details"));
        AutomationProperties.SetName(_repositoryOpenDetails, T("RepositoryWorkspace.ModDetails"));
        ToolTipService.SetToolTip(RepositoryOpenModFolderButton, T("RepositoryWorkspace.OpenFolder"));
        AutomationProperties.SetName(RepositoryOpenModFolderButton, T("RepositoryWorkspace.OpenModFolder"));
        ToolTipService.SetToolTip(_repositoryCopyModPath!, T("RepositoryWorkspace.CopyPath"));
        AutomationProperties.SetName(_repositoryCopyModPath, T("RepositoryWorkspace.CopyPath"));
        PreviewTab.Header = T("RepositoryWorkspace.Preview");
        SelectionEyebrowTextBlock.Text = T("RepositoryWorkspace.CURRENTMOD");
        CurrentFolderTextBlock.Foreground = GetAppThemeBrush("AppNavDefaultForegroundBrush");
        DeploymentStateLabelTextBlock.Visibility = Visibility.Collapsed;
        _combinationWorkspaceTitle!.Text = T("RepositoryWorkspace.MyPresets");
        AutomationProperties.SetName(DeleteConfigurationProfileButton, T("RepositoryWorkspace.DeletePreset"));
        ToolTipService.SetToolTip(DeleteConfigurationProfileButton, T("RepositoryWorkspace.DeletePreset"));
        AutomationProperties.SetName(RepositoryWorkspacePicker, T("RepositoryWorkspace.CurrentRepository"));
        _combinationRecoveryTitle!.Text = T("RepositoryWorkspace.SaveRestore");
        _combinationRecoveryHint!.Text = T("RepositoryWorkspace.N1CloseTheGameAndLoader");
        _combinationNoProfiles!.Text = T("RepositoryWorkspace.OnlyRecognizedDeploymentsInThisRepository");
        foreach (var mod in _secondLevelItems) mod.CoverPlaceholder = T("RepositoryWorkspace.NoPreviewImage");
        string[] chipLabels = [T("RepositoryWorkspace.FilterAll"), T("RepositoryWorkspace.FilterDeployed"), T("RepositoryWorkspace.FilterNotDeployed")];
        for (int index = 0; index < _repositoryStateChips.Length; index++)
        {
            _repositoryStateChips[index].Content = chipLabels[index];
            foreach (string state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
            {
                _repositoryStateChips[index].Resources["ToggleButtonBackground" + state] = GetAppThemeBrush("AppSecondarySelectedBrush");
                _repositoryStateChips[index].Resources["ToggleButtonForeground" + state] = GetAppThemeBrush("AppSecondarySelectedForegroundBrush");
                _repositoryStateChips[index].Resources["ToggleButtonBorderBrush" + state] = GetAppThemeBrush("AppNavSelectedBorderBrush");
            }
        }
        CharacterFolderHintTextBlock.Text = T("RepositoryWorkspace.CharacterFolderHint");
        string[] infoLabels = [T("RepositoryWorkspace.InfoSource"), T("RepositoryWorkspace.InfoLocation"), T("RepositoryWorkspace.InfoTags"), T("RepositoryWorkspace.InfoSize")];
        for (int index = 0; index < _repositoryInfoLabels.Length; index++) _repositoryInfoLabels[index].Text = infoLabels[index];
        _repositoryLinkEditorTitle?.SetValue(TextBlock.TextProperty, ModLinkSectionTitleTextBlock.Text);
        _repositoryInfoSourceEmpty!.Text = T("RepositoryWorkspace.NoSourceLink");
        _repositoryInfoNoTags!.Text = T("RepositoryWorkspace.NoTags");
        ToolTipService.SetToolTip(_repositoryEditSource!, T("RepositoryWorkspace.EditSourceLink"));
        AutomationProperties.SetName(_repositoryEditSource, T("RepositoryWorkspace.EditSourceLink"));
        ToolTipService.SetToolTip(_repositoryAddTags!, T("RepositoryWorkspace.EditTags"));
        AutomationProperties.SetName(_repositoryAddTags, T("RepositoryWorkspace.EditTags"));
        RefreshRepositoryPickerV4();
        RefreshRepositoryCountsV4();
        RefreshCombinationWorkspaceV4();
        UpdateRepositorySelectedMetadataV4();
        UpdateRepositoryEmptyState();
    }

    private void UpdateRepositoryWorkspaceLayoutV4()
    {
        if (!_repositoryV4Ready) return;
        double width = WorkspaceScrollViewer.ActualWidth;
        if (width <= 0) width = Math.Max(320, Bounds.Width - 220);
        RefreshRepositoryStatusV4();
        var layout = RepositoryWorkspacePolicy.Layout(width);
        WorkspaceWorkbench.ColumnDefinitions.Clear();
        WorkspaceWorkbench.RowDefinitions.Clear();
        WorkspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.CategoryWidth) });
        WorkspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        WorkspaceWorkbench.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.DetailWidth) });
        WorkspaceWorkbench.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Place(CategoryCard, 0, 0); Place(ModCard, 0, 1); Place(WorkspaceDetailHost, 0, 2);
        if (!_repositoryDetailDialogOpen) WorkspaceDetailHost.Visibility = layout.InlineDetails ? Visibility.Visible : Visibility.Collapsed;
        _repositoryOpenDetails!.Visibility = layout.InlineDetails || _modList.View == RepositoryWorkspaceView.Presets ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceContentGrid.VerticalAlignment = VerticalAlignment.Stretch;
        double height = WorkspaceScrollViewer.ActualHeight;
        if (height > 0) WorkspaceContentGrid.Height = height;
        WorkspaceWorkbench.Height = double.NaN;
        FirstLevelListView.Height = double.NaN; SecondLevelListView.Height = double.NaN;
        PreviewCard.MinHeight = 0; ShortcutCard.MinHeight = 0;
        UpdateRepositoryCoverLayoutV4();
        RepositoryHeaderGrid.RowDefinitions.Clear();
        RepositoryHeaderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RepositoryHeaderGrid.RowDefinitions.Add(new RowDefinition { Height = width < 720 ? GridLength.Auto : new GridLength(0) });
        RepositoryHeaderGrid.RowSpacing = width < 720 ? 8 : 0;
        Grid.SetRow(RepositoryToolbarCommandsGrid, width < 720 ? 1 : 0);
        Grid.SetColumn(RepositoryToolbarCommandsGrid, width < 720 ? 0 : 1);
        Grid.SetColumnSpan(RepositoryToolbarCommandsGrid, width < 720 ? 2 : 1);
        RepositoryToolbarCommandsGrid.HorizontalAlignment = width < 720 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        RepositoryCountTextBlock.Visibility = width < 620 ? Visibility.Collapsed : Visibility.Visible;
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
        using var timing = MeasureRepositoryUi(RepositoryPerformanceOperation.CoverLayout, _modList.VisibleMods.Count);
        RepositoryCoverGrid grid = RepositoryWorkspacePolicy.CoverGrid(_repositoryCoverGrid.ActualWidth, _repositoryCoverGrid.ActualHeight);
        if (double.IsNaN(_repositoryCoverRepeater!.Width) || Math.Abs(_repositoryCoverRepeater.Width - grid.GalleryWidth) > .5)
            _repositoryCoverRepeater.Width = grid.GalleryWidth;
        // The layout owns card stretching. Keep a fixed minimum instead of deriving
        // it from a stretched slot (which loses a column after scrollbar rounding).
        if (Math.Abs(_repositoryCoverWrap.MinItemWidth - grid.MinItemWidth) > .5) _repositoryCoverWrap.MinItemWidth = grid.MinItemWidth;
        if (Math.Abs(_repositoryCoverWrap.MinItemHeight - grid.ItemHeight) > .5) _repositoryCoverWrap.MinItemHeight = grid.ItemHeight;
        var previewHeight = new GridLength(grid.PreviewHeight);
        foreach (var mod in _modList.VisibleMods)
        {
            mod.CoverPreviewHeight = previewHeight;
        }
        if (_repositoryCoverWrap.MaximumRowsOrColumns != grid.Columns) _repositoryCoverWrap.MaximumRowsOrColumns = grid.Columns;
    }

    private void OnRepositoryCatalogChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        _modList.MarkCatalogChanged();
        if (_repositoryRefreshQueued) return;
        _repositoryRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _repositoryRefreshQueued = false;
            // Explicit population/search may already have applied this snapshot.
            if (!_modList.IsCurrent) RefreshRepositoryModView();
        })) _repositoryRefreshQueued = false;
    }

    private void RefreshRepositoryModView()
    {
        if (!_repositoryV4Ready) return;
        using var timing = MeasureRepositoryUi(RepositoryPerformanceOperation.Filter, _secondLevelItems.Count);
        string? current = _modList.SelectedMod?.Path ?? _currentSecondLevelPath;
        string? path = RepositoryWorkspacePolicy.PreserveSelection(_secondLevelItems.Select(mod => mod.Path), current);
        _modList.SelectedMod = _secondLevelItems.FirstOrDefault(mod => string.Equals(mod.Path, path, StringComparison.OrdinalIgnoreCase));
        SelectedLocalLibrary();
        foreach (var mod in _secondLevelItems) ApplyModRowState(mod);
        var visible = _secondLevelItems
            .Where(mod => MatchesLocalLibraryFilter(mod) && RepositoryWorkspacePolicy.MatchesStateFilter(mod.Tone, _modList.StateFilter))
            .ToArray();
        _syncingRepositorySelection = true;
        try
        {
            foreach (var mod in visible) mod.CoverPlaceholder = T("RepositoryWorkspace.NoPreviewImage");
            _modList.ShowMods(visible);
            SecondLevelListView.SelectedItem = visible.Contains(_modList.SelectedMod) ? _modList.SelectedMod : null;
            UpdateRepositoryCoverSelectionV4();
        }
        finally { _syncingRepositorySelection = false; }
        // A search or filter that hides the selected Mod moves the selection to the first match,
        // so the details never describe a Mod the list no longer shows.
        if (SecondLevelListView.SelectedItem is null && visible.Length > 0) SecondLevelListView.SelectedItem = visible[0];
        RepositoryModTitleTextBlock.Text = _characters.Selected is { } role ? role.Name : T("RepositoryWorkspace.ModFiles");
        RepositoryModCountTextBlock.Text = T("RepositoryWorkspace.Mods", visible.Length, _secondLevelItems.Count);
        if (_localFiltersButton is not null)
        {
            _localFiltersButton.Content = string.IsNullOrEmpty(_localTagFilter) ? T("Common.Filters") : T("RepositoryWorkspace.TagActive");
            ToolTipService.SetToolTip(_localFiltersButton, T("RepositoryWorkspace.Tag") + _localTagFilter);
        }
        UpdateRepositoryEmptyState();
        QueueRepositoryCoverLayoutV4();
        UpdateRepositorySelectedMetadataV4();
        RefreshRepositoryCountsV4();
    }

    private void SetRepositoryStateFilter(ModStateFilter filter)
    {
        _modList.StateFilter = filter;
        foreach (var chip in _repositoryStateChips) chip.IsChecked = (ModStateFilter)chip.Tag == filter;
        RefreshRepositoryModView();
    }

    // List rows show the state as a coloured pill and the Mod's tags under its name.
    private void ApplyModRowState(SecondLevelFolderItem mod)
    {
        mod.Tone = ModTone(mod);
        mod.Tags = LocalModTags(mod.Path);
        int shortcuts = ModShortcutCount(mod);
        mod.KeysText = shortcuts switch
        {
            0 => string.Empty,
            1 => T("RepositoryWorkspace.ShortcutCountOne"),
            _ => T("RepositoryWorkspace.ShortcutCount", shortcuts)
        };
    }

    // Saved bindings when the Mod has them (they may be edited), otherwise the INI scan of its key sections.
    private int ModShortcutCount(SecondLevelFolderItem mod)
        => _modBindings.TryGetValue(mod.Path, out List<ShortcutBinding>? bindings)
            ? bindings.Count(binding => !string.IsNullOrWhiteSpace(binding.Shortcut))
            : _scannedShortcutCounts.TryGetValue(mod.Path, out int scanned) ? scanned : 0;

    private readonly Dictionary<string, int> _scannedShortcutCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _shortcutCountScans = new(StringComparer.OrdinalIgnoreCase);

    // Rows of Mods without saved bindings count their INI key sections once, off the UI thread.
    private async Task EnsureModShortcutCountAsync(SecondLevelFolderItem mod)
    {
        if (_modBindings.ContainsKey(mod.Path) || _scannedShortcutCounts.ContainsKey(mod.Path) || !_shortcutCountScans.Add(mod.Path)) return;
        try
        {
            int count = await Task.Run(() => ModShortcutScanner.ScanDirectory(mod.Path, activeOnly: true)
                .Take(ShortcutScanSafetyLimit).Count(shortcut => !string.IsNullOrWhiteSpace(shortcut.Shortcut)));
            _scannedShortcutCounts[mod.Path] = count;
            ApplyModRowState(mod);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogApplicationIssue("Count Mod shortcuts", ex);
        }
        finally { _shortcutCountScans.Remove(mod.Path); }
    }

    // Summed once per scanned Mod, off the UI thread; rows and the details show the cached value.
    private async Task<long?> EnsureModSizeAsync(SecondLevelFolderItem mod)
    {
        if (mod.SizeBytes is long known) return known;
        if (_modSizeTasks.TryGetValue(mod, out Task<long?>? running)) return await running;
        Task<long?> task = SumModSizeAsync(mod);
        _modSizeTasks[mod] = task;
        try { return await task; }
        finally { _modSizeTasks.Remove(mod); }
    }

    private async Task<long?> SumModSizeAsync(SecondLevelFolderItem mod)
    {
        try
        {
            long bytes = await Task.Run(() => mod.Files.Sum(file =>
            {
                try
                {
                    var info = new FileInfo(Path.IsPathRooted(file) ? file : Path.Combine(mod.Path, file));
                    return info.Exists ? info.Length : 0L;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    return 0L;
                }
            }));
            mod.SizeBytes = bytes;
            mod.SizeText = FormatFileSize(bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Mod size", ex);
            return null;
        }
    }

    private ModStateTone ModTone(SecondLevelFolderItem mod)
        => RepositoryWorkspacePolicy.StateTone(mod.State, StateMissingText, StateCopiedText, StateLinkedText);

    private string[] LocalModTags(string path)
        => LocalModRelativePath(path) is { } relativePath ? SelectedLocalLibrary()?.TagsFor(relativePath) ?? [] : [];

    // An empty Mod list says why it is empty and, for a character without Mods, offers the next step.
    // A repository that is not set up yet also lists the steps left before the first deployment.
    private EmptyState CreateRepositoryEmptyState()
    {
        _repositoryEmptyImport = new Button { Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 36 };
        _repositoryEmptyImport.Click += (_, args) => OnImportZipClicked(ImportZipButton, args);
        _repositoryEmptyOnline = RepositoryButton("\uE774", (_, args) => OnOnlineNavClicked(OnlineNavButton, args));
        _repositoryEmptyOnline.MinHeight = 36;
        _repositoryEmptyButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        _repositoryEmptyButtons.Children.Add(_repositoryEmptyImport);
        _repositoryEmptyButtons.Children.Add(_repositoryEmptyOnline);
        _repositoryEmptyAdopt = new HyperlinkButton { HorizontalAlignment = HorizontalAlignment.Center };
        _repositoryEmptyAdopt.Click += async (_, _) => await RunUiActionAsync("ShowExistingModImportAsync", ShowExistingModImportAsync);
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(_repositoryEmptyButtons);
        actions.Children.Add(_repositoryEmptyAdopt);
        actions.Children.Add(CreateRepositorySetupCard());
        _repositoryEmptyState = new EmptyState { Actions = actions, Visibility = Visibility.Collapsed };
        return _repositoryEmptyState;
    }

    private Border CreateRepositorySetupCard()
    {
        var panel = new StackPanel { Spacing = 10 };
        _repositorySetupTitle = RepositoryText(14, true);
        _repositorySetupProgress = RepositoryText(12);
        _repositorySetupProgress.Opacity = 0.75;
        panel.Children.Add(_repositorySetupTitle);
        panel.Children.Add(_repositorySetupProgress);
        RoutedEventHandler[] handlers =
        [
            (_, args) => OnEditRepositoryDetailsClicked(RenameRepositoryButton, args),
            (_, args) => OnPickSourceClicked(PickSourceButton, args),
            (_, args) => OnPickTargetClicked(PickTargetButton, args)
        ];
        _repositorySetupSteps = handlers.Select((handler, index) =>
        {
            var row = RepositoryColumns(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
            row.ColumnSpacing = 10;
            var done = new FontIcon { Glyph = "\uE73E", FontSize = 14, Foreground = GetAppThemeBrush("AppSuccessBrush"), VerticalAlignment = VerticalAlignment.Center };
            var number = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
                BorderBrush = GetAppThemeBrush("AppStrongBorderBrush"), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var marks = new Grid { Width = 20, VerticalAlignment = VerticalAlignment.Center };
            marks.Children.Add(done);
            marks.Children.Add(number);
            var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            var title = RepositoryText(13, true);
            var detail = RepositoryText(12);
            detail.Opacity = 0.7;
            detail.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(title);
            text.Children.Add(detail);
            var action = new Button { MinHeight = 30, Padding = new Thickness(10, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
            action.Click += handler;
            row.Children.Add(marks);
            Grid.SetColumn(text, 1); row.Children.Add(text);
            Grid.SetColumn(action, 2); row.Children.Add(action);
            panel.Children.Add(row);
            return new SetupStepRow(done, number, title, detail, action);
        }).ToArray();
        _repositorySetupCard = new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, Child = panel
        };
        return _repositorySetupCard;
    }

    private RepositorySetup CurrentRepositorySetup()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        string source = (SourceTextBox.Text ?? string.Empty).Trim();
        string target = (TargetTextBox.Text ?? string.Empty).Trim();
        // The game counts as chosen when the repository has one, its own or the default preset.
        return new RepositorySetup(
            repository is not null && !string.IsNullOrWhiteSpace(GetEffectiveOnlineGameName(repository)),
            source.Length > 0 && Directory.Exists(source),
            target.Length > 0 && Directory.Exists(target));
    }

    private void UpdateRepositorySetupCard(RepositorySetup setup)
    {
        _repositorySetupTitle!.Text = T("RepositoryWorkspace.SetupTitle");
        _repositorySetupProgress!.Text = T("RepositoryWorkspace.SetupProgress", setup.Done, RepositorySetup.Total);
        WorkspaceRepository? repository = GetSelectedRepository();
        string source = (SourceTextBox.Text ?? string.Empty).Trim();
        (bool Done, string Title, string Detail, string Action)[] steps =
        [
            (setup.HasGame, T("RepositoryWorkspace.SetupGame"),
                setup.HasGame ? GetEffectiveOnlineGameName(repository) : T("RepositoryWorkspace.SetupGameHint"),
                T("RepositoryWorkspace.SetupEdit")),
            (setup.HasRepositoryFolder, T("RepositoryWorkspace.SetupRepository"),
                setup.HasRepositoryFolder ? source : T("RepositoryWorkspace.SetupRepositoryHint"), T("RepositoryWorkspace.SetupChoose")),
            (setup.HasDeployTarget, T("RepositoryWorkspace.SetupTarget"), T("RepositoryWorkspace.SetupTargetHint"), T("RepositoryWorkspace.SetupChoose"))
        ];
        for (int index = 0; index < _repositorySetupSteps.Length; index++)
        {
            SetupStepRow row = _repositorySetupSteps[index];
            row.Done.Visibility = steps[index].Done ? Visibility.Visible : Visibility.Collapsed;
            row.Number.Visibility = steps[index].Done ? Visibility.Collapsed : Visibility.Visible;
            row.Title.Text = steps[index].Title;
            row.Detail.Text = steps[index].Detail;
            row.Action.Content = steps[index].Action;
            row.Action.Visibility = steps[index].Done ? Visibility.Collapsed : Visibility.Visible;
            row.Action.IsEnabled = !_repositoryWorkspaceBusy;
        }
    }

    private void UpdateRepositoryEmptyState()
    {
        if (_repositoryEmptyState is null) return;
        _modList.UpdateEmptyKind(_secondLevelItems.Count, _characters.Selected is not null);
        ModListEmptyKind kind = _modList.EmptyKind;
        _repositoryEmptyState.Visibility = kind == ModListEmptyKind.None ? Visibility.Collapsed : Visibility.Visible;
        _repositoryEmptyState.Glyph = kind == ModListEmptyKind.NoMatches ? "\uE721" : "\uE8B5";
        string character = _characters.Selected?.Name ?? string.Empty;
        _repositoryEmptyState.Title = kind switch
        {
            ModListEmptyKind.NoMatches => T("RepositoryWorkspace.NoMatchingModsTryAnotherSearch"),
            ModListEmptyKind.NoMods => T("RepositoryWorkspace.NoModsForCharacter", character),
            _ => T("RepositoryWorkspace.SelectACharacter")
        };
        _repositoryEmptyState.Hint = kind == ModListEmptyKind.NoMods ? T("RepositoryWorkspace.DropArchivesToImport", character) : string.Empty;
        RepositorySetup setup = CurrentRepositorySetup();
        bool offerImport = kind == ModListEmptyKind.NoMods;
        bool showSetup = kind != ModListEmptyKind.NoMatches && !setup.IsComplete;
        _repositoryEmptyButtons!.Visibility = offerImport ? Visibility.Visible : Visibility.Collapsed;
        _repositoryEmptyAdopt!.Visibility = offerImport ? Visibility.Visible : Visibility.Collapsed;
        _repositorySetupCard!.Visibility = showSetup ? Visibility.Visible : Visibility.Collapsed;
        if (showSetup) UpdateRepositorySetupCard(setup);
        _repositoryEmptyState.ShowActions = offerImport || showSetup;
        _repositoryEmptyImport!.Content = RepositoryIconLabel("\uE8E5", T("Localization.ImportMod"));
        _repositoryEmptyOnline!.Content = RepositoryIconLabel("\uE774", T("RepositoryWorkspace.FindOnline"));
        _repositoryEmptyAdopt.Content = T("RepositoryWorkspace.AdoptExistingFolder");
        _repositoryEmptyImport.IsEnabled = _repositoryEmptyOnline.IsEnabled = _repositoryEmptyAdopt.IsEnabled = !_repositoryWorkspaceBusy;
        CharacterFolderHintTextBlock.Visibility = _characters.All.Sum(item => item.Children.Count) == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncRepositorySelectionV4(SecondLevelFolderItem? item)
    {
        if (!_repositoryV4Ready) return;
        _modList.SelectedMod = item;
        _syncingRepositorySelection = true;
        try
        {
            SecondLevelListView.SelectedItem = _modList.VisibleMods.Contains(item!) ? item : null;
            UpdateRepositoryCoverSelectionV4();
        }
        finally { _syncingRepositorySelection = false; }
        UpdateRepositorySelectedMetadataV4();
    }

    private void UpdateRepositoryCoverSelectionV4()
    {
        foreach (var mod in _modList.VisibleMods)
            mod.CoverSelectionThickness = new Thickness(_modList.IsSelected(mod) ? 2 : 0);
    }

    private void OnRepositoryCoverClicked(object sender, RoutedEventArgs args)
    {
        if (_syncingRepositorySelection || _repositoryWorkspaceBusy) return;
        if (sender is FrameworkElement { DataContext: SecondLevelFolderItem mod }) SecondLevelListView.SelectedItem = mod;
    }

    private async void LoadRepositoryCoverV4(SecondLevelFolderItem mod)
    {
        try
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
        catch (Exception ex)
        {
            ReportUiActionFailure("LoadRepositoryCoverV4", ex);
        }
    }

    private void UpdateRepositorySelectedMetadataV4()
    {
        if (_repositorySelectedPath is null) return;
        var mod = GetSelectedSecondLevelItem();
        _repositorySelectedPath.Text = mod is null ? T("RepositoryWorkspace.SelectAMod") : RepositoryWorkspacePolicy.DisplayPath(SourceTextBox.Text, mod.Path);
        ToolTipService.SetToolTip(_repositorySelectedPath, mod?.Path);
        RepositoryOpenModFolderButton.IsEnabled = mod is not null && !_repositoryWorkspaceBusy;
        _repositoryCopyModPath!.IsEnabled = mod is not null;
        _repositoryOpenDetails!.IsEnabled = mod is not null && !_repositoryWorkspaceBusy;
        _repositoryEditSource!.IsEnabled = _repositoryAddTags!.IsEnabled = mod is not null && !_repositoryWorkspaceBusy;
        UpdateRepositoryInfoSource();
        string[] tags = mod is null ? [] : LocalModTags(mod.Path);
        _repositoryInfoTags!.Children.Clear();
        foreach (string tag in tags)
        {
            _repositoryInfoTags.Children.Add(new Border
            {
                Padding = new Thickness(8, 1, 8, 2), CornerRadius = new CornerRadius(4),
                Background = GetAppThemeBrush("AppSecondaryDefaultBrush"),
                Child = new TextBlock { Text = tag, FontSize = 12 }
            });
        }
        _repositoryInfoNoTags!.Visibility = tags.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = UpdateRepositoryInfoSizeAsync(mod);
        UpdateRepositoryPreviewCaption(mod);
        int shortcuts = mod is null ? 0 : ModShortcutCount(mod);
        if (ShortcutTab is not null) ShortcutTab.Header = shortcuts > 0 ? T("Common.Shortcuts") + " · " + shortcuts : T("Common.Shortcuts");
        // With links, deploying replaces the character's deployed Mod (unless it keeps several); say which one.
        WorkspaceRepository? repository = GetSelectedRepository();
        bool links = repository?.UseDirectoryLinks == true;
        bool several = links && mod is not null && AllowsMultipleMods(repository!, mod.Path);
        string? replaced = mod is null || !links || ModTone(mod) == ModStateTone.Deployed || several ? null
            : RepositoryWorkspacePolicy.ReplacedByDeploy(_secondLevelItems.Select(item => (item.Name, ModTone(item))), mod.Name);
        RepositoryDeploymentHintTextBlock.Text = replaced is not null
            ? T("RepositoryWorkspace.DeployReplaces", _characters.Selected?.Name ?? string.Empty, replaced)
            : several ? T("RepositoryWorkspace.LinkDeploymentKeepsOtherMods")
            : links ? T("RepositoryWorkspace.LinkDeploymentReplacesTheSameCharacter")
            : T("RepositoryWorkspace.CopyDeploymentKeepsARecoverableBackup");
        UpdateDeployButtonLabel();
    }

    // The deploy button toggles: on a deployed Mod it removes it, so say so.
    private void UpdateDeployButtonLabel()
    {
        SecondLevelFolderItem? mod = GetSelectedSecondLevelItem();
        ToggleCopyButton.Content = mod is not null && ModTone(mod) == ModStateTone.Deployed
            ? T("LocalLibrary.RemoveFromTarget")
            : T("Localization.DeployMod");
    }

    private void UpdateRepositoryInfoSource()
    {
        if (_repositoryInfoSource is null) return;
        string link = GetSelectedSecondLevelItem() is null ? string.Empty : (ModLinkTextBox.Text ?? string.Empty).Trim();
        ((TextBlock)_repositoryInfoSource.Content).Text =
            Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) ? uri.Host + uri.AbsolutePath.TrimEnd('/') : link;
        ToolTipService.SetToolTip(_repositoryInfoSource, link.Length == 0 ? null : link);
        _repositoryInfoSource.Visibility = link.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _repositoryInfoSourceEmpty!.Visibility = link.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // The preview frame names the shown image and where it sits among the Mod's images.
    private void UpdateRepositoryPreviewCaption(SecondLevelFolderItem? mod)
    {
        if (_repositoryPreviewCaption is null || _repositoryPreviewCaptionHost is not { } caption) return;
        string? shown = mod is null ? null : FindPreviewImage(mod.Files);
        if (shown is null)
        {
            caption.Visibility = Visibility.Collapsed;
            return;
        }
        List<string> images = mod!.Files.Where(IsImageFile).ToList();
        int index = images.FindIndex(image => string.Equals(image, shown, StringComparison.OrdinalIgnoreCase));
        _repositoryPreviewCaption.Text = $"{Path.GetFileName(shown)} · {Math.Max(index, 0) + 1} / {Math.Max(images.Count, 1)}";
        caption.Visibility = Visibility.Visible;
    }

    private async Task UpdateRepositoryInfoSizeAsync(SecondLevelFolderItem? mod)
    {
        if (_repositoryInfoSize is null) return;
        int revision = ++_repositorySizeRevision;
        if (mod is null)
        {
            _repositoryInfoSize.Text = "—";
            return;
        }
        _repositoryInfoSize.Text = "…";
        long? bytes = mod.SizeBytes ?? await EnsureModSizeAsync(mod);
        if (bytes is long size && revision == _repositorySizeRevision && !_onlineWindowClosed)
            _repositoryInfoSize.Text = T("RepositoryWorkspace.SizeAndFiles", FormatFileSize(size), mod.Files.Count);
    }

    private void CopySelectedModPath()
    {
        if (GetSelectedSecondLevelItem() is not { } mod) return;
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(mod.Path);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            StatusTextBlock.Text = T("RepositoryWorkspace.PathCopied", mod.Path);
        }
        catch (Exception ex)
        {
            LogApplicationIssue("Copy Mod path", ex);
            StatusTextBlock.Text = T("RepositoryWorkspace.PathNotCopied");
        }
    }

    private void RefreshRepositoryCountsV4()
    {
        if (RepositoryCountTextBlock is null) return;
        int deployed = _characters.All.Sum(character => character.Children.Count(mod => ModTone(mod) == ModStateTone.Deployed));
        RepositoryCountTextBlock.Text = T("RepositoryWorkspace.CharactersMods", FirstCountTextBlock.Text, SecondCountTextBlock.Text, deployed);
    }

    private void RefreshRepositoryPickerV4()
    {
        if (RepositoryWorkspacePicker is null) return;
        _refreshingRepositoryPicker = true;
        try
        {
            RepositoryWorkspacePicker.Items.Clear();
            foreach (var repository in _repositories) RepositoryWorkspacePicker.Items.Add(new ComboBoxItem { Content = repository.Name, Tag = repository.Id });
            RepositoryWorkspacePicker.SelectedItem = RepositoryWorkspacePicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == _selectedRepositoryId);
            RepositoryWorkspacePicker.PlaceholderText = T("RepositoryWorkspace.SelectRepository");
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
                if (CreatePresetPreviewImage(profile.PreviewFile, 160, 90) is { } thumbnail) label.Children.Add(thumbnail);
                label.Children.Add(name);
                label.Children.Add(new TextBlock { Text = T("RepositoryWorkspace.Mods2", profile.ModRelativePaths.Count, (profile.IncludesPersistentState ? "组合 + 状态" : "仅组合"), (profile.IncludesPersistentState ? "set + state" : "set only")), FontSize = 12 });
                _combinationPresetList.Items.Add(new ListViewItem { Content = label, Tag = profile.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            _combinationPresetList.SelectedItem = _combinationPresetList.Items.OfType<ListViewItem>().FirstOrDefault(item => (string?)item.Tag == _selectedConfigurationProfileId);
            var selected = GetSelectedConfigurationProfile();
            _combinationMemberTitle!.Text = selected?.Name ?? T("RepositoryWorkspace.ModsInPreset");
            _combinationMemberPanel!.Children.Clear();
            if (selected is not null)
                _combinationMemberPanel.Children.Add(CreatePresetPreviewEditor(selected.RepositoryId, selected.Id, selected.PreviewFile,
                    file => selected.PreviewFile = file, RefreshConfigurationProfiles));
            if (selected is null)
            {
                var empty = new TextBlock { Text = T("RepositoryWorkspace.NoCombinationPresetsYetDeployYour"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 20, 8, 8) };
                _combinationMemberPanel.Children.Add(empty);
            }
            else foreach (string relative in selected.ModRelativePaths)
            {
                int values = selected.PersistentStates.FirstOrDefault(state => string.Equals(state.ModRelativePath, relative, StringComparison.OrdinalIgnoreCase))?.Values.Count ?? 0;
                var body = new StackPanel { Spacing = 5 };
                body.Children.Add(new TextBlock { Text = Path.GetFileName(relative), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new TextBlock { Text = Path.GetDirectoryName(relative), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new TextBlock { Text = selected.IncludesPersistentState ? T("RepositoryWorkspace.SavedPersistentValues", values) : T("RepositoryWorkspace.DeploymentSetOnly"), FontSize = 12 });
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
        RepositoryWorkspacePicker.IsEnabled = !busy;
        _combinationPresetList!.IsEnabled = !busy;
        foreach (var button in _repositoryModeButtons) button.IsEnabled = !busy;
        UpdateRepositorySelectedMetadataV4();
        UpdateRepositoryEmptyState();
    }

    private async Task ShowRepositoryDetailsV4Async()
    {
        if (_repositoryDetailDialogOpen || _repositoryWorkspaceBusy || GetSelectedSecondLevelItem() is null) return;
        _repositoryDetailDialogOpen = true;
        RepositoryDetach(WorkspaceDetailHost);
        WorkspaceDetailHost.Visibility = Visibility.Visible;
        WorkspaceDetailHost.Height = Math.Clamp(RootGrid.ActualHeight - 150, 320, 600);
        // The dialog can be short; scroll only its preview body, not the pinned deploy action.
        var previewContent = PreviewCard.Child;
        PreviewCard.Child = null;
        var previewScroll = new ScrollViewer
        {
            Content = previewContent,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        PreviewCard.Child = previewScroll;
        var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Content = WorkspaceDetailHost, CloseButtonText = T("Common.Close"), RequestedTheme = RootGrid.ActualTheme };
        try { await dialog.ShowAsync(); }
        finally
        {
            dialog.Content = null;
            previewScroll.Content = null;
            PreviewCard.Child = previewContent;
            WorkspaceDetailHost.Height = double.NaN;
            WorkspaceWorkbench.Children.Add(WorkspaceDetailHost);
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
        foreach (var root in new FrameworkElement?[] { RootGrid, CategoryCard, ModCard, WorkspaceActionsGrid,
            WorkspaceDetailHost, PreviewCard, ShortcutCard, UpdatesSupportPanel, WorkspaceStatusCard })
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
