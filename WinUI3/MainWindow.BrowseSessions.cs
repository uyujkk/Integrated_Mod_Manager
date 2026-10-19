using IntegratedModManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private string? _activeBrowseSessionKey;
    private OnlineBrowseSession? _pendingBrowsePosition;
    private bool _restoringBrowseControls;
    private int _browseRestoreRevision;
    private int _onlineAuthorWorkFilter;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _browseSaveTimer;

    private string? CurrentBrowseSessionKey()
    {
        var repository = GetSelectedRepository();
        return repository is null ? null : WorkspaceIdentity.Online(repository.Id, repository.SourcePath,
            GetEffectiveOnlineSource(repository), GetEffectiveOnlineCategoryId(repository));
    }

    private void EnsureOnlineBrowseSession()
    {
        string? key = CurrentBrowseSessionKey();
        if (key == _activeBrowseSessionKey) return;
        _activeBrowseSessionKey = key;
        CancelOnlineModListRequest();
        _lastLoadedOnlineConfigKey = null;
        _onlineMods.Clear();
        EnsureOnlineCharacterOptionsForRepository(GetSelectedRepository());
        ++_browseRestoreRevision;
        var state = key is not null && _onlinePreferences.BrowseSessions.TryGetValue(key, out var saved) && saved is not null
            ? saved.Sanitize() : new OnlineBrowseSession { Layout = _onlineCardLayoutMode.ToString() };
        _pendingBrowsePosition = state;
        _restoringBrowseControls = _resettingOnlineFilters = true;
        try
        {
            _onlineCurrentPage = state.Page;
            _onlineCharacterFilter = state.Character;
            _onlineAuthorId = state.AuthorId;
            _onlineAuthorName = state.AuthorName;
            _onlineSearchText = state.Search;
            _onlineSortMode = Enum.Parse<OnlineSortMode>(state.Sort);
            _onlineCardLayoutMode = Enum.Parse<OnlineCardLayoutMode>(state.Layout);
            _onlineAuthorWorkFilter = state.WorkFilter;
            OnlineSearchTextBox.Text = state.Search;
            OnlineSortComboBox.SelectedItem = OnlineSortComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is OnlineSortMode sort && sort == _onlineSortMode);
        }
        finally { _restoringBrowseControls = _resettingOnlineFilters = false; }
    }

    private void CaptureOnlineBrowseSession()
    {
        if (_activeBrowseSessionKey is null || _activeBrowseSessionKey != CurrentBrowseSessionKey() || _restoringBrowseControls) return;
        var state = new OnlineBrowseSession
        {
            Page = _onlineCurrentPage, Character = _onlineCharacterFilter, AuthorId = _onlineAuthorId,
            AuthorName = _onlineAuthorName, Search = _onlineSearchText, Sort = _onlineSortMode.ToString(),
            Layout = _onlineCardLayoutMode.ToString(), WorkFilter = _onlineAuthorWorkFilter
        };
        if (_pendingBrowsePosition is { } pending)
        { state.Offset = pending.Offset; state.Anchor = pending.Anchor; state.AnchorOffset = pending.AnchorOffset; }
        else if (OnlineResultsScrollViewer() is { } viewer)
        {
            state.Offset = viewer.VerticalOffset;
            var list = OnlineResultsList();
            foreach (var mod in _onlineVisibleMods)
            {
                if (list.ContainerFromItem(mod) is not FrameworkElement container) continue;
                double y;
                try { y = container.TransformToVisual(viewer).TransformPoint(new Windows.Foundation.Point()).Y; }
                catch (ArgumentException) { continue; }
                if (y + container.ActualHeight <= 0 || y >= viewer.ViewportHeight) continue;
                state.Anchor = GetOnlineDownloadIdentity(mod);
                state.AnchorOffset = y;
                break;
            }
        }
        _onlinePreferences.BrowseSessions.Remove(_activeBrowseSessionKey);
        _onlinePreferences.BrowseSessions[_activeBrowseSessionKey] = state.Sanitize();
        while (_onlinePreferences.BrowseSessions.Count > 64)
            _onlinePreferences.BrowseSessions.Remove(_onlinePreferences.BrowseSessions.Keys.First());
    }

    private ListViewBase OnlineResultsList() => _onlineCardLayoutMode == OnlineCardLayoutMode.Grid
        ? OnlineVirtualizedGridView : OnlineVirtualizedListView;

    private ScrollViewer? OnlineResultsScrollViewer() => FindBrowseViewer(OnlineResultsList());
    private static ScrollViewer? FindBrowseViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindBrowseViewer(VisualTreeHelper.GetChild(parent, i)) is { } result) return result;
        return null;
    }

    private void InitializeBrowseSessionTracking()
    {
        _browseSaveTimer = DispatcherQueue.CreateTimer();
        _browseSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _browseSaveTimer.IsRepeating = false;
        _browseSaveTimer.Tick += (_, _) => { if (!_onlineWindowClosed) SaveShellConfig(); };
        foreach (ListViewBase list in new ListViewBase[] { OnlineVirtualizedListView, OnlineVirtualizedGridView })
            list.Loaded += (_, _) =>
            {
                if (FindBrowseViewer(list) is { } viewer && viewer.Tag as string != "BrowseTracked")
                {
                    viewer.Tag = "BrowseTracked";
                    viewer.ViewChanged += (_, args) =>
                    {
                        if (!args.IsIntermediate && _pendingBrowsePosition is null && !_onlineWindowClosed
                            && ReferenceEquals(list, OnlineResultsList()))
                        { _browseSaveTimer.Stop(); _browseSaveTimer.Start(); }
                    };
                }
                QueueRestoreOnlineBrowsePosition();
            };
    }

    private void ResetOnlineBrowsePosition()
    {
        ++_browseRestoreRevision;
        _pendingBrowsePosition = new OnlineBrowseSession();
    }

    private void QueueRestoreOnlineBrowsePosition()
    {
        if (_pendingBrowsePosition is not { } state || _isLoadingOnlineMods || _onlineVisibleMods.Count == 0
            || _currentPrimarySection != PrimarySection.Online || !OnlineResultsList().IsLoaded) return;
        int revision = ++_browseRestoreRevision;
        string? key = _activeBrowseSessionKey;
        var list = OnlineResultsList();
        var anchor = _onlineVisibleMods.FirstOrDefault(mod => GetOnlineDownloadIdentity(mod) == state.Anchor);
        int attempts = 0;
        bool scrolledToAnchor = false;
        EventHandler<object>? handler = null;
        void Restore()
        {
            if (_onlineWindowClosed || revision != _browseRestoreRevision || key != CurrentBrowseSessionKey()
                || !ReferenceEquals(list, OnlineResultsList())) { list.LayoutUpdated -= handler; return; }
            if (FindBrowseViewer(list) is not { } viewer || viewer.ViewportHeight <= 0)
            { if (++attempts > 12) list.LayoutUpdated -= handler; return; }
            if (anchor is not null && !scrolledToAnchor)
            {
                scrolledToAnchor = true;
                list.ScrollIntoView(anchor, ScrollIntoViewAlignment.Leading);
                return;
            }
            double offset = state.Offset;
            if (anchor is not null && list.ContainerFromItem(anchor) is FrameworkElement container)
            {
                try { offset = viewer.VerticalOffset + container.TransformToVisual(viewer)
                    .TransformPoint(new Windows.Foundation.Point()).Y - state.AnchorOffset; }
                catch (ArgumentException) { /* Fall back to the saved scroll distance. */ }
            }
            viewer.ChangeView(null, Math.Clamp(offset, 0, viewer.ScrollableHeight), null, disableAnimation: true);
            _pendingBrowsePosition = null;
            list.LayoutUpdated -= handler;
        }
        handler = (_, _) => Restore();
        list.LayoutUpdated += handler;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Restore());
    }
}
