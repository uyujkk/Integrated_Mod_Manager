using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Lays its children out left to right and wraps them onto a new line when the next one does not fit, so a row of
/// filter chips never clips or scrolls sideways (the installed toolkit packages have no WrapPanel).
/// </summary>
public sealed class FlowPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(6.0, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(6.0, OnSpacingChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((FlowPanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        double maxWidth = availableSize.Width;
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(maxWidth, double.PositiveInfinity));
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > maxWidth)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            x += size.Width + HorizontalSpacing;
            widest = Math.Max(widest, x - HorizontalSpacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return new Size(double.IsInfinity(maxWidth) ? widest : Math.Min(widest, maxWidth), y + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        var line = new List<(UIElement Child, double X, Size Size)>();
        void Place()
        {
            // Children on one line share its height and are centred in it.
            foreach ((UIElement child, double left, Size size) in line)
                child.Arrange(new Rect(left, y + (lineHeight - size.Height) / 2, Math.Min(size.Width, finalSize.Width), size.Height));
            line.Clear();
        }
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                Place();
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            line.Add((child, x, size));
            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        Place();
        return finalSize;
    }
}
