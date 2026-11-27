using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>What picking a feature does once its place is on screen.</summary>
internal enum SpotlightMode
{
    /// <summary>The first time: focus the place and draw a ring around it.</summary>
    Show,
    /// <summary>Later, for places that change data: only focus it.</summary>
    Focus,
    /// <summary>Later: use it, as a click would (press a button, open a group or a list). Switches are never flipped.</summary>
    Use
}

/// <summary>
/// "Take me there" for the search's feature directory: finds a control by its x:Name or automation id once its page
/// is on screen, opens the folded group it sits in, scrolls it into view and gives it keyboard focus. The first time it
/// draws an accent ring around it for a moment, which follows the place while the page scrolls or a group unfolds;
/// later it uses the place directly. The ring takes no input. The window gives it its root.
/// </summary>
internal sealed class Spotlight(Grid root)
{
    private readonly Grid _root = root;
    private Canvas? _layer;
    private Border? _ring;
    private FrameworkElement? _tracked;
    private readonly List<ScrollViewer> _scrollers = [];
    private int _version;

    // How long the page may take to appear (a page is created the first time it is opened), how long a folded group
    // may take to finish unfolding, and how long the ring stays.
    private const int FindAttempts = 40;
    private static readonly TimeSpan FindInterval = TimeSpan.FromMilliseconds(100);
    private const int SettleAttempts = 16;
    private static readonly TimeSpan SettleInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RingTime = TimeSpan.FromMilliseconds(2600);
    private const double Gap = 4;

    /// <summary>The automation id of the ring, for UI tests.</summary>
    public const string RingAutomationId = "FeatureSpotlightRing";

    /// <summary>
    /// Points at <paramref name="target"/>. False when it never showed: for example a button that only appears once a
    /// character is selected. A newer call ends this one.
    /// </summary>
    public async Task<bool> ShowAsync(string target, string? container, string label, SpotlightMode mode = SpotlightMode.Show)
    {
        int version = ++_version;
        Clear();
        bool opened = false;
        FrameworkElement? element = null;
        for (int attempt = 0; attempt < FindAttempts; attempt++)
        {
            if (container is not null && !opened && Find(container, requireShown: true) is { } group)
            {
                Expand(group);
                opened = true;
            }
            element = Find(target, requireShown: true);
            if (element is not null) break;
            await Task.Delay(FindInterval);
            if (version != _version) return false;
        }
        if (element is null) return false;

        element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.35 });
        // A group that was just unfolded is still growing, and the page may still scroll: wait until the place stops
        // moving, so the ring is drawn where it ends up.
        if (!await SettleAsync(element, version)) return false;
        if (element is Control { IsEnabled: true } control) control.Focus(FocusState.Keyboard);
        switch (mode)
        {
            case SpotlightMode.Use:
                Use(element);
                return true;
            case SpotlightMode.Focus:
                return true;
        }
        Draw(element, label);
        await Task.Delay(RingTime);
        if (version == _version) Clear();
        return true;
    }

    public void Clear()
    {
        if (_tracked is not null) _tracked.LayoutUpdated -= OnTrackedMoved;
        foreach (ScrollViewer scroller in _scrollers) scroller.ViewChanged -= OnScrolling;
        _scrollers.Clear();
        _tracked = null;
        _ring = null;
        _layer?.Children.Clear();
    }

    private async Task<bool> SettleAsync(FrameworkElement element, int version)
    {
        Windows.Foundation.Rect? last = null;
        for (int attempt = 0; attempt < SettleAttempts; attempt++)
        {
            await Task.Delay(SettleInterval);
            if (version != _version || !IsShown(element)) return false;
            Windows.Foundation.Rect bounds = Bounds(element);
            if (last is { } previous && previous == bounds) return true;
            last = bounds;
        }
        return version == _version && IsShown(element);
    }

    private static void Expand(FrameworkElement group)
    {
        switch (group)
        {
            case CommunityToolkit.WinUI.Controls.SettingsExpander settings: settings.IsExpanded = true; break;
            case Expander expander: expander.IsExpanded = true; break;
        }
    }

    // What a click would do: press a button or a clickable card, open a group or a list. Switches and check boxes keep
    // their state: picking a feature never turns something on or off.
    private static void Use(FrameworkElement element)
    {
        if (element is ToggleButton or ToggleSwitch or CheckBox) return;
        switch (element)
        {
            case CommunityToolkit.WinUI.Controls.SettingsExpander settings: settings.IsExpanded = true; return;
            case Expander expander: expander.IsExpanded = true; return;
            case ComboBox comboBox: comboBox.IsDropDownOpen = true; return;
        }
        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider unfold
                 && unfold.ExpandCollapseState != ExpandCollapseState.Expanded) unfold.Expand();
    }

    // The first match on screen: several cards share an automation id, and hidden pages keep their controls.
    private FrameworkElement? Find(string name, bool requireShown)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(_root);
        while (pending.Count > 0)
        {
            DependencyObject current = pending.Pop();
            if (current is FrameworkElement element
                && (element.Name == name || AutomationProperties.GetAutomationId(element) == name)
                && (!requireShown || IsShown(element)))
                return element;
            if (current is UIElement { Visibility: Visibility.Collapsed }) continue;
            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int index = count - 1; index >= 0; index--) pending.Push(VisualTreeHelper.GetChild(current, index));
        }
        return null;
    }

    private bool IsShown(FrameworkElement element)
    {
        if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? current = element; current is not null && !ReferenceEquals(current, _root); current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed } or UIElement { Opacity: <= 0 }) return false;
        }
        return true;
    }

    private Windows.Foundation.Rect Bounds(FrameworkElement element)
        => element.TransformToVisual(_root).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private void Draw(FrameworkElement element, string label)
    {
        if (_layer is null)
        {
            _layer = new Canvas { IsHitTestVisible = false };
            Canvas.SetZIndex(_layer, 900);
            _root.Children.Add(_layer);
        }
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var ring = new Border
        {
            BorderBrush = accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Opacity = 0
        };
        // A faint fill makes the place stand out on busy pages; the ring alone carries the meaning.
        var fill = new Border { CornerRadius = new CornerRadius(6), Background = accent, Opacity = 0.12 };
        // A border has no automation peer; an invisible label names the place for Narrator and UI tests.
        var name = new TextBlock { Text = label, Opacity = 0, IsHitTestVisible = false };
        AutomationProperties.SetAutomationId(name, RingAutomationId);
        var content = new Grid();
        content.Children.Add(fill);
        content.Children.Add(name);
        ring.Child = content;
        _layer.Children.Add(ring);
        _ring = ring;
        _tracked = element;
        Place();
        // The ring stays on the place while the page scrolls, a group unfolds or the window is resized.
        element.LayoutUpdated += OnTrackedMoved;
        for (DependencyObject? current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not ScrollViewer scroller) continue;
            scroller.ViewChanged += OnScrolling;
            _scrollers.Add(scroller);
        }
        // It fades in rather than popping up.
        ring.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(160) };
        if (!_root.DispatcherQueue.TryEnqueue(() => ring.Opacity = 1)) ring.Opacity = 1;
    }

    private void OnTrackedMoved(object? sender, object e) => Place();

    private void OnScrolling(object? sender, ScrollViewerViewChangedEventArgs e) => Place();

    private void Place()
    {
        if (_ring is null || _tracked is null) return;
        // The place went away (another page, a closed group): so does the ring.
        if (!IsShown(_tracked))
        {
            Clear();
            return;
        }
        Windows.Foundation.Rect bounds = Bounds(_tracked);
        _ring.Width = bounds.Width + Gap * 2;
        _ring.Height = bounds.Height + Gap * 2;
        Canvas.SetLeft(_ring, bounds.X - Gap);
        Canvas.SetTop(_ring, bounds.Y - Gap);
    }
}
