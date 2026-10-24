using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace ModFolderCopier.WinUI;

/// <summary>A separate, non-activating desktop overlay; independent of main-window minimization.</summary>
internal sealed class ShortcutOverlayWindow : Window
{
    public const int MinimumOpacity = 10;
    private const int DefaultWidth = 420, DefaultHeight = 860;
    private readonly nint handle;
    private readonly SubclassProc callback;
    private bool keyboardInputEnabled;
    // Only the background carries the opacity; text and buttons stay fully opaque.
    private readonly SolidColorBrush background = new(Color.FromArgb(255, 28, 31, 38));

    public ShortcutOverlayWindow(UIElement content, Brush textBrush, string title)
    {
        // Keep the "IMM · " prefix in every language: it identifies the overlay in window lists.
        Title = "IMM · " + title;
        SystemBackdrop = new TransparentBackdrop();
        ExtendsContentIntoTitleBar = true;
        var titleBar = new Grid { Height = 32, Padding = new Thickness(12, 0, 140, 0) };
        titleBar.Children.Add(new TextBlock
        {
            Text = Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = textBrush, Opacity = .8
        });
        var root = new Grid { Background = background, RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow((FrameworkElement)content, 1);
        root.Children.Add(titleBar);
        root.Children.Add(content);
        Content = root;
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
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
