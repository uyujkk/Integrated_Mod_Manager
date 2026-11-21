using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private DownloadTaskItem CreateDownloadTask(OnlineModCard mod, string destinationPath) => _services.Downloads.CreateTask(mod, destinationPath);

    private void UpdateDownloadTask(DownloadTaskItem task, DownloadTaskState state, double progress, UiMessage status, bool forceRefresh = false)
        => _services.Downloads.UpdateTask(task, state, progress, status, forceRefresh);

    // A task's card follows its state: in the navigation flyout and on the Updates page, and on the Online page's cards.
    private void OnDownloadTaskRefreshRequested(object? sender, (DownloadTaskItem Task, bool Finished) args)
    {
        RefreshDownloadTaskCard(args.Task);
        _onlineView.RefreshOnlineCardStates(refreshInstalled: args.Finished);
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
                await _services.OnlineDownloads.RetryOnlineDownloadAsync(task);
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
        => DownloadTaskViews.Refresh(task, view, _services.Language, _services.OnlineDownloads._onlineDownloadActions.IsPreparing);

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
        _services.Downloads.ClearFinished();
        _onlineView.RefreshOnlineCardStates();
        _services.Downloads.Persist();
    }
}
