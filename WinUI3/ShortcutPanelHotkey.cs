using System.Runtime.InteropServices;

namespace ModFolderCopier.WinUI;

internal sealed class ShortcutPanelHotkey : IDisposable
{
    private readonly nint handle;
    private readonly WindowProc callback;
    private readonly Action toggle;
    private int registeredId;

    public ShortcutPanelHotkey(nint handle, Action toggle)
    {
        this.handle = handle;
        this.toggle = toggle;
        callback = OnMessage;
        if (!SetWindowSubclass(handle, callback, 81, 0)) throw new InvalidOperationException("Global hotkey window setup failed.");
    }

    public bool TrySet(uint modifiers, uint key)
    {
        int next = registeredId == 0x494D ? 0x494E : 0x494D;
        // Register first: a collision must preserve the user's previous working shortcut.
        if (!RegisterHotKey(handle, next, modifiers | 0x4000, key)) return false;
        if (registeredId != 0) UnregisterHotKey(handle, registeredId);
        registeredId = next;
        return true;
    }

    public void Disable()
    {
        if (registeredId != 0) UnregisterHotKey(handle, registeredId);
        registeredId = 0;
    }

    public void Dispose()
    {
        Disable();
        RemoveWindowSubclass(handle, callback, 81);
    }

    private nint OnMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0312 && registeredId != 0 && (int)wParam == registeredId)
        {
            toggle();
            return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, WindowProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, WindowProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
