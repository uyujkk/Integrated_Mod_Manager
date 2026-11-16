using System.Runtime.InteropServices;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace ModFolderCopier.WinUI;

internal sealed record RegionSelection(PixelRect Area, bool LockSize, bool UseAsDefault);
internal sealed record RegionSelectionText(string Hint, string LockSize, string UseAsDefault, string Capture, string Cancel);

/// <summary>A separate, non-activating desktop overlay; independent of main-window minimization.</summary>
internal sealed class ShortcutOverlayWindow : Window
{
    public const int MinimumOpacity = ShortcutPageViewModel.MinimumOverlayOpacity;
    // The title bar is the panel's own (40 DIP): drag area, Mod name, status dot, menu and close. The height follows the
    // content up to DefaultHeight; MinimumHeight keeps the title and one row visible.
    public const int TitleRowHeight = 40;
    private const int FadeMilliseconds = 90, MoveMilliseconds = 220;
    private readonly nint handle;
    private readonly SubclassProc callback;
    private bool keyboardInputEnabled;
    private bool hiddenForCapture;
    private readonly Grid rootGrid;
    private readonly UIElement panelContent;
    private readonly FrameworkElement titleBar;
    private readonly Func<IEnumerable<FrameworkElement>> titleBarControls;
    private double contentHeight;
    // The tallest the user made the panel (DIP); its content decides the height up to this.
    private double maxHeight = OverlayLayoutPolicy.DefaultMaxHeight;
    private bool sizing, resized;
    private readonly Brush textBrush, elementBrush;
    private FrameworkElement? activeLayer;
    private RectInt32 boundsBeforeLayer;
    private Color backgroundBeforeLayer;
    // Only the background carries the opacity; text and buttons stay fully opaque.
    private readonly SolidColorBrush background = new(Color.FromArgb(255, 28, 31, 38));

    /// <param name="titleBar">The panel's title bar; the window makes it the drag area.</param>
    /// <param name="titleBarControls">The buttons in the title bar, which stay clickable instead of dragging.</param>
    /// <param name="placements">Where it was on each display, last used first (OverlayLayoutPolicy).</param>
    public ShortcutOverlayWindow(UIElement content, FrameworkElement titleBar, Func<IEnumerable<FrameworkElement>> titleBarControls,
        Brush textBrush, Brush elementBrush, string title, IReadOnlyList<OverlayPlacement> placements)
    {
        // Keep the "IMM · " prefix in every language: it identifies the overlay in window lists.
        Title = "IMM · " + title;
        SystemBackdrop = new TransparentBackdrop();
        // No system caption: the panel draws its own title bar, whose close button hides the panel (with the hotkey
        // in its tooltip) instead of destroying it.
        ExtendsContentIntoTitleBar = true;
        titleBar.Height = TitleRowHeight;
        this.titleBar = titleBar;
        this.titleBarControls = titleBarControls;
        this.textBrush = textBrush;
        this.elementBrush = elementBrush;
        panelContent = content;
        rootGrid = new Grid { Background = background, RequestedTheme = ElementTheme.Dark };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow((FrameworkElement)content, 1);
        rootGrid.Children.Add(titleBar);
        rootGrid.Children.Add(content);
        Content = rootGrid;
        handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        titleBar.SizeChanged += (_, _) => UpdateDragRegions();
        rootGrid.LayoutUpdated += (_, _) => UpdateDragRegions();
        // Without this DWM composes transparent pixels over black. An empty blur region makes DWM
        // honour per-pixel alpha without blurring (verified: 10% background shows the desktop, text stays opaque).
        nint region = CreateRectRgn(-2, -2, -1, -1);
        DwmEnableBlurBehindWindow(handle, new BlurBehind { Flags = 3, Enable = true, Region = region });
        DeleteObject(region);
        // Caption buttons are drawn by the system over the content; keep them on the panel's colour.
        AppWindowTitleBar bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        bar.ButtonForegroundColor = bar.ButtonInactiveForegroundColor = Color.FromArgb(255, 230, 230, 230);
        bar.ButtonHoverBackgroundColor = Color.FromArgb(60, 255, 255, 255);
        bar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
        bar.ButtonPressedBackgroundColor = Color.FromArgb(90, 255, 255, 255);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }
        // Keep pointer interaction without taking keyboard focus from the game.
        // Expose the independent panel in window switching, while retaining no-activation clicks.
        SetWindowLongPtr(handle, -20, (GetWindowLongPtr(handle, -20) & ~(nint)0x80) | (nint)0x08040000);
        callback = OnMessage;
        if (!SetWindowSubclass(handle, callback, 79, 0)) throw new InvalidOperationException("Overlay window setup failed.");
        Closed += (_, _) => RemoveWindowSubclass(handle, callback, 79);
        // Where it was on a display that is still connected, else the top right of the primary display.
        IReadOnlyList<ConnectedDisplay> displays = Displays();
        OverlayDisplay primary = displays.FirstOrDefault(display => display.IsPrimary)?.Display ?? Fallback();
        var (bounds, savedMaxHeight, _) = OverlayLayoutPolicy.Restore(placements, displays.Select(display => display.Display).ToArray(), primary);
        maxHeight = savedMaxHeight;
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    /// <summary>The user moved or resized the panel: its place on that display, to remember (last used first).</summary>
    public event Action<WindowRect, OverlayDisplay, double>? PlacementChanged;

    private sealed record ConnectedDisplay(OverlayDisplay Display, bool IsPrimary);

    // The connected displays: device name and resolution as the key, the work area and the scale.
    private static IReadOnlyList<ConnectedDisplay> Displays()
    {
        var displays = new List<ConnectedDisplay>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref Rect clip, nint data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfoW(monitor, ref info)) return true;
            uint dpi = GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0 ? dpiX : 96;
            Rect bounds = info.Monitor, work = info.Work;
            string key = $"{info.Device}|{bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}";
            displays.Add(new ConnectedDisplay(new OverlayDisplay(key, new WindowRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), dpi / 96.0),
                (info.Flags & 1) != 0));
            return true;
        }, 0);
        return displays;
    }

    private OverlayDisplay Fallback()
    {
        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        return new OverlayDisplay(string.Empty, new WindowRect(area.X, area.Y, area.Width, area.Height), GetDpiForWindow(handle) / 96.0);
    }

    private WindowRect CurrentBounds() => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

    // The display the panel is mostly on now.
    private OverlayDisplay CurrentDisplay(WindowRect bounds)
        => OverlayLayoutPolicy.DisplayOf(Displays().Select(display => display.Display).ToArray(), bounds) ?? Fallback();

    public void ShowPanel() => AppWindow.Show(false);

    // The title bar drags the window; its buttons stay buttons. Rects are in client pixels.
    private RectInt32[] lastCaption = [], lastPassthrough = [];

    private void UpdateDragRegions()
    {
        if (titleBar.XamlRoot is null || titleBar.ActualWidth <= 0) return;
        double scale = titleBar.XamlRoot.RasterizationScale;
        RectInt32 Bounds(FrameworkElement element)
        {
            Windows.Foundation.Rect rect = element.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return new RectInt32((int)Math.Round(rect.X * scale), (int)Math.Round(rect.Y * scale), (int)Math.Round(rect.Width * scale), (int)Math.Round(rect.Height * scale));
        }
        RectInt32[] caption = [Bounds(titleBar)];
        RectInt32[] passthrough = titleBarControls().Where(control => control.Visibility == Visibility.Visible && control.ActualWidth > 0).Select(Bounds).ToArray();
        if (caption.SequenceEqual(lastCaption) && passthrough.SequenceEqual(lastPassthrough)) return;
        lastCaption = caption;
        lastPassthrough = passthrough;
        InputNonClientPointerSource source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        source.SetRegionRects(NonClientRegionKind.Caption, caption);
        source.SetRegionRects(NonClientRegionKind.Passthrough, passthrough);
    }

    /// <summary>
    /// The panel's height follows its content (DIP below the title bar): at least the title and a row, at most
    /// <see cref="DefaultHeight"/> and the screen; longer content scrolls.
    /// </summary>
    public void FitContentHeight(double contentDip)
    {
        if (activeLayer is not null || double.IsNaN(contentDip) || contentDip <= 0) return;
        contentHeight = contentDip;
        double scale = GetDpiForWindow(handle) / 96.0;
        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        if (sizing) return;
        // The window's size includes its resize frame; the content gets what the XAML root has.
        // Before the first layout the root has no size yet; a frame is only ever a few pixels.
        int frame = rootGrid.XamlRoot is { Size.Height: > 0 } root ? AppWindow.Size.Height - (int)Math.Round(root.Size.Height * root.RasterizationScale) : 0;
        frame = frame is > 0 and < 32 ? frame : 0;
        int wanted = OverlayLayoutPolicy.FitHeight(TitleRowHeight + contentDip, maxHeight, scale, area.Height, frame);
        if (Math.Abs(AppWindow.Size.Height - wanted) <= 1) return;
        AppWindow.Resize(new SizeInt32(AppWindow.Size.Width, wanted));
    }

    /// <summary>The content height last fitted, in DIP (for tests and the layout after a layer).</summary>
    public double ContentHeight => contentHeight;

    // Pointer input reaches whatever is behind the window. Windows only honours this on a layered window.
    public void SetClickThrough(bool enabled)
    {
        const nint transparent = 0x20, layered = 0x80000;
        nint style = GetWindowLongPtr(handle, -20);
        if (enabled)
        {
            SetWindowLongPtr(handle, -20, style | transparent | layered);
            SetLayeredWindowAttributes(handle, 0, 255, 2); // LWA_ALPHA: fully opaque, so the per-pixel background is unchanged.
        }
        else SetWindowLongPtr(handle, -20, style & ~(transparent | layered));
    }

    /// <summary>Turns the panel into an empty bordered frame the user moves and resizes. Returns the framed area on screen, or null when cancelled.</summary>
    public async Task<RegionSelection?> SelectRegionAsync(PixelRect start, RegionSelectionText text, bool lockSize, bool useAsDefault, bool animate)
    {
        var result = new TaskCompletionSource<RegionSelection?>();
        var lockBox = new CheckBox { Content = text.LockSize, Foreground = textBrush, IsChecked = lockSize, AllowFocusOnInteraction = false };
        var defaultBox = new CheckBox { Content = text.UseAsDefault, Foreground = textBrush, IsChecked = useAsDefault, AllowFocusOnInteraction = false };
        SetResizable(!lockSize);
        lockBox.Click += (_, _) => SetResizable(lockBox.IsChecked != true);
        Closed += (_, _) => result.TrySetResult(null);
        var captureButton = new Button { Content = text.Capture, Foreground = textBrush, AllowFocusOnInteraction = false };
        var cancelButton = new Button { Content = text.Cancel, Foreground = textBrush, AllowFocusOnInteraction = false };
        captureButton.Click += (_, _) =>
        {
            int title = TitleHeightInPixels;
            result.TrySetResult(CharacterScreenCapture.ClientScreenRect(handle) is { } client
                ? new RegionSelection(client with { Y = client.Y + title, Height = client.Height - title }, lockBox.IsChecked == true, defaultBox.IsChecked == true) : null);
        };
        cancelButton.Click += (_, _) => result.TrySetResult(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(captureButton);
        buttons.Children.Add(cancelButton);
        var bar = new StackPanel { Spacing = 6, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Bottom };
        bar.Children.Add(new Border
        {
            Background = elementBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock { Text = text.Hint, FontSize = 12, Foreground = textBrush, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }
        });
        var options = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        options.Children.Add(lockBox);
        options.Children.Add(defaultBox);
        bar.Children.Add(new Border { Background = elementBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 0, 8, 0), Child = options });
        bar.Children.Add(buttons);
        var layer = new Grid();
        layer.Children.Add(new Border
        {
            BorderThickness = new Thickness(2), IsHitTestVisible = false,
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 140, 90))
        });
        layer.Children.Add(bar);
        int titleHeight = TitleHeightInPixels;
        // Nearly invisible, but the frame still receives the pointer.
        await EnterLayerAsync(layer, new PixelRect(start.X, start.Y - titleHeight, start.Width, start.Height + titleHeight), Color.FromArgb(1, 28, 31, 38), animate);
        return await result.Task;
    }

    /// <summary>A rectangle of the given size in device-independent pixels, centred on this window's screen.</summary>
    public PixelRect CenteredOnScreen(int width, int height)
    {
        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(handle) / 96.0;
        int pixelWidth = Math.Min((int)(width * scale), area.Width), pixelHeight = Math.Min((int)(height * scale), area.Height);
        return new PixelRect(area.X + (area.Width - pixelWidth) / 2, area.Y + (area.Height - pixelHeight) / 2, pixelWidth, pixelHeight);
    }

    /// <summary>Fades the panel out, moves the window to the bounds and fades the layer in. Undone by <see cref="LeaveLayerAsync"/>.</summary>
    public async Task EnterLayerAsync(FrameworkElement layer, PixelRect bounds, Color? layerBackground, bool animate)
    {
        boundsBeforeLayer = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        backgroundBeforeLayer = background.Color;
        await FadeAsync(panelContent, 0, animate);
        panelContent.Visibility = Visibility.Collapsed;
        if (layerBackground is { } color) background.Color = color;
        await AnimateBoundsAsync(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height), animate);
        Grid.SetRow(layer, 1);
        activeLayer = layer;
        await FadeAsync(layer, 0, false);
        rootGrid.Children.Add(layer);
        await FadeAsync(layer, 1, animate);
    }

    public async Task LeaveLayerAsync(bool animate)
    {
        if (activeLayer is not { } layer) return;
        activeLayer = null;
        await FadeAsync(layer, 0, animate);
        rootGrid.Children.Remove(layer);
        background.Color = backgroundBeforeLayer;
        SetResizable(true);
        await AnimateBoundsAsync(boundsBeforeLayer, animate);
        panelContent.Visibility = Visibility.Visible;
        await FadeAsync(panelContent, 1, animate);
    }

    private void SetResizable(bool resizable)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsResizable = resizable;
    }

    private int TitleHeightInPixels => (int)(TitleRowHeight * GetDpiForWindow(handle) / 96.0);

    // Runs on the compositor, so the fade stays smooth while the window itself is being moved.
    private static async Task FadeAsync(UIElement element, float opacity, bool animate)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (!animate)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = opacity;
            return;
        }
        ScalarKeyFrameAnimation fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, opacity);
        fade.Duration = TimeSpan.FromMilliseconds(FadeMilliseconds);
        visual.StartAnimation("Opacity", fade);
        await Task.Delay(FadeMilliseconds);
    }

    // Driven by the render callback and elapsed time, not a fixed timer: each frame lands on a screen refresh
    // and a slow frame is skipped rather than stretching the animation. The panel is empty by now, so resizing is cheap.
    private Task AnimateBoundsAsync(RectInt32 to, bool animate)
    {
        if (!animate)
        {
            AppWindow.MoveAndResize(to);
            return Task.CompletedTask;
        }
        int fromX = AppWindow.Position.X, fromY = AppWindow.Position.Y, fromWidth = AppWindow.Size.Width, fromHeight = AppWindow.Size.Height;
        var done = new TaskCompletionSource();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        EventHandler<object>? frame = null;
        frame = (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / MoveMilliseconds), eased = 1 - Math.Pow(1 - t, 3);
            int Mix(int from, int target) => from + (int)Math.Round((target - from) * eased);
            AppWindow.MoveAndResize(new RectInt32(Mix(fromX, to.X), Mix(fromY, to.Y), Mix(fromWidth, to.Width), Mix(fromHeight, to.Height)));
            if (t < 1) return;
            CompositionTarget.Rendering -= frame;
            done.TrySetResult();
        };
        CompositionTarget.Rendering += frame;
        return done.Task;
    }

    /// <summary>Whether the window can be left out of a capture without hiding it from the user.</summary>
    public static bool CanExcludeFromCapture => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    public bool ShowsLayer => activeLayer is not null;

    public PixelRect? ClientArea => CharacterScreenCapture.ClientScreenRect(handle);

    // Only for the moment of a capture: a lasting exclusion would also hide the overlay from recordings and streams.
    public void BeginCaptureExclusion()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && SetWindowDisplayAffinity(handle, 0x11)) return; // WDA_EXCLUDEFROMCAPTURE
        hiddenForCapture = true;
        AppWindow.Hide();
    }

    public void EndCaptureExclusion()
    {
        if (!hiddenForCapture) { SetWindowDisplayAffinity(handle, 0); return; }
        hiddenForCapture = false;
        ShowPanel();
    }

    // Naming a preset needs real keyboard/IME focus; ordinary shortcut clicks must not take it.
    public void SetKeyboardInputEnabled(bool enabled)
    {
        keyboardInputEnabled = enabled;
        nint style = GetWindowLongPtr(handle, -20);
        SetWindowLongPtr(handle, -20, enabled ? style & ~(nint)0x08000000 : style | (nint)0x08000000);
        if (enabled) Activate();
    }

    public void SetBackgroundOpacity(int percent) =>
        background.Color = Color.FromArgb((byte)Math.Round(Math.Clamp(percent, MinimumOpacity, 100) * 2.55), 28, 31, 38);

    private nint OnMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        switch (message)
        {
            case 0x0021 when !keyboardInputEnabled:
                return 3; // WM_MOUSEACTIVATE -> MA_NOACTIVATE, still deliver the click.
            case 0x0024 when activeLayer is null: // WM_GETMINMAXINFO: no smaller than the title bar and a row
            {
                nint result = DefSubclassProc(hwnd, message, wParam, lParam);
                double scale = GetDpiForWindow(hwnd) / 96.0;
                MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                info.MinTrackWidth = (int)Math.Round(OverlayLayoutPolicy.MinimumWidth * scale);
                info.MinTrackHeight = (int)Math.Round(OverlayLayoutPolicy.MinimumHeight * scale);
                Marshal.StructureToPtr(info, lParam, false);
                return result;
            }
            case 0x0231: // WM_ENTERSIZEMOVE
                sizing = true;
                resized = false;
                break;
            case 0x0214: // WM_SIZING: the user drags an edge
                resized = true;
                break;
            case 0x0216 when activeLayer is null: // WM_MOVING: stick to the work area's edges
            {
                Rect rect = Marshal.PtrToStructure<Rect>(lParam);
                var moving = new WindowRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                WindowRect snapped = OverlayLayoutPolicy.Snap(moving, CurrentDisplay(moving));
                if (snapped != moving)
                {
                    rect = new Rect { Left = snapped.X, Top = snapped.Y, Right = snapped.X + snapped.Width, Bottom = snapped.Y + snapped.Height };
                    Marshal.StructureToPtr(rect, lParam, false);
                    return 1;
                }
                break;
            }
            case 0x0232: // WM_EXITSIZEMOVE: remember where it is now, and how tall the user allows it to be
            {
                sizing = false;
                if (activeLayer is not null) break;
                WindowRect bounds = CurrentBounds();
                OverlayDisplay display = CurrentDisplay(bounds);
                if (resized) maxHeight = Math.Max(OverlayLayoutPolicy.MinimumHeight, bounds.Height / display.Scale);
                PlacementChanged?.Invoke(bounds, display, maxHeight);
                if (resized && contentHeight > 0) FitContentHeight(contentHeight);
                break;
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    /// <summary>A fully transparent system backdrop, so the content's own brush alpha shows the desktop/game.</summary>
    private sealed class TransparentBackdrop : SystemBackdrop
    {
        private static Windows.UI.Composition.Compositor? compositor;
        private static nint queueController;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);
            // The system compositor needs a Windows.System dispatcher queue on this thread.
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null && queueController == 0)
                CreateDispatcherQueueController(new() { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 0 }, out queueController);
            compositor ??= new Windows.UI.Composition.Compositor();
            connectedTarget.SystemBackdrop = compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            disconnectedTarget.SystemBackdrop = null;
            base.OnTargetDisconnected(disconnectedTarget);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind
    { public uint Flags; [MarshalAs(UnmanagedType.Bool)] public bool Enable; public nint Region; [MarshalAs(UnmanagedType.Bool)] public bool Transition; }
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd, in BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [StructLayout(LayoutKind.Sequential)] private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    { public int ReservedX, ReservedY, MaxSizeX, MaxSizeY, MaxPositionX, MaxPositionY, MinTrackWidth, MinTrackHeight, MaxTrackWidth, MaxTrackHeight; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfoEx
    {
        public int Size; public Rect Monitor; public Rect Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
