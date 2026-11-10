using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Repository;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
    internal string? _activeBrowseSessionKey;
    internal OnlineBrowseSession? _pendingBrowsePosition;
    private bool _restoringBrowseControls;
    internal int _browseRestoreRevision;
    internal Microsoft.UI.Dispatching.DispatcherQueueTimer? _browseSaveTimer;

    private string? CurrentBrowseSessionKey()
    {
        var repository = _services.Repositories.Selected;
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
        _services.Online.Page.Mods.Clear();
        EnsureOnlineCharacterOptionsForRepository(_services.Repositories.Selected);
        ++_browseRestoreRevision;
        var state = key is not null && _services.Online.Preferences.BrowseSessions.TryGetValue(key, out var saved) && saved is not null
            ? saved.Sanitize() : new OnlineBrowseSession { Layout = _services.Online.Page.CardLayoutMode.ToString() };
        _pendingBrowsePosition = state;
        _restoringBrowseControls = _resettingOnlineFilters = true;
        try
        {
            _services.Online.Page.CurrentPage = state.Page;
            _services.Online.Page.CharacterFilter = state.Character;
            _services.Online.Page.AuthorId = state.AuthorId;
            _services.Online.Page.AuthorName = state.AuthorName;
            _services.Online.Page.SearchText = state.Search;
            _services.Online.Page.SortMode = Enum.Parse<OnlineSortMode>(state.Sort);
            _services.Online.Page.CardLayoutMode = Enum.Parse<OnlineCardLayoutMode>(state.Layout);
            _services.Online.Page.AuthorWorkFilter = state.WorkFilter;
            OnlineSearchTextBox.Text = state.Search;
            OnlineSortComboBox.SelectedItem = OnlineSortComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is OnlineSortMode sort && sort == _services.Online.Page.SortMode);
        }
        finally { _restoringBrowseControls = _resettingOnlineFilters = false; }
    }

    internal void CaptureOnlineBrowseSession()
    {
        if (_activeBrowseSessionKey is null || _activeBrowseSessionKey != CurrentBrowseSessionKey() || _restoringBrowseControls) return;
        var state = new OnlineBrowseSession
        {
            Page = _services.Online.Page.CurrentPage, Character = _services.Online.Page.CharacterFilter, AuthorId = _services.Online.Page.AuthorId,
            AuthorName = _services.Online.Page.AuthorName, Search = _services.Online.Page.SearchText, Sort = _services.Online.Page.SortMode.ToString(),
            Layout = _services.Online.Page.CardLayoutMode.ToString(), WorkFilter = _services.Online.Page.AuthorWorkFilter
        };
        if (_pendingBrowsePosition is { } pending)
        { state.Offset = pending.Offset; state.Anchor = pending.Anchor; state.AnchorOffset = pending.AnchorOffset; }
        else if (OnlineResultsScrollViewer() is { } viewer)
        {
            state.Offset = viewer.VerticalOffset;
            var list = OnlineResultsList();
            foreach (var mod in _services.Online.Page.VisibleMods)
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
        _services.Online.Preferences.BrowseSessions.Remove(_activeBrowseSessionKey);
        _services.Online.Preferences.BrowseSessions[_activeBrowseSessionKey] = state.Sanitize();
        while (_services.Online.Preferences.BrowseSessions.Count > 64)
            _services.Online.Preferences.BrowseSessions.Remove(_services.Online.Preferences.BrowseSessions.Keys.First());
    }

    private ListViewBase OnlineResultsList() => _services.Online.Page.CardLayoutMode == OnlineCardLayoutMode.Grid
        ? OnlineVirtualizedGridView : OnlineVirtualizedListView;

    private ScrollViewer? OnlineResultsScrollViewer() => FindBrowseViewer(OnlineResultsList());
    private static ScrollViewer? FindBrowseViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindBrowseViewer(VisualTreeHelper.GetChild(parent, i)) is { } result) return result;
        return null;
    }

    internal void InitializeBrowseSessionTracking()
    {
        _browseSaveTimer = DispatcherQueue.CreateTimer();
        _browseSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _browseSaveTimer.IsRepeating = false;
        _browseSaveTimer.Tick += (_, _) => { if (!_services.IsClosing) _host.SaveShellConfig(); };
        foreach (ListViewBase list in new ListViewBase[] { OnlineVirtualizedListView, OnlineVirtualizedGridView })
            list.Loaded += (_, _) =>
            {
                if (FindBrowseViewer(list) is { } viewer && viewer.Tag as string != "BrowseTracked")
                {
                    viewer.Tag = "BrowseTracked";
                    viewer.ViewChanged += (_, args) =>
                    {
                        if (!args.IsIntermediate && _pendingBrowsePosition is null && !_services.IsClosing
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
        if (_pendingBrowsePosition is not { } state || _isLoadingOnlineMods || _services.Online.Page.VisibleMods.Count == 0
            || !_host.IsOnlinePageShown || !OnlineResultsList().IsLoaded) return;
        int revision = ++_browseRestoreRevision;
        string? key = _activeBrowseSessionKey;
        var list = OnlineResultsList();
        var anchor = _services.Online.Page.VisibleMods.FirstOrDefault(mod => GetOnlineDownloadIdentity(mod) == state.Anchor);
        int attempts = 0;
        bool scrolledToAnchor = false;
        EventHandler<object>? handler = null;
        void Restore()
        {
            if (_services.IsClosing || revision != _browseRestoreRevision || key != CurrentBrowseSessionKey()
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
