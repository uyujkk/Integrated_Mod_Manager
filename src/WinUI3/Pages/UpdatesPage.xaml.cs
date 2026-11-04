using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.WinUI.Controls;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Updates page (stages J3 and J3-b, docs/development/UI-Structure-Plan.md and UI-Fluent-Design-Plan.md):
/// a status area, then three views on a selector bar: Mod updates, download history, and install and
/// recovery. It labels itself, builds its rows from <see cref="UpdatesViewModel"/> and the download tasks,
/// and sends clicks out as requests.
/// </summary>
public sealed partial class UpdatesPage : UserControl
{
    private readonly AppServices _services;
    // The finished tasks the page last listed; a task that finishes later rebuilds the list.
    private readonly HashSet<string> _downloadHistoryIds = [];
    private readonly Dictionary<string, DownloadTaskCardView> _downloadTaskCards = [];
    private bool _isApplyingInterval;
    private bool _isApplyingBackupLimit;
    private bool _isApplyingConflictDetection;
    private bool _isApplyingFilter;
    private bool _isApplyingView;

    internal UpdatesPage(AppServices services)
    {
        _services = services;
        ViewModel = services.Updates;
        InitializeComponent();

        foreach (TrackedModFilter filter in Enum.GetValues<TrackedModFilter>())
            TrackedModsFilterSegmented.Items.Add(new SegmentedItem { Tag = filter });
        foreach (DownloadHistoryFilter filter in Enum.GetValues<DownloadHistoryFilter>())
            DownloadHistoryFilterSegmented.Items.Add(new SegmentedItem { Tag = filter });
        TrackedModsToolbar.SizeChanged += (_, _) => LayoutTrackedModsToolbar();
        UpdatesCheckInfoBar.ActionButton = new Button();
        ((Button)UpdatesCheckInfoBar.ActionButton).Click += (_, _) =>
        {
            ViewModel.View = UpdatesPageView.Mods;
            ViewModel.TrackedModFilter = TrackedModFilter.Failed;
            RenderTrackedMods();
        };

        ViewModel.Refreshed += (_, _) => Render();
        ViewModel.PropertyChanged += OnViewModelChanged;
        services.Language.LanguageChanged += (_, _) => Render();
        // Rows and the status icon take their colours when they are built.
        services.Appearance.ThemeChanged += (_, _) => Render();
        services.Downloads.Changed += (_, _) => RenderDownloadHistory();
        services.Downloads.TaskUpdated += (_, task) => OnDownloadTaskUpdated(task);
        ShowView();
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
        UpdatesViewMods.Text = T("Updates.ViewModUpdates");
        UpdatesViewDownloads.Text = T("Updates.ViewDownloadHistory");
        UpdatesViewRecovery.Text = T("Updates.ViewInstallAndRecovery");
        AutomationProperties.SetName(UpdatesViewSelector, T("Updates.ModUpdates"));

        // Status area.
        ToolTipService.SetToolTip(CheckModUpdatesButton, T("Localization.CheckTrackedModUpdatesNow"));
        AutomationProperties.SetName(CheckModUpdatesButton, T("Localization.CheckTrackedModUpdatesNow"));
        CheckModUpdatesButton.Content = ViewModel.IsCheckingModUpdates ? T("Common.Checking") : T("Updates.CheckNow");
        ModUpdateStatusTextBlock.Text = ViewModel.ModUpdateStatus;
        TrackedModsSummaryTextBlock.Text = ViewModel.TrackedSummary;
        RenderStatus();

        // Mod updates.
        SetCard(ModUpdateIntervalSettingsCard, T("Updates.CheckInterval"),
            T("Updates.CheckOnStartupAtTheSelected") + " " + T("Updates.OnlyOnlineInstalledModsWithA"));
        AutomationProperties.SetName(ModUpdateIntervalComboBox, T("Updates.CheckInterval"));
        PopulateModUpdateIntervalOptions();
        RenderTrackedMods();

        // Download history.
        DownloadTaskCenterHintTextBlock.Text = T("Updates.ShowsOnlineModDownloadAndExtraction");
        ClearDownloadTasksButton.Content = T("Updates.ClearFinished");
        RenderDownloadHistory();

        // Install and recovery.
        SetCard(ConflictDetectionSettingsCard, T("Updates.DetectConflictsBeforeInstall"), T("Updates.CheckFileConflictsBeforeInstallationCopies"));
        ConflictDetectionToggleSwitch.OnContent = T("Common.On");
        ConflictDetectionToggleSwitch.OffContent = T("Common.Off");
        AutomationProperties.SetName(ConflictDetectionToggleSwitch, T("Updates.DetectConflictsBeforeInstall"));
        _isApplyingConflictDetection = true;
        ConflictDetectionToggleSwitch.IsOn = ViewModel.ConflictDetection;
        _isApplyingConflictDetection = false;
        SetCard(OpenCombinationPresetsButton, T("Common.OpenCombinationWorkspace"), T("Updates.SaveEnabledModsAndPersistentState"));
        RenderRollback();
        BackupHistoryExpander.Header = T("ProductivityFeatures.InstallBackups");
        BackupHistoryExpander.Description = T("ProductivityFeatures.AutomaticallyPreserveTheStateBeforeInstalls");
        AutomationProperties.SetName(BackupHistoryExpander, T("ProductivityFeatures.ExpandInstallBackupHistory"));
        BackupLimitLabelTextBlock.Text = T("ProductivityFeatures.StorageLimitGB");
        AutomationProperties.SetName(BackupLimitNumberBox, T("ProductivityFeatures.InstallBackupStorageLimitInGB"));
        _isApplyingBackupLimit = true;
        BackupLimitNumberBox.Value = ViewModel.BackupLimitGb;
        _isApplyingBackupLimit = false;
        RenderBackups();
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
            case nameof(UpdatesViewModel.Backups):
                RenderBackups();
                break;
            case nameof(UpdatesViewModel.CanUndoLastInstall):
                RenderRollback();
                break;
            case nameof(UpdatesViewModel.ModUpdateIntervalIndex):
                SelectModUpdateInterval();
                break;
            case nameof(UpdatesViewModel.View):
                ShowView();
                break;
            case nameof(UpdatesViewModel.CheckState):
                RenderStatus();
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

    // Views: the selector bar picks one; the others are collapsed and keep their state.
    private void ShowView()
    {
        UpdatesPageView view = ViewModel.View;
        _isApplyingView = true;
        UpdatesViewSelector.SelectedItem = view switch
        {
            UpdatesPageView.Downloads => UpdatesViewDownloads,
            UpdatesPageView.Recovery => UpdatesViewRecovery,
            _ => UpdatesViewMods
        };
        _isApplyingView = false;
        ModsViewScroll.Visibility = view == UpdatesPageView.Mods ? Visibility.Visible : Visibility.Collapsed;
        DownloadsViewScroll.Visibility = view == UpdatesPageView.Downloads ? Visibility.Visible : Visibility.Collapsed;
        RecoveryViewScroll.Visibility = view == UpdatesPageView.Recovery ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnUpdatesViewSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_isApplyingView || sender.SelectedItem?.Tag is not string tag) return;
        ViewModel.View = Enum.Parse<UpdatesPageView>(tag);
    }

    // Status area: an icon and colour for the state, a bar while checking, and an InfoBar for failures.
    private void RenderStatus()
    {
        ModUpdateCheckState state = ViewModel.CheckState;
        (string Glyph, string Background, string Foreground) look = state switch
        {
            ModUpdateCheckState.UpdatesAvailable => ("", "AppAccentSoftBrush", "AppAccentBrush"),
            ModUpdateCheckState.UpToDate => ("", "AppSuccessSoftBrush", "AppSuccessBrush"),
            ModUpdateCheckState.SomeFailed or ModUpdateCheckState.CheckFailed => ("", "AppDangerSoftBrush", "AppDangerBrush"),
            _ => ("", "AppSecondaryDefaultBrush", "AppSecondaryDefaultForegroundBrush")
        };
        UpdatesStatusGlyph.Glyph = look.Glyph;
        UpdatesStatusGlyph.Foreground = ThemeBrush(look.Foreground);
        UpdatesStatusIconBorder.Background = ThemeBrush(look.Background);
        CheckModUpdatesButton.Content = ViewModel.IsCheckingModUpdates ? T("Common.Checking") : T("Updates.CheckNow");
        UpdatesCheckProgressBar.Visibility = state == ModUpdateCheckState.Checking ? Visibility.Visible : Visibility.Collapsed;

        int failed = ViewModel.CountFor(TrackedModFilter.Failed);
        var showFailed = (Button)UpdatesCheckInfoBar.ActionButton;
        if (state == ModUpdateCheckState.CheckFailed)
        {
            UpdatesCheckInfoBar.Severity = InfoBarSeverity.Error;
            UpdatesCheckInfoBar.Title = T("Updates.UpdateCheckFailed");
            UpdatesCheckInfoBar.Message = ViewModel.CheckError ?? string.Empty;
            showFailed.Visibility = Visibility.Collapsed;
            UpdatesCheckInfoBar.IsOpen = true;
        }
        else if (failed > 0 && state != ModUpdateCheckState.Checking)
        {
            UpdatesCheckInfoBar.Severity = InfoBarSeverity.Warning;
            UpdatesCheckInfoBar.Title = T("Updates.ModChecksFailedPleaseRetry", failed);
            UpdatesCheckInfoBar.Message = string.Empty;
            showFailed.Content = T("Updates.FilterFailed", failed);
            showFailed.Visibility = Visibility.Visible;
            UpdatesCheckInfoBar.IsOpen = true;
        }
        else
        {
            UpdatesCheckInfoBar.IsOpen = false;
        }
        UpdatesStatusDetailsPanel.Visibility = UpdatesCheckInfoBar.IsOpen || UpdatesCheckProgressBar.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // Tracked Mods: filters, search and order come from the view model; the repeater only builds visible rows.
    private void RenderTrackedMods()
    {
        IReadOnlyList<TrackedModRow> entries = ViewModel.TrackedMods;
        _isApplyingFilter = true;
        foreach (SegmentedItem item in TrackedModsFilterSegmented.Items.OfType<SegmentedItem>())
        {
            var filter = (TrackedModFilter)item.Tag;
            int count = ViewModel.CountFor(filter);
            item.Content = filter switch
            {
                TrackedModFilter.Updates => T("Updates.FilterUpdates", count),
                TrackedModFilter.Failed => T("Updates.FilterFailed", count),
                TrackedModFilter.UpToDate => T("Updates.FilterUpToDate", count),
                _ => T("Updates.FilterAll", count)
            };
        }
        TrackedModsFilterSegmented.SelectedIndex = (int)ViewModel.TrackedModFilter;
        _isApplyingFilter = false;
        TrackedModsSearchBox.PlaceholderText = T("Updates.SearchTrackedMods");
        AutomationProperties.SetName(TrackedModsSearchBox, T("Updates.SearchTrackedMods"));
        TrackedModsToolbar.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LayoutTrackedModsToolbar();

        IReadOnlyList<TrackedModRow> visible = ViewModel.VisibleTrackedMods;
        TrackedModsRepeater.ItemTemplate ??= new DelegateElementFactory(item => CreateTrackedModRow((TrackedModRow)item));
        TrackedModsRepeater.ItemsSource = visible;

        if (entries.Count == 0)
        {
            // Mods are tracked once they come from the online page, so the empty state leads there.
            var browse = new Button { Content = T("Dashboard.BrowseOnline"), Style = AppStyle("PrimaryButtonStyle") };
            browse.Click += (_, _) => ViewModel.Request(UpdatesAction.BrowseOnline);
            TrackedModsEmptyHost.Content = new EmptyState
            {
                Glyph = "", Title = T("Updates.NoTrackedOnlineModsYet"), Hint = T("Updates.AfterDownloadingAndExtractingFromThe"),
                Actions = browse
            };
        }
        else
        {
            TrackedModsEmptyHost.Content = visible.Count == 0
                ? new EmptyState { Glyph = "", Title = T("Updates.NoMatchingTrackedMods") }
                : null;
        }
    }

    private void OnTrackedModsFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingFilter || TrackedModsFilterSegmented.SelectedItem is not SegmentedItem { Tag: TrackedModFilter filter }) return;
        ViewModel.TrackedModFilter = filter;
        RenderTrackedMods();
    }

    private void OnTrackedModsSearchChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Search = TrackedModsSearchBox.Text;
        RenderTrackedMods();
    }

    // The search box sits beside the filter while they fit, and drops to its own row when they don't.
    private void LayoutTrackedModsToolbar()
    {
        if (TrackedModsToolbar.ActualWidth <= 0) return;
        TrackedModsFilterSegmented.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        bool beside = TrackedModsToolbar.ActualWidth >= TrackedModsFilterSegmented.DesiredSize.Width + 12 + 260;
        TrackedModsToolbar.ColumnDefinitions[1].Width = beside ? new GridLength(260) : new GridLength(0);
        TrackedModsToolbar.ColumnSpacing = beside ? 12 : 0;
        TrackedModsToolbar.RowDefinitions[1].Height = beside ? new GridLength(0) : GridLength.Auto;
        TrackedModsToolbar.RowSpacing = beside ? 0 : 8;
        Grid.SetRow(TrackedModsSearchBox, beside ? 0 : 1);
        Grid.SetColumn(TrackedModsSearchBox, beside ? 1 : 0);
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
    // navigation's task flyout, so the view only points there.
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

        _isApplyingFilter = true;
        foreach (SegmentedItem item in DownloadHistoryFilterSegmented.Items.OfType<SegmentedItem>())
        {
            var filter = (DownloadHistoryFilter)item.Tag;
            int count = finished.Count(task => UpdatesListPolicy.Matches(task.State, filter));
            item.Content = filter switch
            {
                DownloadHistoryFilter.Completed => T("DownloadTasks.FilterCompleted", count),
                DownloadHistoryFilter.Failed => T("DownloadTasks.FilterFailed", count),
                DownloadHistoryFilter.Canceled => T("DownloadTasks.FilterCanceled", count),
                _ => T("DownloadTasks.FilterAll", count)
            };
        }
        DownloadHistoryFilterSegmented.SelectedIndex = (int)ViewModel.HistoryFilter;
        _isApplyingFilter = false;
        DownloadHistoryFilterSegmented.Visibility = finished.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        List<DownloadTaskItem> visible = [.. finished.Where(task => UpdatesListPolicy.Matches(task.State, ViewModel.HistoryFilter))];
        DownloadHistoryRepeater.ItemTemplate ??= new DelegateElementFactory(item => CreateDownloadHistoryRow((DownloadTaskItem)item));
        DownloadHistoryRepeater.ItemsSource = visible;
        DownloadHistoryEmptyHost.Content = finished.Length == 0
            ? new EmptyState { Glyph = "", Title = T("DownloadTasks.NoDownloadTasks"), Hint = T("DownloadTasks.DownloadsStartedFromTheOnlineBrowser") }
            : visible.Count == 0 ? new EmptyState { Glyph = "", Title = T("DownloadTasks.NoMatchingHistory") } : null;
    }

    private void OnDownloadHistoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingFilter || DownloadHistoryFilterSegmented.SelectedItem is not SegmentedItem { Tag: DownloadHistoryFilter filter }) return;
        ViewModel.HistoryFilter = filter;
        RenderDownloadHistory();
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

    // Undo is a clickable card; with nothing to undo it is disabled and its description says why.
    private void RenderRollback()
    {
        SetCard(RollbackLastInstallButton, T("Updates.UndoLastModChange"), ViewModel.RollbackStatus);
        RollbackLastInstallButton.IsEnabled = ViewModel.CanUndoLastInstall;
    }

    // Backups: the summary with "Refresh", then one card per backup with "Restore".
    private void RenderBackups()
    {
        BackupHistoryExpander.Items.Clear();
        var refresh = new Button { Content = T("ProductivityFeatures.RefreshList"), Style = AppStyle("SecondaryButtonStyle") };
        AutomationProperties.SetAutomationId(refresh, "RefreshBackupsButton");
        AutomationProperties.SetName(refresh, T("ProductivityFeatures.RefreshList"));
        refresh.Click += (_, _) => ViewModel.Request(UpdatesAction.RefreshBackups);
        BackupHistoryExpander.Items.Add(new SettingsCard
        {
            Header = ViewModel.BackupSummary,
            Description = ViewModel.Backups.Count == 0 ? T("ProductivityFeatures.NoInstallBackupsYet") : null!,
            Content = refresh
        });

        foreach (InstallBackupRow backup in ViewModel.Backups)
        {
            var restore = new Button { Content = T("ProductivityFeatures.Restore"), Style = AppStyle("SecondaryButtonStyle") };
            AutomationProperties.SetName(restore, T("ProductivityFeatures.RestoreBackup", backup.Description));
            restore.Click += (_, _) => ViewModel.Request(UpdatesAction.RestoreBackup, backup.Path);
            var card = new SettingsCard { Header = backup.Description, Description = backup.Details, Content = restore };
            BackupHistoryExpander.Items.Add(card);
            AnimateEntrance(card);
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
}
