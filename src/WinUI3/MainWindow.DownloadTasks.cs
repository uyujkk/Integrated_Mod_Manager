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
            SourceIdentity = GetOnlineDownloadIdentity(mod),
            DestinationPath = destinationPath,
            State = DownloadTaskState.Queued,
            StatusZh = "等待开始",
            StatusEn = "Waiting to start"
        };
        _downloadTasks.Insert(0, task);
        while (_downloadTasks.Count > 30)
        {
            DownloadTaskItem? oldestFinished = _downloadTasks.LastOrDefault(item =>
                item.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled);
            if (oldestFinished is null)
            {
                break;
            }
            _downloadTasks.Remove(oldestFinished);
            oldestFinished.Cancellation.Dispose();
        }
        RefreshDownloadTaskCenter();
        return task;
    }

    private void UpdateDownloadTask(
        DownloadTaskItem task,
        DownloadTaskState state,
        double progress,
        string statusZh,
        string statusEn,
        bool forceRefresh = false)
    {
        if (!DownloadProgressPolicy.CanApply(task.State, state))
        {
            return;
        }

        bool phaseChanged = task.State != state;
        task.State = state;
        task.Progress = DownloadProgressPolicy.MergeProgress(task.Progress, progress);
        task.StatusZh = statusZh;
        task.StatusEn = statusEn;
        if (!_onlineWindowClosed && (phaseChanged || DownloadProgressPolicy.IsTerminal(state))) PersistDownloadTasks();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!forceRefresh && now - task.LastUiRefreshUtc < TimeSpan.FromMilliseconds(125))
        {
            return;
        }
        task.LastUiRefreshUtc = now;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_onlineWindowClosed) return;
            RefreshDownloadTaskCard(task);
            RefreshOnlineCardStates(refreshInstalled: DownloadProgressPolicy.IsTerminal(task.State));
        });
    }

    private void RefreshDownloadTaskCenter()
    {
        if (DownloadTaskListPanel is null)
        {
            return;
        }

        RefreshNavTasks();
        DownloadTaskListPanel.Children.Clear();
        _downloadTaskCards.Clear();

        // The updates page keeps the history: finished, failed and canceled downloads. Running ones
        // are followed in the title bar's task flyout, so the page only points there.
        DownloadTaskItem[] finished = [.. _downloadTasks.Where(item => DownloadProgressPolicy.IsTerminal(item.State))];
        int running = _downloadTasks.Count - finished.Length;
        if (running > 0)
        {
            DownloadTaskListPanel.Children.Add(new TextBlock
            {
                Text = T("DownloadTasks.RunningInNavigation", running),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap
            });
        }
        ClearDownloadTasksButton.IsEnabled = finished.Length > 0;
        if (finished.Length == 0)
        {
            DownloadTaskListPanel.Children.Add(CreateInfoCard(
                T("DownloadTasks.NoDownloadTasks"),
                T("DownloadTasks.DownloadsStartedFromTheOnlineBrowser")));
        }
        foreach (DownloadTaskItem task in finished)
        {
            DownloadTaskListPanel.Children.Add(CreateDownloadTaskCard(task, _downloadTaskCards));
        }
        if (NavTasksFlyout.IsOpen) FillNavTasksPanel();
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

        var actionButton = new Button
        {
            MinWidth = 104,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        actionButton.Click += async (_, _) =>
        {
            try
            {
                if (DownloadProgressPolicy.IsActive(task.State))
                {
                    if (task.Session?.CanCancel == false || task.ReplacementCommitting) return;
                    task.Cancellation.Cancel();
                    UpdateDownloadTask(task, DownloadTaskState.Canceling, task.Progress, "正在取消...", "Canceling...", forceRefresh: true);
                    return;
                }

                if (task.State is DownloadTaskState.Failed or DownloadTaskState.Canceled && task.RetryRequest is not null)
                    await RetryOnlineDownloadAsync(task);
                else OpenDirectory(task.DestinationPath, T("DownloadTasks.DownloadFolder"));
            }
            catch (Exception ex)
            {
                ReportUiActionFailure("actionButton.Click", ex);
            }
        };
        Grid.SetColumn(actionButton, 1);
        grid.Children.Add(actionButton);
        border.Child = grid;
        cards[task.Id] = new DownloadTaskCardView(statusTextBlock, progressBar, actionButton);
        RefreshDownloadTaskCard(task);
        return border;
    }

    // A task can be shown twice: in the Updates task center and in the title bar flyout.
    private void RefreshDownloadTaskCard(DownloadTaskItem task)
    {
        // A download that just finished joins the history on the updates page.
        if (DownloadProgressPolicy.IsTerminal(task.State) && !_downloadTaskCards.ContainsKey(task.Id))
        {
            RefreshDownloadTaskCenter();
        }
        RefreshNavTasks();
        foreach (Dictionary<string, DownloadTaskCardView> cards in new[] { _downloadTaskCards, _navTaskCards })
        {
            if (cards.TryGetValue(task.Id, out DownloadTaskCardView? view)) RefreshDownloadTaskCardView(task, view);
        }
        ClearDownloadTasksButton.IsEnabled = NavTasksClearButton.IsEnabled
            = _downloadTasks.Any(item => DownloadProgressPolicy.IsTerminal(item.State));
    }

    private void RefreshDownloadTaskCardView(DownloadTaskItem task, DownloadTaskCardView view)
    {
        bool active = DownloadProgressPolicy.IsActive(task.State);
        view.StatusTextBlock.Text = L(task.StatusZh, task.StatusEn);
        view.ProgressBar.Value = task.Progress;
        view.ProgressBar.IsIndeterminate = active
            && (task.State is not DownloadTaskState.Downloading || !task.HasKnownTotalLength);
        bool retry = task.State is DownloadTaskState.Failed or DownloadTaskState.Canceled && task.RetryRequest is not null;
        view.ActionButton.Content = active ? T("Common.Cancel") : retry ? T("DownloadTasks.Retry") : T("DownloadTasks.OpenFolder");
        view.ActionButton.IsEnabled = active
            ? task.State is not DownloadTaskState.Canceling && task.Session?.CanCancel != false && !task.ReplacementCommitting
            : retry ? !task.RetryInProgress && !_onlineDownloadActions.IsPreparing : Directory.Exists(task.DestinationPath);
    }

    // Navigation summary: always present so the history stays one click away; the badge and
    // bar follow the running downloads.
    private void RefreshNavTasks()
    {
        if (NavTasksButton is null) return;
        DownloadTaskItem[] active = _downloadTasks.Where(item => DownloadProgressPolicy.IsActive(item.State)).ToArray();
        NavTasksCountTextBlock.Text = active.Length > 0 ? active.Length.ToString() : string.Empty;
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
        foreach (DownloadTaskItem task in _downloadTasks.Take(12))
            NavTasksPanel.Children.Add(CreateDownloadTaskCard(task, _navTaskCards));
        if (_downloadTasks.Count == 0)
            NavTasksPanel.Children.Add(new TextBlock { Text = T("DownloadTasks.NoDownloadTasks"), Opacity = 0.75 });
        NavTasksClearButton.IsEnabled = _downloadTasks.Any(item => DownloadProgressPolicy.IsTerminal(item.State));
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
        foreach (DownloadTaskItem task in _downloadTasks
            .Where(item => item.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled)
            .ToList())
        {
            _downloadTasks.Remove(task);
            task.Cancellation.Dispose();
        }
        RefreshDownloadTaskCenter();
        RefreshOnlineCardStates();
        PersistDownloadTasks();
    }
}
