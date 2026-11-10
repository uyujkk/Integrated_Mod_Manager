using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace ModFolderCopier.WinUI;

/// <summary>
/// A vertical splitter between two panes (stage J7-b): drag it with the pointer, or focus it and use the arrow keys.
/// It reports how far it moved; the owner decides the widths, so they stay within its own bounds.
/// </summary>
internal sealed partial class PaneSplitter : ContentControl
{
    private const double KeyStep = 16;
    private readonly Rectangle _line;
    private double _pressedX;
    private bool _dragging;

    public PaneSplitter()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        _line = new Rectangle { Width = 2, RadiusX = 1, RadiusY = 1, Margin = new Thickness(0, 24, 0, 24), HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
        Content = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Children = { _line } };
        PointerEntered += (_, _) => ShowLine(true);
        PointerExited += (_, _) => ShowLine(_dragging || FocusState != FocusState.Unfocused);
        GotFocus += (_, _) => ShowLine(true);
        LostFocus += (_, _) => ShowLine(_dragging);
    }

    /// <summary>The pointer or a key moved the splitter this far (DIPs, positive to the right) since the last report.</summary>
    public event EventHandler<double>? Moved;

    /// <summary>A drag or a key press ended; the owner keeps the widths it now has.</summary>
    public event EventHandler? Completed;

    public Brush? LineBrush { get => _line.Fill; set => _line.Fill = value; }

    private void ShowLine(bool show) => _line.Opacity = show ? 1 : 0;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = CapturePointer(e.Pointer);
        _pressedX = e.GetCurrentPoint(null).Position.X;
        Focus(FocusState.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        double x = e.GetCurrentPoint(null).Position.X;
        double delta = x - _pressedX;
        if (Math.Abs(delta) < 0.5) return;
        _pressedX = x;
        Moved?.Invoke(this, delta);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag(null);
    }

    private void EndDrag(Pointer? pointer)
    {
        if (!_dragging) return;
        _dragging = false;
        if (pointer is not null) ReleasePointerCapture(pointer);
        ShowLine(FocusState != FocusState.Unfocused);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        double delta = e.Key switch { VirtualKey.Left => -KeyStep, VirtualKey.Right => KeyStep, _ => 0 };
        if (delta == 0) return;
        Moved?.Invoke(this, delta);
        Completed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
