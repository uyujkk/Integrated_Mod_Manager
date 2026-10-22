using System.Runtime.InteropServices;

namespace ModFolderCopier.WinUI;

internal sealed record CharacterFrame(int Width, int Height, byte[] Bgra);

/// <summary>Visible desktop pixels in the selected game's client rectangle. No process-memory reads.</summary>
internal static class CharacterScreenCapture
{
    public static bool OverlapsRegion(nint overlay, ShortcutTarget target, IntegratedModManager.Core.CharacterScreenRegion region)
    {
        if (!GetClientRect(target.Handle, out var client) || !GetWindowRect(overlay, out var panel)) return true;
        var origin = new Point();
        if (!ClientToScreen(target.Handle, ref origin)) return true;
        double left = origin.X + region.X * client.Right, top = origin.Y + region.Y * client.Bottom;
        return panel.Left < left + region.Width * client.Right && panel.Right > left
            && panel.Top < top + region.Height * client.Bottom && panel.Bottom > top;
    }
    public static CharacterFrame Capture(ShortcutTarget target)
    {
        if (!ShortcutInputService.IsTargetForeground(target)) throw new InvalidOperationException("请让目标游戏保持前台 / Keep the selected game in the foreground.");
        if (!GetClientRect(target.Handle, out var rect)) throw new InvalidOperationException("Cannot read target bounds.");
        var origin = new Point();
        if (!ClientToScreen(target.Handle, ref origin)) throw new InvalidOperationException("Cannot locate target window.");
        int width = rect.Right, height = rect.Bottom;
        if (width < 32 || height < 32 || (long)width * height > 33_177_600) throw new InvalidOperationException("Unsupported target size.");
        // Reject partly offscreen windows rather than matching unrelated desktop pixels.
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
        if (origin.X < left || origin.Y < top || origin.X + width > left + GetSystemMetrics(78)
            || origin.Y + height > top + GetSystemMetrics(79)) throw new InvalidOperationException("目标窗口需完整显示在屏幕内 / Target must be fully on screen.");
        nint screen = GetDC(0), memory = 0, bitmap = 0, previous = 0;
        try
        {
            memory = CreateCompatibleDC(screen);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out nint pixels, 0, 0);
            if (memory == 0 || bitmap == 0 || pixels == 0) throw new InvalidOperationException("Cannot allocate capture surface.");
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y, 0x00CC0020 | 0x40000000))
                throw new InvalidOperationException("Screen capture failed.");
            var bytes = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            if (!ShortcutInputService.IsTargetForeground(target)) throw new InvalidOperationException("Target focus changed during capture.");
            return new(width, height, bytes);
        }
        finally
        {
            if (previous != 0 && memory != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dc, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
}
