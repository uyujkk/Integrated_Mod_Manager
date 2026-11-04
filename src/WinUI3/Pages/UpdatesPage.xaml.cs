using System.ComponentModel;
using System.Diagnostics;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Updates page (stage J3, docs/development/UI-Structure-Plan.md): tracked Mods, download history,
/// install safety and backups. It labels and lays itself out, builds its rows from
/// <see cref="UpdatesViewModel"/> and the download tasks, and sends clicks out as requests.
/// </summary>
public sealed partial class UpdatesPage : UserControl
{
    private const double DownloadHistoryRowSpacing = 6; // StackLayout.Spacing of DownloadHistoryRepeater

    private readonly AppServices _services;
    private ToggleButton[] _trackedModChips = [];
    private ToggleButton[] _downloadHistoryChips = [];
    // The finished tasks the page last listed; a task that finishes later rebuilds the list.
    private readonly HashSet<string> _downloadHistoryIds = [];
    private readonly Dictionary<string, DownloadTaskCardView> _downloadTaskCards = [];
    private int _updatesColumns = -1;
    // The history card height the layout chose; FitDownloadHistoryToWholeRows trims below it.
    private double? _downloadsCardHeight;
    private bool _isApplyingInterval;
    private bool _isApplyingBackupLimit;
    private bool _isApplyingConflictDetection;

    internal UpdatesPage(AppServices services)
    {
        _services = services;
        ViewModel = services.Updates;
        InitializeComponent();

        UpdatesScrollViewer.SizeChanged += (_, args) =>
        {
            // A hidden page reports zero; it lays out again when it is shown.
            if (args.NewSize.Width > 0) UpdateUpdatesLayout();
        };
        ViewModel.Refreshed += (_, _) => Render();
        ViewModel.PropertyChanged += OnViewModelChanged;
        services.Language.LanguageChanged += (_, _) => Render();
        // Rows and chips take their colours when they are built.
        services.Appearance.ThemeChanged += (_, _) => Render();
        services.Downloads.Changed += (_, _) => RenderDownloadHistory();
        services.Downloads.TaskUpdated += (_, task) => OnDownloadTaskUpdated(task);
        Render();
    }

    public UpdatesViewModel ViewModel { get; }

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private Brush ThemeBrush(string token) => ThemeTokens.Get(token, _services.Appearance.ThemeKey);

    private static Style AppStyle(string key) => (Style)Application.Current.Resources[key];

    // Labels, settings and every list, as the window used to refresh the page.
    private void Render()
    {
        UpdatesPageHeader.Title = T("Updates.ModUpdates");
        UpdatesPageHeader.Description = T("Updates.CheckInstalledModsForNewVersions");

        InstallSafetyTitleTextBlock.Text = T("Updates.InstallSafety");
        InstallSafetyHintTextBlock.Text = T("Updates.CheckFileConflictsBeforeInstallationCopies");
        ConflictDetectionToggleSwitch.Header = T("Updates.DetectConflictsBeforeInstall");
        ConflictDetectionToggleSwitch.OnContent = T("Common.On");
        ConflictDetectionToggleSwitch.OffContent = T("Common.Off");
        _isApplyingConflictDetection = true;
        ConflictDetectionToggleSwitch.IsOn = ViewModel.ConflictDetection;
        _isApplyingConflictDetection = false;
        RollbackLastInstallButton.Content = T("Updates.UndoLastModChange");
        OpenCombinationPresetsTextBlock.Text = T("Common.OpenCombinationWorkspace");
        RenderRollback();

        DownloadTaskCenterTitleTextBlock.Text = T("Updates.DownloadTasks");
        DownloadTaskCenterHintTextBlock.Text = T("Updates.ShowsOnlineModDownloadAndExtraction");
        ClearDownloadTasksButton.Content = T("Updates.ClearFinished");

        BackupManagerTitleTextBlock.Text = T("ProductivityFeatures.InstallBackups");
        BackupManagerHintTextBlock.Text = T("ProductivityFeatures.AutomaticallyPreserveTheStateBeforeInstalls");
        BackupLimitLabelTextBlock.Text = T("ProductivityFeatures.StorageLimitGB");
        RefreshBackupsButton.Content = T("ProductivityFeatures.RefreshList");
        AutomationProperties.SetName(BackupLimitNumberBox, T("ProductivityFeatures.InstallBackupStorageLimitInGB"));
        AutomationProperties.SetName(RefreshBackupsButton, RefreshBackupsButton.Content?.ToString() ?? string.Empty);
        AutomationProperties.SetName(BackupHistoryExpander, T("ProductivityFeatures.ExpandInstallBackupHistory"));
        _isApplyingBackupLimit = true;
        BackupLimitNumberBox.Value = ViewModel.BackupLimitGb;
        _isApplyingBackupLimit = false;
        RenderBackups();
        RenderDownloadHistory();

        TrackedModSettingsTitleTextBlock.Text = T("Updates.CheckSettings");
        TrackedModSettingsHintTextBlock.Text = T("Updates.CheckOnStartupAtTheSelected");
        ModUpdateIntervalLabelTextBlock.Text = T("Updates.CheckInterval");
        ModUpdateIntervalHintTextBlock.Text = T("Updates.OnlyOnlineInstalledModsWithA");
        ToolTipService.SetToolTip(ModUpdateIntervalComboBox, TrackedModSettingsHintTextBlock.Text + "\n" + ModUpdateIntervalHintTextBlock.Text);
        ToolTipService.SetToolTip(CheckModUpdatesButton, T("Localization.CheckTrackedModUpdatesNow"));
        AutomationProperties.SetName(CheckModUpdatesButton, T("Localization.CheckTrackedModUpdatesNow"));
        CheckModUpdatesButton.Content = ViewModel.IsCheckingModUpdates ? T("Common.Checking") : T("Updates.CheckNow");
        TrackedModsTitleTextBlock.Text = T("Updates.TrackedMods");
        ModUpdateStatusTextBlock.Text = ViewModel.ModUpdateStatus;
        TrackedModsSummaryTextBlock.Text = ViewModel.TrackedSummary;
        PopulateModUpdateIntervalOptions();
        RenderTrackedMods();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UpdatesViewModel.Backups):
                RenderBackups();
                break;
            case nameof(UpdatesViewModel.CanUndoLastInstall):
                RenderRollback();
                break;
            case nameof(UpdatesViewModel.ModUpdateIntervalIndex):
                SelectModUpdateInterval();
                break;
            case nameof(UpdatesViewModel.BackupLimitGb):
                if (BackupLimitNumberBox.Value != ViewModel.BackupLimitGb)
                {
                    _isApplyingBackupLimit = true;
                    BackupLimitNumberBox.Value = ViewModel.BackupLimitGb;
                    _isApplyingBackupLimit = false;
                }
                break;
        }
    }

    // Tracked Mods: filters, search and order come from the view model; the repeater only builds visible rows.
    private void RenderTrackedMods()
    {
        IReadOnlyList<TrackedModRow> entries = ViewModel.TrackedMods;
        if (_trackedModChips.Length == 0)
            _trackedModChips = CreateFilterChips(TrackedModsFilterPanel, ViewModel.TrackedModFilter, filter =>
            {
                ViewModel.TrackedModFilter = filter;
                RenderTrackedMods();
            });
        foreach (ToggleButton chip in _trackedModChips)
        {
            var filter = (TrackedModFilter)chip.Tag;
            int count = ViewModel.CountFor(filter);
            chip.Content = filter switch
            {
                TrackedModFilter.Updates => T("Updates.FilterUpdates", count),
                TrackedModFilter.Failed => T("Updates.FilterFailed", count),
                TrackedModFilter.UpToDate => T("Updates.FilterUpToDate", count),
                _ => T("Updates.FilterAll", count)
            };
            chip.IsChecked = filter == ViewModel.TrackedModFilter;
        }
        TrackedModsSearchBox.PlaceholderText = T("Updates.SearchTrackedMods");
        TrackedModsFilterPanel.Visibility = TrackedModsSearchBox.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        IReadOnlyList<TrackedModRow> visible = ViewModel.VisibleTrackedMods;
        if (TrackedModsSearchBox.Parent is Grid toolbar && toolbar.Tag is null)
        {
            toolbar.Tag = true;
            toolbar.SizeChanged += (_, _) => LayoutTrackedModsToolbar();
        }
        LayoutTrackedModsToolbar();
        TrackedModsRepeater.ItemTemplate ??= new DelegateElementFactory(item => CreateTrackedModRow((TrackedModRow)item));
        TrackedModsRepeater.ItemsSource = visible;

        if (entries.Count == 0)
        {
            // Mods are tracked once they come from the online page, so the empty state leads there.
            var empty = new StackPanel { Spacing = 10 };
            empty.Children.Add(CreateInfoCard(T("Updates.NoTrackedOnlineModsYet"), T("Updates.AfterDownloadingAndExtractingFromThe")));
            var browse = new Button { Content = T("Dashboard.BrowseOnline"), Style = AppStyle("PrimaryButtonStyle") };
            browse.Click += (_, _) => ViewModel.Request(UpdatesAction.BrowseOnline);
            empty.Children.Add(browse);
            TrackedModsEmptyHost.Content = empty;
        }
        else
        {
            TrackedModsEmptyHost.Content = visible.Count == 0
                ? new EmptyState { Glyph = "", Title = T("Updates.NoMatchingTrackedMods") }
                : null;
        }
    }

    private void OnTrackedModsSearchChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Search = TrackedModsSearchBox.Text;
        RenderTrackedMods();
    }

    // The search box sits beside the chips while they fit, and drops to its own row when they don't.
    private void LayoutTrackedModsToolbar()
    {
        // A hidden toolbar (no tracked Mods) gets no rows, so their spacing takes no room either.
        if (TrackedModsSearchBox.Parent is not Grid toolbar || toolbar.ActualWidth <= 0
            || TrackedModsSearchBox.Visibility != Visibility.Visible) return;
        TrackedModsFilterPanel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        bool besideChips = toolbar.ActualWidth >= TrackedModsFilterPanel.DesiredSize.Width + 12 + 220;
        if (toolbar.RowDefinitions.Count == 0)
        {
            toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            toolbar.RowSpacing = 8;
        }
        toolbar.ColumnDefinitions[1].Width = besideChips ? new GridLength(220) : new GridLength(0);
        toolbar.ColumnSpacing = besideChips ? 12 : 0;
        Grid.SetRow(TrackedModsSearchBox, besideChips ? 0 : 1);
        Grid.SetColumn(TrackedModsSearchBox, besideChips ? 1 : 0);
        toolbar.RowDefinitions[1].Height = besideChips ? new GridLength(0) : GridLength.Auto;
    }

    // A row of single-choice chips; the caller keeps the choice and rebuilds its list.
    private ToggleButton[] CreateFilterChips<TFilter>(Panel host, TFilter selected, Action<TFilter> choose) where TFilter : struct, Enum
    {
        host.Children.Clear();
        return Enum.GetValues<TFilter>().Select(filter =>
        {
            var chip = new ToggleButton
            {
                MinHeight = 28, Padding = new Thickness(10, 2, 10, 2), CornerRadius = new CornerRadius(14),
                Tag = filter, IsChecked = EqualityComparer<TFilter>.Default.Equals(filter, selected)
            };
            foreach (string state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
            {
                chip.Resources["ToggleButtonBackground" + state] = ThemeBrush("AppSecondarySelectedBrush");
                chip.Resources["ToggleButtonForeground" + state] = ThemeBrush("AppSecondarySelectedForegroundBrush");
                chip.Resources["ToggleButtonBorderBrush" + state] = ThemeBrush("AppNavSelectedBorderBrush");
            }
            chip.Click += (_, _) => choose(filter);
            host.Children.Add(chip);
            return chip;
        }).ToArray();
    }

    private static string FormatDateForDisplay(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";

    // Compact row: thumbnail, title with recorded and latest dates, a status pill and the actions.
    private UIElement CreateTrackedModRow(TrackedModRow row)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(6),
            Background = ThemeBrush("AppPreviewBackgroundBrush")
        };
        if (!string.IsNullOrWhiteSpace(row.PreviewUrl))
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            thumb.Child = image;
            _ = _services.Images.LoadCachedAsync(image, row.PreviewUrl);
        }
        else thumb.Child = new FontIcon { Glyph = "", FontSize = 16, Opacity = 0.6 };
        grid.Children.Add(thumb);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = row.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(title, row.Title + "\n" + T("Common.LabelValue", T("Updates.LocalPath"), row.Path));
        text.Children.Add(title);
        text.Children.Add(new TextBlock
        {
            // Common.LabelValue puts a full-width colon after a Chinese label.
            Text = T("Common.LabelValue", T("Updates.Recorded"), FormatDateForDisplay(row.LastKnownUpdatedAt)) + "  ·  "
                + T("Common.LabelValue", T("Updates.Latest"), FormatDateForDisplay(row.LatestUpdatedAt)),
            Style = AppStyle("CaptionTextStyle"), MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
        });
        // Only a failed check needs its message on the row; other results are said by the pill.
        if (row.Status == TrackedModStatus.Failed && row.FailureText is not null)
        {
            var message = new TextBlock
            {
                Text = row.FailureText, FontSize = 12, Foreground = ThemeBrush("AppDangerBrush"),
                MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTipService.SetToolTip(message, message.Text);
            text.Children.Add(message);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        (string Label, string Background, string Foreground) pill = row.Status switch
        {
            TrackedModStatus.Update => (T("Updates.Update"), "AppAccentSoftBrush", "AppAccentBrush"),
            TrackedModStatus.Failed => (T("Updates.CheckFailed"), "AppDangerSoftBrush", "AppDangerBrush"),
            TrackedModStatus.UpToDate => (T("Updates.UpToDate"), "AppSuccessSoftBrush", "AppSuccessBrush"),
            _ => (T("Updates.NotChecked"), "AppSecondaryDefaultBrush", "AppSecondaryDefaultForegroundBrush")
        };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Background = ThemeBrush(pill.Background),
            Child = new TextBlock { Text = pill.Label, FontSize = 12, Foreground = ThemeBrush(pill.Foreground) }
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (row.HasUpdate)
        {
            var replaceButton = new Button
            {
                Content = T("Common.DownloadAndReplace"), Style = AppStyle("PrimaryButtonStyle"), MinHeight = 32,
                IsEnabled = !ViewModel.IsReplacing(row.Path) && Directory.Exists(row.Path)
            };
            replaceButton.Click += (_, _) => ViewModel.Request(UpdatesAction.DownloadAndReplace, row.Source);
            actions.Children.Add(replaceButton);
        }
        var openPageButton = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 }, Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(0),
            Style = AppStyle("SecondaryButtonStyle"), IsEnabled = !string.IsNullOrWhiteSpace(row.ProfileUrl)
        };
        ToolTipService.SetToolTip(openPageButton, T("Updates.OpenPage"));
        AutomationProperties.SetName(openPageButton, T("Updates.OpenPage"));
        openPageButton.Click += (_, _) => ViewModel.Request(UpdatesAction.OpenModPage, row.ProfileUrl);
        actions.Children.Add(openPageButton);

        // Folder, old backup and removal live in the "More" menu.
        var more = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var openFolderItem = new MenuFlyoutItem
        {
            Text = T("Updates.OpenFolder"), Icon = new FontIcon { Glyph = "" }, IsEnabled = Directory.Exists(row.Path)
        };
        openFolderItem.Click += (_, _) =>
        {
            if (Directory.Exists(row.Path))
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{row.Path}\"", UseShellExecute = true });
        };
        more.Items.Add(openFolderItem);
        if (!string.IsNullOrWhiteSpace(row.BackupPath))
        {
            var backupItem = new MenuFlyoutItem { Text = T("Updates.OpenOldBackup"), Icon = new FontIcon { Glyph = "" } };
            backupItem.Click += (_, _) => ViewModel.Request(UpdatesAction.OpenOldBackup, row.BackupPath);
            more.Items.Add(backupItem);
        }
        more.Items.Add(new MenuFlyoutSeparator());
        var removeTrackingItem = new MenuFlyoutItem
        {
            Text = T("Updates.RemoveTracking"), Icon = new FontIcon { Glyph = "" }, IsEnabled = !ViewModel.IsReplacing(row.Path)
        };
        removeTrackingItem.Click += (_, _) => ViewModel.Request(UpdatesAction.RemoveTracking, row.Source);
        more.Items.Add(removeTrackingItem);
        var moreButton = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 }, Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(0),
            Style = AppStyle("SecondaryButtonStyle"), Flyout = more
        };
        ToolTipService.SetToolTip(moreButton, T("Updates.More"));
        AutomationProperties.SetName(moreButton, T("Updates.More"));
        actions.Children.Add(moreButton);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        return new Border { Style = AppStyle("InsetBorderStyle"), Padding = new Thickness(10), Child = grid };
    }

    private static UIElement CreateInfoCard(string title, string body) => new Border
    {
        Style = AppStyle("InsetBorderStyle"),
        Padding = new Thickness(14),
        Child = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = body, Style = AppStyle("MutedTextStyle"), TextWrapping = TextWrapping.Wrap }
            }
        }
    };

    private void PopulateModUpdateIntervalOptions()
    {
        string[] labels =
        [
            T("ModUpdates.ManualOnly"),
            T("ModUpdates.CheckDailyOnStartup"),
            T("ModUpdates.CheckEvery3DaysOnStartup"),
            T("ModUpdates.CheckWeeklyOnStartup")
        ];
        // Relabel in place: replacing the items of an open list in its SelectionChanged would break it.
        if (ModUpdateIntervalComboBox.Items.Count == 0)
        {
            foreach (string label in labels) ModUpdateIntervalComboBox.Items.Add(new ComboBoxItem { Content = label });
        }
        bool relabeled = false;
        for (int index = 0; index < labels.Length; index++)
        {
            var item = (ComboBoxItem)ModUpdateIntervalComboBox.Items[index];
            if (Equals(item.Content, labels[index])) continue;
            item.Content = labels[index];
            relabeled = true;
        }
        // A closed combo box keeps showing the old text of a relabeled selection until it is selected again.
        if (relabeled && !ModUpdateIntervalComboBox.IsDropDownOpen)
        {
            _isApplyingInterval = true;
            ModUpdateIntervalComboBox.SelectedItem = null;
            _isApplyingInterval = false;
        }
        SelectModUpdateInterval();
    }

    private void SelectModUpdateInterval()
    {
        if (ModUpdateIntervalComboBox.SelectedIndex == ViewModel.ModUpdateIntervalIndex) return;
        _isApplyingInterval = true;
        ModUpdateIntervalComboBox.SelectedIndex = ViewModel.ModUpdateIntervalIndex;
        _isApplyingInterval = false;
    }

    private void OnModUpdateIntervalSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingInterval || ModUpdateIntervalComboBox.SelectedIndex < 0) return;
        ViewModel.ModUpdateIntervalIndex = ModUpdateIntervalComboBox.SelectedIndex;
    }

    // Download history: finished, failed and canceled downloads. Running ones are followed in the
    // navigation's task flyout, so the page only points there.
    private void RenderDownloadHistory()
    {
        _downloadTaskCards.Clear();
        IList<DownloadTaskItem> tasks = _services.Downloads.Tasks;
        DownloadTaskItem[] finished = [.. tasks.Where(item => DownloadProgressPolicy.IsTerminal(item.State))];
        _downloadHistoryIds.Clear();
        foreach (DownloadTaskItem task in finished) _downloadHistoryIds.Add(task.Id);
        int running = tasks.Count - finished.Length;
        DownloadHistoryRunningTextBlock.Text = running > 0 ? T("DownloadTasks.RunningInNavigation", running) : string.Empty;
        DownloadHistoryRunningTextBlock.Visibility = running > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearDownloadTasksButton.IsEnabled = finished.Length > 0;

        if (_downloadHistoryChips.Length == 0)
            _downloadHistoryChips = CreateFilterChips(DownloadHistoryFilterPanel, ViewModel.HistoryFilter, filter =>
            {
                ViewModel.HistoryFilter = filter;
                RenderDownloadHistory();
            });
        foreach (ToggleButton chip in _downloadHistoryChips)
        {
            var filter = (DownloadHistoryFilter)chip.Tag;
            int count = finished.Count(task => UpdatesListPolicy.Matches(task.State, filter));
            chip.Content = filter switch
            {
                DownloadHistoryFilter.Completed => T("DownloadTasks.FilterCompleted", count),
                DownloadHistoryFilter.Failed => T("DownloadTasks.FilterFailed", count),
                DownloadHistoryFilter.Canceled => T("DownloadTasks.FilterCanceled", count),
                _ => T("DownloadTasks.FilterAll", count)
            };
            chip.IsChecked = filter == ViewModel.HistoryFilter;
        }
        DownloadHistoryFilterPanel.Visibility = finished.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        List<DownloadTaskItem> visible = [.. finished.Where(task => UpdatesListPolicy.Matches(task.State, ViewModel.HistoryFilter))];
        if (DownloadHistoryRepeater.ItemTemplate is null)
        {
            DownloadHistoryRepeater.ItemTemplate = new DelegateElementFactory(item => CreateDownloadHistoryRow((DownloadTaskItem)item));
            // Row heights are known once the first row is built; fit the card to whole rows then.
            DownloadHistoryRepeater.ElementPrepared += (_, args) => { if (args.Index == 0) QueueFitDownloadHistory(); };
        }
        DownloadHistoryRepeater.ItemsSource = visible;
        QueueFitDownloadHistory();
        DownloadHistoryEmptyHost.Content = finished.Length == 0
            ? CreateInfoCard(T("DownloadTasks.NoDownloadTasks"), T("DownloadTasks.DownloadsStartedFromTheOnlineBrowser"))
            : visible.Count == 0 ? new EmptyState { Glyph = "", Title = T("DownloadTasks.NoMatchingHistory") } : null;
    }

    private void OnDownloadTaskUpdated(DownloadTaskItem task)
    {
        // A download that just finished joins the history.
        if (DownloadProgressPolicy.IsTerminal(task.State) && !_downloadHistoryIds.Contains(task.Id))
        {
            RenderDownloadHistory();
        }
        if (_downloadTaskCards.TryGetValue(task.Id, out DownloadTaskCardView? view))
        {
            DownloadTaskViews.Refresh(task, view, _services.Language, _services.Downloads.IsRetryBlocked());
        }
        ClearDownloadTasksButton.IsEnabled = _services.Downloads.Tasks.Any(item => DownloadProgressPolicy.IsTerminal(item.State));
    }

    // Compact history row: outcome icon, title with its last status, and open folder or retry.
    private UIElement CreateDownloadHistoryRow(DownloadTaskItem task)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        (string Glyph, string Brush) outcome = task.State switch
        {
            DownloadTaskState.Completed => ("", "AppSuccessBrush"),
            DownloadTaskState.Failed => ("", "AppDangerBrush"),
            _ => ("", "AppSecondaryDefaultForegroundBrush")
        };
        grid.Children.Add(new FontIcon
        {
            Glyph = outcome.Glyph, FontSize = 16, Foreground = ThemeBrush(outcome.Brush), VerticalAlignment = VerticalAlignment.Center
        });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = task.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
        });
        var status = new TextBlock
        {
            Style = AppStyle("CaptionTextStyle"), MaxLines = 2, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.Children.Add(status);
        // Finished tasks show no bar; the card view still owns one so it refreshes like the flyout's cards.
        var progress = new ProgressBar { Visibility = Visibility.Collapsed };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var action = new Button
        {
            MinWidth = 88,
            MinHeight = 32,
            Style = AppStyle("SecondaryButtonStyle"),
            VerticalAlignment = VerticalAlignment.Center
        };
        action.Click += (_, _) => ViewModel.Request(UpdatesAction.DownloadTaskAction, task);
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);
        var view = new DownloadTaskCardView(status, progress, action);
        _downloadTaskCards[task.Id] = view;
        DownloadTaskViews.Refresh(task, view, _services.Language, _services.Downloads.IsRetryBlocked());
        ToolTipService.SetToolTip(status, DownloadTaskViews.StatusText(task, _services.Language));
        return new Border { Style = AppStyle("InsetBorderStyle"), Padding = new Thickness(10), Child = grid };
    }

    private void QueueFitDownloadHistory()
        => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FitDownloadHistoryToWholeRows);

    // A fixed-height history card ends its list on a whole row instead of cutting the last one in half.
    private void FitDownloadHistoryToWholeRows()
    {
        if (_downloadsCardHeight is not double full || DownloadHistoryRepeater.TryGetElement(0) is not FrameworkElement first
            || first.ActualHeight <= 0 || DownloadHistoryListScroll.ActualHeight <= 0) return;
        double viewport = DownloadHistoryListScroll.ActualHeight + (full - UpdatesDownloadsCard.Height);
        double pitch = first.ActualHeight + DownloadHistoryRowSpacing;
        int count = DownloadHistoryRepeater.ItemsSourceView?.Count ?? 0;
        double whole = Math.Floor((viewport + DownloadHistoryRowSpacing) / pitch) * pitch - DownloadHistoryRowSpacing;
        double target = count * pitch - DownloadHistoryRowSpacing <= viewport || whole <= 0 ? full : full - (viewport - whole);
        if (Math.Abs(UpdatesDownloadsCard.Height - target) > 0.5) UpdatesDownloadsCard.Height = target;
    }

    // With nothing to undo the button would only sit disabled; the line below says why,
    // and the combination shortcut beside it takes the full row.
    private void RenderRollback()
    {
        bool canUndo = ViewModel.CanUndoLastInstall;
        RollbackLastInstallButton.IsEnabled = canUndo;
        InstallRollbackStatusTextBlock.Text = ViewModel.RollbackStatus;
        RollbackLastInstallButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        UpdatesRecoveryActionsGrid.ColumnDefinitions[0].Width = canUndo ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        UpdatesRecoveryActionsGrid.ColumnSpacing = canUndo ? 8 : 0;
    }

    private void RenderBackups()
    {
        BackupListPanel.Children.Clear();
        BackupSummaryTextBlock.Text = ViewModel.BackupSummary;
        if (ViewModel.Backups.Count == 0)
        {
            BackupListPanel.Children.Add(new TextBlock
            {
                Text = T("ProductivityFeatures.NoInstallBackupsYet"),
                Style = AppStyle("CaptionTextStyle")
            });
            return;
        }

        foreach (InstallBackupRow backup in ViewModel.Backups)
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var details = new StackPanel { Spacing = 3 };
            details.Children.Add(new TextBlock { Text = backup.Description, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            details.Children.Add(new TextBlock
            {
                Text = backup.Details,
                Style = AppStyle("CaptionTextStyle"),
                TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(details);
            var restore = new Button
            {
                Content = T("ProductivityFeatures.Restore"),
                Style = AppStyle("SecondaryButtonStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(restore, 1);
            AutomationProperties.SetName(restore, T("ProductivityFeatures.RestoreBackup", backup.Description));
            restore.Click += (_, _) => ViewModel.Request(UpdatesAction.RestoreBackup, backup.Path);
            grid.Children.Add(restore);
            var border = new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                Background = ThemeBrush("AppInsetBackgroundBrush"),
                BorderBrush = ThemeBrush("AppCardBorderBrush"),
                BorderThickness = new Thickness(1),
                Child = grid
            };
            BackupListPanel.Children.Add(border);
            AnimateEntrance(border);
        }
    }

    private void AnimateEntrance(UIElement element)
    {
        if (_services.Appearance.ReduceAnimations())
        {
            return;
        }

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = 0;
        visual.Offset = new System.Numerics.Vector3(0, 8, 0);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(150);
        fade.InsertKeyFrame(1, 1);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Duration = TimeSpan.FromMilliseconds(180);
        slide.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Offset", slide);
    }

    private void OnBackupLimitValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isApplyingBackupLimit || double.IsNaN(args.NewValue)) return;
        ViewModel.BackupLimitGb = args.NewValue;
    }

    private void OnConflictDetectionToggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingConflictDetection) return;
        ViewModel.ConflictDetection = ConflictDetectionToggleSwitch.IsOn;
    }

    // Clicks the window answers.
    private void OnCheckModUpdatesClicked(object sender, RoutedEventArgs e) => ViewModel.Request(UpdatesAction.CheckModUpdates);

    private void OnClearDownloadTasksClicked(object sender, RoutedEventArgs e) => ViewModel.Request(UpdatesAction.ClearFinishedDownloads);

    private void OnRollbackLastInstallClicked(object sender, RoutedEventArgs e) => ViewModel.Request(UpdatesAction.RollbackLastInstall);

    private void OnOpenCombinationPresetsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(UpdatesAction.OpenCombinationPresets);

    private void OnRefreshBackupsClicked(object sender, RoutedEventArgs e) => ViewModel.Request(UpdatesAction.RefreshBackups);

    // Hidden pages have no size yet, so they estimate from the window minus the navigation.
    private double PageWidth()
        => UpdatesScrollViewer.ActualWidth > 0 ? UpdatesScrollViewer.ActualWidth : Math.Max(420, (XamlRoot?.Size.Width ?? 0) - 240);

    // Laid out from the page's own width, not the window's.
    private void UpdateUpdatesLayout()
    {
        double width = PageWidth();
        int columns = UpdatesLayoutPolicy.Columns(width);

        // Side-by-side columns fill the window and scroll inside, so the page itself never scrolls;
        // a stacked or very short page scrolls as a whole, with each list capped in height.
        double? fixedHeight = columns > 1
            ? UpdatesLayoutPolicy.FixedColumnHeight(UpdatesScrollViewer.ActualHeight, UpdatesPageHeader.ActualHeight)
            : null;
        UpdatesScrollViewer.VerticalScrollMode = fixedHeight is null ? ScrollMode.Enabled : ScrollMode.Disabled;
        UpdatesScrollViewer.VerticalScrollBarVisibility = fixedHeight is null ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        UpdatesTrackedModsCard.MinHeight = 0;
        // A narrow history card keeps its description in the title's tooltip, leaving the rows the room.
        bool narrowHistory = columns == 2;
        DownloadTaskCenterHintTextBlock.Visibility = narrowHistory ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(DownloadTaskCenterTitleTextBlock, narrowHistory ? DownloadTaskCenterHintTextBlock.Text : null);
        if (fixedHeight is double height)
        {
            UpdatesTrackedModsCard.Height = height;
            UpdatesDownloadsCard.Height = columns == 3 ? height : Math.Max(360, Math.Round(height * 0.65));
            _downloadsCardHeight = UpdatesDownloadsCard.Height;
            UpdatesSupportScroll.Height = height;
            TrackedModsListScroll.MaxHeight = double.PositiveInfinity;
            DownloadHistoryListScroll.MaxHeight = double.PositiveInfinity;
            QueueFitDownloadHistory();
        }
        else
        {
            _downloadsCardHeight = null;
            UpdatesTrackedModsCard.Height = double.NaN;
            UpdatesDownloadsCard.Height = double.NaN;
            UpdatesSupportScroll.Height = double.NaN;
            double viewport = UpdatesScrollViewer.ActualHeight > 0 ? UpdatesScrollViewer.ActualHeight : 800;
            TrackedModsListScroll.MaxHeight = Math.Max(320, Math.Round(viewport * 0.6));
            DownloadHistoryListScroll.MaxHeight = 360;
        }

        if (_updatesColumns == columns)
        {
            return;
        }
        _updatesColumns = columns;

        // Download history gets its own middle column on very wide pages; otherwise it heads the
        // support column, which sits beside tracked Mods or below them.
        UpdatesSupportPanel.Children.Remove(UpdatesDownloadsCard);
        UpdatesTopGrid.Children.Remove(UpdatesDownloadsCard);
        UpdatesPrimaryColumn.Width = new GridLength(columns == 3 ? 1.3 : columns == 2 ? 1.35 : 1, GridUnitType.Star);
        UpdatesSecondaryColumn.Width = columns == 1 ? new GridLength(0) : new GridLength(columns == 3 ? 1 : 0.85, GridUnitType.Star);
        UpdatesTertiaryColumn.Width = columns == 3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        UpdatesTopGrid.ColumnSpacing = columns == 1 ? 0 : 16;
        if (columns == 3)
        {
            UpdatesDownloadsCard.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(UpdatesDownloadsCard, 0);
            Grid.SetColumn(UpdatesDownloadsCard, 1);
            UpdatesTopGrid.Children.Add(UpdatesDownloadsCard);
            Grid.SetRow(UpdatesSupportScroll, 0);
            Grid.SetColumn(UpdatesSupportScroll, 2);
        }
        else
        {
            UpdatesSupportPanel.Children.Insert(0, UpdatesDownloadsCard);
            Grid.SetRow(UpdatesSupportScroll, columns == 2 ? 0 : 1);
            Grid.SetColumn(UpdatesSupportScroll, columns == 2 ? 1 : 0);
        }
    }
}
