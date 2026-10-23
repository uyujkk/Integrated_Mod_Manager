using IntegratedModManager.Core.Configuration;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // Only loaded cards subscribe. Virtualized/recycled cards do not accumulate callbacks.
    private readonly HashSet<Action<bool>> _onlineCardRefreshers = [];
    private string? _onlineCardPresentationKey;

    private void RefreshOnlineCardPresentation()
    {
        string key = $"{_currentLanguage}|{_isDarkTheme}|{_interfaceDensity}";
        if (_onlineCardPresentationKey == key) return;
        _onlineCardPresentationKey = key;
        RefreshPresenters(OnlineVirtualizedListView);
        RefreshPresenters(OnlineVirtualizedGridView);
        void RefreshPresenters(DependencyObject parent)
        {
            if (parent is ContentControl { DataContext: OnlineModCard mod, Tag: string tag } presenter
                && tag is "OnlineListCardPresenter" or "OnlineGridCardPresenter")
            {
                presenter.Content = tag == "OnlineListCardPresenter" ? CreateOnlineModCardV2(mod) : CreateOnlineModTile(mod);
                return;
            }
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                RefreshPresenters(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i));
        }
    }

    private HyperlinkButton CreateOnlineAuthorLink(OnlineModCard mod)
    {
        var link = new HyperlinkButton
        {
            Content = new TextBlock { Text = mod.Author, TextTrimming = TextTrimming.CharacterEllipsis },
            Padding = new Thickness(0), MinHeight = 0, MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            IsEnabled = !string.IsNullOrWhiteSpace(mod.Author)
                && mod.Author != "Unknown author" && mod.Author != "未知作者"
        };
        string hint = mod.AuthorId > 0 || mod.ItemId > 0 ? L($"查看作者作品（跨页）：{mod.Author}", $"Browse author (all pages): {mod.Author}")
            : L($"搜索当前页作者：{mod.Author}", $"Search this page by author: {mod.Author}");
        ToolTipService.SetToolTip(link, hint);
        AutomationProperties.SetName(link, hint);
        link.Click += async (_, _) =>
        {
            try
            {
                string context = GetOnlineDetailContextKey();
                link.IsEnabled = false;
                try
                {
                    if (mod.AuthorId <= 0 && mod.ItemId > 0)
                    {
                        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        try { mod.AuthorId = (await _gameBananaMetadataService.GetModAsync(mod.ItemId, cancellation.Token)).AuthorId; }
                        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException or InvalidDataException or System.Text.Json.JsonException)
                        { LogApplicationIssue("Resolve cached author identity", ex); }
                    }
                    if (!_onlineWindowClosed && context == GetOnlineDetailContextKey())
                    {
                        if (mod.AuthorId <= 0 && mod.ItemId > 0)
                            ShowAppNotification("作者身份暂未获取，先筛选当前页。", "Author identity unavailable; filtering this page instead.");
                        await BrowseOnlineAuthorAsync(mod.AuthorId, mod.Author);
                    }
                }
                finally { if (!_onlineWindowClosed) link.IsEnabled = true; }
            }
            catch (Exception ex)
            {
                ReportUiActionFailure("link.Click", ex);
            }
        };
        link.ContextFlyout = CreateOnlineCardMenu(mod);
        return link;
    }

    private void TrackOnlineCardState(OnlineModCard mod, Button button, ContentControl installedHost,
        TextBlock status, string defaultStatus)
    {
        TrackedModOrigin? installed = null;
        void Refresh(bool refreshInstalled)
        {
            // Byte-progress callbacks must not probe every tracked directory on disk.
            if (refreshInstalled) installed = FindInstalledOnlineMod(mod);
            string path = installed?.Path ?? string.Empty;
            if (!string.Equals(installedHost.Tag as string, path, StringComparison.OrdinalIgnoreCase))
            {
                installedHost.Tag = path;
                installedHost.Content = installed is null ? null : CreateOnlineInstalledIndicator(path);
            }
            bool active = _onlineDownloadActions.IsActive(CaptureOnlineDownloadContext(GetSelectedRepository()),
                GetOnlineDownloadIdentity(mod));
            button.IsEnabled = !string.IsNullOrWhiteSpace(mod.DownloadUrl) && !IsOnlineDownloadActionBlocked(mod);
            DownloadTaskItem? task = _downloadTasks.FirstOrDefault(item =>
                item.Session is not null && item.SourceIdentity == GetOnlineDownloadIdentity(mod)
                && string.Equals(item.Session.Context.RepositoryId, GetSelectedRepository()?.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Session.Context.RepositoryPath, GetSelectedRepository()?.SourcePath, StringComparison.OrdinalIgnoreCase));
            // A retry can be preparing while its previous terminal task is still in history.
            if (active && task is not null && DownloadProgressPolicy.IsTerminal(task.State)) task = null;
            string text = task?.State switch
            {
                DownloadTaskState.Queued => L("等待下载", "Queued"),
                DownloadTaskState.Preparing => L("准备下载…", "Preparing…"),
                DownloadTaskState.Downloading => L($"正在下载 {task.Progress:0}%", $"Downloading {task.Progress:0}%"),
                DownloadTaskState.Extracting => L("正在解压…", "Extracting…"),
                DownloadTaskState.Canceling => L("正在取消…", "Canceling…"),
                DownloadTaskState.Completed => L("下载完成", "Download completed"),
                DownloadTaskState.Failed => L("下载失败，可重试", "Download failed; retry available"),
                DownloadTaskState.Canceled => L("下载已取消", "Download canceled"),
                _ => active ? L("准备下载…", "Preparing…") : defaultStatus
            };
            status.Text = text;
            ToolTipService.SetToolTip(status, task is null ? text : L(task.StatusZh, task.StatusEn));
            string action = active ? text : installed is null
                ? L("下载并解压", "Download and extract")
                : L("重新下载并解压", "Download and extract again");
            ToolTipService.SetToolTip(button, action);
            AutomationProperties.SetName(button, action);
            if (button.Content is FontIcon icon)
                icon.Glyph = active ? "\uE895" : task?.State == DownloadTaskState.Failed ? "\uE72C" : "\uE896";
        }
        button.Loaded += (_, _) => { _onlineCardRefreshers.Add(Refresh); Refresh(true); };
        button.Unloaded += (_, _) => _onlineCardRefreshers.Remove(Refresh);
        Refresh(true);
    }

    private void RefreshOnlineCardStates(bool refreshInstalled = true)
    {
        if (_onlineWindowClosed) return;
        foreach (Action<bool> refresh in _onlineCardRefreshers.ToArray()) refresh(refreshInstalled);
    }

    // Late download counts update these cards in place instead of rebuilding and re-sorting the list.
    private readonly HashSet<Action> _onlineCardMetricRefreshers = [];

    private void TrackOnlineCardMetrics(FrameworkElement host, Action update)
    {
        host.Loaded += (_, _) => { _onlineCardMetricRefreshers.Add(update); update(); };
        host.Unloaded += (_, _) => _onlineCardMetricRefreshers.Remove(update);
    }

    private void RefreshOnlineCardMetrics()
    {
        if (_onlineWindowClosed) return;
        foreach (Action update in _onlineCardMetricRefreshers.ToArray()) update();
    }
}
