using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ModFolderCopier.WinUI;

/// <summary>A separate, non-activating desktop overlay; independent of main-window minimization.</summary>
internal sealed class ShortcutOverlayWindow : Window
{
    private readonly nint handle;
    private readonly SubclassProc callback;

    public ShortcutOverlayWindow(UIElement content)
    {
        Title = "IMM · Mod shortcuts";
        Content = content;
        handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Resize(new SizeInt32(400, 580));
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
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + Math.Max(0, area.Width - 420), area.Y + 60));
    }

    public void ShowPanel() => AppWindow.Show(false);

    private nint OnMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0021) return 3; // WM_MOUSEACTIVATE -> MA_NOACTIVATE, still deliver the click.
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
