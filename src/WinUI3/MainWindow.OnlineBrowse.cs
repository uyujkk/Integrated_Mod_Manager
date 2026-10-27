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
        string hint = mod.AuthorId > 0 || mod.ItemId > 0 ? T("OnlineBrowse.BrowseAuthorAllPages", mod.Author)
            : T("OnlineBrowse.SearchThisPageByAuthor", mod.Author);
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

    // What a card shows for its download: shared by list cards (controls) and grid tiles (view model).
    private readonly record struct OnlineCardStatus(bool CanDownload, string Text, string ToolTip, string Action, string Glyph);

    private OnlineCardStatus GetOnlineCardStatus(OnlineModCard mod, TrackedModOrigin? installed, string defaultStatus)
    {
        bool active = _onlineDownloadActions.IsActive(CaptureOnlineDownloadContext(GetSelectedRepository()),
            GetOnlineDownloadIdentity(mod));
        DownloadTaskItem? task = _downloadTasks.FirstOrDefault(item =>
            item.Session is not null && item.SourceIdentity == GetOnlineDownloadIdentity(mod)
            && string.Equals(item.Session.Context.RepositoryId, GetSelectedRepository()?.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Session.Context.RepositoryPath, GetSelectedRepository()?.SourcePath, StringComparison.OrdinalIgnoreCase));
        // A retry can be preparing while its previous terminal task is still in history.
        if (active && task is not null && DownloadProgressPolicy.IsTerminal(task.State)) task = null;
        string text = task?.State switch
        {
            DownloadTaskState.Queued => T("OnlineBrowse.Queued"),
            DownloadTaskState.Preparing => T("OnlineBrowse.Preparing"),
            DownloadTaskState.Downloading => T("OnlineBrowse.Downloading", task.Progress),
            DownloadTaskState.Extracting => T("OnlineBrowse.Extracting"),
            DownloadTaskState.Canceling => T("OnlineBrowse.Canceling"),
            DownloadTaskState.Completed => T("OnlineBrowse.DownloadCompleted"),
            DownloadTaskState.Failed => T("OnlineBrowse.DownloadFailedRetryAvailable"),
            DownloadTaskState.Canceled => T("OnlineBrowse.DownloadCanceled"),
            _ => active ? T("OnlineBrowse.Preparing") : defaultStatus
        };
        string action = active ? text : installed is null
            ? T("Common.DownloadAndExtract")
            : T("Common.DownloadAndExtractAgain");
        return new(
            !string.IsNullOrWhiteSpace(mod.DownloadUrl) && !IsOnlineDownloadActionBlocked(mod),
            text,
            task is null ? text : L(task.StatusZh, task.StatusEn),
            action,
            active ? "\uE895" : task?.State == DownloadTaskState.Failed ? "\uE72C" : "\uE896");
    }

    // The installed badge is rebuilt only when the installed path changes.
    private void ApplyOnlineInstalledIndicator(ContentControl installedHost, TrackedModOrigin? installed)
    {
        string path = installed?.Path ?? string.Empty;
        if (string.Equals(installedHost.Tag as string, path, StringComparison.OrdinalIgnoreCase)) return;
        installedHost.Tag = path;
        installedHost.Content = installed is null ? null : CreateOnlineInstalledIndicator(path);
    }

    private void TrackOnlineCardState(OnlineModCard mod, Button button, ContentControl installedHost,
        TextBlock status, string defaultStatus)
    {
        TrackedModOrigin? installed = null;
        void Refresh(bool refreshInstalled)
        {
            // Byte-progress callbacks must not probe every tracked directory on disk.
            if (refreshInstalled) installed = FindInstalledOnlineMod(mod);
            ApplyOnlineInstalledIndicator(installedHost, installed);
            OnlineCardStatus state = GetOnlineCardStatus(mod, installed, defaultStatus);
            button.IsEnabled = state.CanDownload;
            status.Text = state.Text;
            ToolTipService.SetToolTip(status, state.ToolTip);
            ToolTipService.SetToolTip(button, state.Action);
            AutomationProperties.SetName(button, state.Action);
            if (button.Content is FontIcon icon) icon.Glyph = state.Glyph;
        }
        button.Loaded += (_, _) => { _onlineCardRefreshers.Add(Refresh); Refresh(true); };
        button.Unloaded += (_, _) => _onlineCardRefreshers.Remove(Refresh);
        Refresh(true);
    }

    private void TrackOnlineTileState(OnlineModTile tile, string defaultStatus)
    {
        OnlineModTileViewModel model = tile.ViewModel;
        TrackedModOrigin? installed = null;
        void Refresh(bool refreshInstalled)
        {
            if (refreshInstalled) installed = FindInstalledOnlineMod(model.Mod);
            ApplyOnlineInstalledIndicator(tile.Installed, installed);
            OnlineCardStatus state = GetOnlineCardStatus(model.Mod, installed, defaultStatus);
            model.CanDownload = state.CanDownload;
            model.StatusText = state.Text;
            model.StatusToolTip = state.ToolTip;
            model.DownloadToolTip = state.Action;
            model.DownloadGlyph = state.Glyph;
        }
        tile.Loaded += (_, _) => { _onlineCardRefreshers.Add(Refresh); Refresh(true); };
        tile.Unloaded += (_, _) => _onlineCardRefreshers.Remove(Refresh);
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
