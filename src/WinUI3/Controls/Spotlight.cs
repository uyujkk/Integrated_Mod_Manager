using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// "Take me there" for the search's feature directory: finds a control by its x:Name or automation id once its page
/// is on screen, opens the folded group it sits in, scrolls it into view, gives it keyboard focus and draws an accent
/// ring around it for a moment. Nothing is clicked or changed; the ring takes no input. The window gives it its root.
/// </summary>
internal sealed class Spotlight(Grid root)
{
    private readonly Grid _root = root;
    private Canvas? _layer;
    private int _version;

    // How long the page may take to appear (a page is created the first time it is opened) and how long the ring stays.
    private const int FindAttempts = 40;
    private static readonly TimeSpan FindInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RingTime = TimeSpan.FromMilliseconds(2600);

    /// <summary>The automation id of the ring, for UI tests.</summary>
    public const string RingAutomationId = "FeatureSpotlightRing";

    /// <summary>
    /// Points at <paramref name="target"/>. False when it never showed: for example a button that only appears once a
    /// character is selected. A newer call ends this one.
    /// </summary>
    public async Task<bool> ShowAsync(string target, string? container, string label)
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
        await Task.Delay(150);
        if (version != _version || !IsShown(element)) return false;
        _root.UpdateLayout();
        if (element is Control { IsEnabled: true } control) control.Focus(FocusState.Keyboard);
        Draw(element, label);
        await Task.Delay(RingTime);
        if (version == _version) Clear();
        return true;
    }

    public void Clear() => _layer?.Children.Clear();

    private static void Expand(FrameworkElement group)
    {
        switch (group)
        {
            case CommunityToolkit.WinUI.Controls.SettingsExpander settings: settings.IsExpanded = true; break;
            case Expander expander: expander.IsExpanded = true; break;
        }
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

    private void Draw(FrameworkElement element, string label)
    {
        if (_layer is null)
        {
            _layer = new Canvas { IsHitTestVisible = false };
            Canvas.SetZIndex(_layer, 900);
            _root.Children.Add(_layer);
        }
        Windows.Foundation.Rect bounds = element.TransformToVisual(_root)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        const double gap = 4;
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var ring = new Border
        {
            Width = bounds.Width + gap * 2, Height = bounds.Height + gap * 2,
            BorderBrush = accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
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
        Canvas.SetLeft(ring, bounds.X - gap);
        Canvas.SetTop(ring, bounds.Y - gap);
        _layer.Children.Add(ring);
    }
}
