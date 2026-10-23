using IntegratedModManager.Core;
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

        DownloadTaskListPanel.Children.Clear();
        _downloadTaskCards.Clear();
        if (_downloadTasks.Count == 0)
        {
            DownloadTaskListPanel.Children.Add(CreateInfoCard(
                L("暂无下载任务", "No download tasks"),
                L("从在线 Mod 页面开始下载后，进度和结果会显示在这里。", "Downloads started from the online browser will appear here.")));
            ClearDownloadTasksButton.IsEnabled = false;
            return;
        }

        ClearDownloadTasksButton.IsEnabled = _downloadTasks.Any(item =>
            item.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled);
        foreach (DownloadTaskItem task in _downloadTasks)
        {
            DownloadTaskListPanel.Children.Add(CreateDownloadTaskCard(task));
        }
    }

    private UIElement CreateDownloadTaskCard(DownloadTaskItem task)
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
                else OpenDirectory(task.DestinationPath, L("下载目录", "Download folder"));
            }
            catch (Exception ex)
            {
                ReportUiActionFailure("actionButton.Click", ex);
            }
        };
        Grid.SetColumn(actionButton, 1);
        grid.Children.Add(actionButton);
        border.Child = grid;
        _downloadTaskCards[task.Id] = new DownloadTaskCardView(statusTextBlock, progressBar, actionButton);
        RefreshDownloadTaskCard(task);
        return border;
    }

    private void RefreshDownloadTaskCard(DownloadTaskItem task)
    {
        if (!_downloadTaskCards.TryGetValue(task.Id, out DownloadTaskCardView? view))
        {
            return;
        }

        bool active = DownloadProgressPolicy.IsActive(task.State);
        view.StatusTextBlock.Text = L(task.StatusZh, task.StatusEn);
        view.ProgressBar.Value = task.Progress;
        view.ProgressBar.IsIndeterminate = active
            && (task.State is not DownloadTaskState.Downloading || !task.HasKnownTotalLength);
        bool retry = task.State is DownloadTaskState.Failed or DownloadTaskState.Canceled && task.RetryRequest is not null;
        view.ActionButton.Content = active ? L("取消", "Cancel") : retry ? L("重试", "Retry") : L("打开目录", "Open Folder");
        view.ActionButton.IsEnabled = active
            ? task.State is not DownloadTaskState.Canceling && task.Session?.CanCancel != false && !task.ReplacementCommitting
            : retry ? !task.RetryInProgress && !_onlineDownloadActions.IsPreparing : Directory.Exists(task.DestinationPath);
        ClearDownloadTasksButton.IsEnabled = _downloadTasks.Any(item => DownloadProgressPolicy.IsTerminal(item.State));
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
