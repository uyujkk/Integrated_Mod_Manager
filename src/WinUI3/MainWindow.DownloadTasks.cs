using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private DownloadTaskItem CreateDownloadTask(OnlineModCard mod, string destinationPath)
    {
        var task = new DownloadTaskItem
        {
            Title = mod.Title,
            SourceIdentity = OnlinePage.GetOnlineDownloadIdentity(mod),
            DestinationPath = destinationPath,
            State = DownloadTaskState.Queued,
            Status = M("DownloadTasks.WaitingToStart"),
            StatusZh = UiText.Chinese("DownloadTasks.WaitingToStart"),
            StatusEn = UiText.English("DownloadTasks.WaitingToStart")
        };
        _services.Downloads.Tasks.Insert(0, task);
        while (_services.Downloads.Tasks.Count > 30)
        {
            DownloadTaskItem? oldestFinished = _services.Downloads.Tasks.LastOrDefault(item =>
                item.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled);
            if (oldestFinished is null)
            {
                break;
            }
            _services.Downloads.Tasks.Remove(oldestFinished);
            oldestFinished.Cancellation.Dispose();
        }
        RefreshDownloadTaskCenter();
        return task;
    }

    private void UpdateDownloadTask(
        DownloadTaskItem task,
        DownloadTaskState state,
        double progress,
        UiMessage status,
        bool forceRefresh = false)
    {
        if (!DownloadProgressPolicy.CanApply(task.State, state))
        {
            return;
        }

        bool phaseChanged = task.State != state;
        task.State = state;
        task.Progress = DownloadProgressPolicy.MergeProgress(task.Progress, progress);
        task.Status = status;
        task.StatusZh = status.Resolve(UiLanguagePolicy.Chinese);
        task.StatusEn = status.Resolve(UiLanguagePolicy.English);
        if (!_onlineView._onlineWindowClosed && (phaseChanged || DownloadProgressPolicy.IsTerminal(state))) _onlineView.PersistDownloadTasks();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!forceRefresh && now - task.LastUiRefreshUtc < TimeSpan.FromMilliseconds(125))
        {
            return;
        }
        task.LastUiRefreshUtc = now;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_onlineView._onlineWindowClosed) return;
            RefreshDownloadTaskCard(task);
            _onlineView.RefreshOnlineCardStates(refreshInstalled: DownloadProgressPolicy.IsTerminal(task.State));
        });
    }

    // The Updates page lists the finished tasks, the navigation the running ones; both follow the service.
    private void RefreshDownloadTaskCenter() => _services.Downloads.NotifyChanged();

    // Cancel while running, retry after a failure or cancel, otherwise open the download folder.
    private Button CreateDownloadTaskActionButton(DownloadTaskItem task)
    {
        var actionButton = new Button
        {
            MinWidth = 104,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        actionButton.Click += async (_, _) => await RunDownloadTaskActionAsync(task);
        return actionButton;
    }

    // The task button, in the flyout or on the Updates page: cancel, retry or open the folder.
    private async Task RunDownloadTaskActionAsync(DownloadTaskItem task)
    {
        try
        {
            if (DownloadProgressPolicy.IsActive(task.State))
            {
                if (task.Session?.CanCancel == false || task.ReplacementCommitting) return;
                task.Cancellation.Cancel();
                UpdateDownloadTask(task, DownloadTaskState.Canceling, task.Progress, M("DownloadTasks.Canceling"), forceRefresh: true);
                return;
            }

            if (task.State is DownloadTaskState.Failed or DownloadTaskState.Canceled && task.RetryRequest is not null)
                await _onlineView.RetryOnlineDownloadAsync(task);
            else OpenDirectory(task.DestinationPath, T("DownloadTasks.DownloadFolder"));
        }
        catch (Exception ex)
        {
            ReportUiActionFailure("actionButton.Click", ex);
        }
    }

    private UIElement CreateDownloadTaskCard(DownloadTaskItem task, Dictionary<string, DownloadTaskCardView> cards)
    {
        var border = new Border
        {
            Style = (Style)Application.Current.Resources["InsetBorderStyle"],
            Padding = new Thickness(14)
        };
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = task.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var statusTextBlock = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(statusTextBlock);
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6
        };
        content.Children.Add(progressBar);
        grid.Children.Add(content);

        Button actionButton = CreateDownloadTaskActionButton(task);
        Grid.SetColumn(actionButton, 1);
        grid.Children.Add(actionButton);
        border.Child = grid;
        cards[task.Id] = new DownloadTaskCardView(statusTextBlock, progressBar, actionButton);
        RefreshDownloadTaskCard(task);
        return border;
    }

    // A task can be shown twice: on the Updates page (which follows the service) and in the navigation flyout.
    private void RefreshDownloadTaskCard(DownloadTaskItem task)
    {
        _services.Downloads.NotifyTaskUpdated(task);
        RefreshNavTasks();
        if (_navTaskCards.TryGetValue(task.Id, out DownloadTaskCardView? view)) RefreshDownloadTaskCardView(task, view);
        NavTasksClearButton.IsEnabled = _services.Downloads.Tasks.Any(item => DownloadProgressPolicy.IsTerminal(item.State));
    }

    private void RefreshDownloadTaskCardView(DownloadTaskItem task, DownloadTaskCardView view)
        => DownloadTaskViews.Refresh(task, view, _services.Language, _onlineView._onlineDownloadActions.IsPreparing);

    // Navigation summary: always present so the history stays one click away; the badge and
    // bar follow the running downloads.
    private void RefreshNavTasks()
    {
        if (NavTasksButton is null) return;
        DownloadTaskItem[] active = _services.Downloads.Tasks.Where(item => DownloadProgressPolicy.IsActive(item.State)).ToArray();
        NavTasksBadge.Value = active.Length;
        NavTasksBadge.Visibility = active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NavTasksProgressBar.Visibility = active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NavTasksProgressBar.IsIndeterminate = active.Any(item => item.State is not DownloadTaskState.Downloading || !item.HasKnownTotalLength);
        NavTasksProgressBar.Value = active.Length > 0 ? active.Average(item => item.Progress) : 0;
        string tip = active.Length > 0 ? T("Shell.TasksActive", active.Length) : T("Shell.NoActiveTasks");
        ToolTipService.SetToolTip(NavTasksButton, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NavTasksButton, T("Shell.Downloads") + " · " + tip);
    }

    private void OnNavTasksFlyoutOpening(object? sender, object e)
    {
        NavTasksTitleTextBlock.Text = T("Shell.BackgroundTasks");
        NavTasksClearButton.Content = T("Updates.ClearFinished");
        NavTasksAllButton.Content = T("Shell.ViewAllTasks");
        FillNavTasksPanel();
    }

    private void FillNavTasksPanel()
    {
        NavTasksPanel.Children.Clear();
        _navTaskCards.Clear();
        foreach (DownloadTaskItem task in _services.Downloads.Tasks.Take(12))
            NavTasksPanel.Children.Add(CreateDownloadTaskCard(task, _navTaskCards));
        if (_services.Downloads.Tasks.Count == 0)
            NavTasksPanel.Children.Add(new TextBlock { Text = T("DownloadTasks.NoDownloadTasks"), Opacity = 0.75 });
        NavTasksClearButton.IsEnabled = _services.Downloads.Tasks.Any(item => DownloadProgressPolicy.IsTerminal(item.State));
    }

    private void OnNavTasksFlyoutClosed(object? sender, object e)
    {
        NavTasksPanel.Children.Clear();
        _navTaskCards.Clear();
    }

    private void OnNavTasksAllClicked(object sender, RoutedEventArgs e)
    {
        NavTasksFlyout.Hide();
        OnUpdatesNavClicked(UpdatesNavButton, e);
    }

    private void OnClearDownloadTasksClicked(object sender, RoutedEventArgs e)
    {
        foreach (DownloadTaskItem task in _services.Downloads.Tasks
            .Where(item => item.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled)
            .ToList())
        {
            _services.Downloads.Tasks.Remove(task);
            task.Cancellation.Dispose();
        }
        RefreshDownloadTaskCenter();
        _onlineView.RefreshOnlineCardStates();
        _onlineView.PersistDownloadTasks();
    }
}
