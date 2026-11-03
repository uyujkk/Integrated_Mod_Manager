using System.Runtime.InteropServices;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Composition;
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
    public const int MinimumOpacity = 10;
    private const int DefaultWidth = 420, DefaultHeight = 860, TitleRowHeight = 32, FadeMilliseconds = 90, MoveMilliseconds = 220;
    private readonly nint handle;
    private readonly SubclassProc callback;
    private bool keyboardInputEnabled;
    private bool hiddenForCapture;
    private readonly Grid rootGrid;
    private readonly UIElement panelContent;
    private readonly Brush textBrush, elementBrush;
    private FrameworkElement? activeLayer;
    private RectInt32 boundsBeforeLayer;
    private Color backgroundBeforeLayer;
    // Only the background carries the opacity; text and buttons stay fully opaque.
    private readonly SolidColorBrush background = new(Color.FromArgb(255, 28, 31, 38));

    public ShortcutOverlayWindow(UIElement content, Brush textBrush, Brush elementBrush, string title)
    {
        // Keep the "IMM · " prefix in every language: it identifies the overlay in window lists.
        Title = "IMM · " + title;
        SystemBackdrop = new TransparentBackdrop();
        ExtendsContentIntoTitleBar = true;
        var titleBar = new Grid { Height = TitleRowHeight, Padding = new Thickness(12, 0, 140, 0) };
        titleBar.Children.Add(new Border
        {
            Background = elementBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Title, FontSize = 12, Foreground = textBrush }
        });
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
        SetTitleBar(titleBar);
        handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
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
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(handle) / 96.0;
        int width = Math.Min((int)(DefaultWidth * scale), area.Width);
        int height = Math.Min((int)(DefaultHeight * scale), area.Height - (int)(40 * scale));
        AppWindow.Resize(new SizeInt32(width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        // Keep pointer interaction without taking keyboard focus from the game.
        // Expose the independent panel in window switching, while retaining no-activation clicks.
        SetWindowLongPtr(handle, -20, (GetWindowLongPtr(handle, -20) & ~(nint)0x80) | (nint)0x08040000);
        callback = OnMessage;
        if (!SetWindowSubclass(handle, callback, 79, 0)) throw new InvalidOperationException("Overlay window setup failed.");
        Closed += (_, _) => RemoveWindowSubclass(handle, callback, 79);
        AppWindow.Move(new PointInt32(area.X + Math.Max(0, area.Width - width - (int)(20 * scale)), area.Y + (int)(20 * scale)));
    }

    public void ShowPanel() => AppWindow.Show(false);

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
        if (message == 0x0021 && !keyboardInputEnabled) return 3; // WM_MOUSEACTIVATE -> MA_NOACTIVATE, still deliver the click.
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
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
