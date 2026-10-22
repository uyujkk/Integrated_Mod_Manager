using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using IntegratedModManager.Core;

namespace ModFolderCopier.WinUI;

internal sealed record ShortcutTarget(nint Handle, uint ProcessId, long StartTicks, string Label)
{
    public override string ToString() => Label;
}

/// <summary>User-triggered foreground keyboard input. No hooks or game-process access.</summary>
internal sealed class ShortcutInputService : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;
    private long lastSend;

    public static IReadOnlyList<ShortcutTarget> ListTargets()
    {
        var result = new List<ShortcutTarget>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId) return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                result.Add(new(hwnd, pid, process.StartTime.ToUniversalTime().Ticks, $"{title} · {process.ProcessName} ({pid})"));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return result.Count < 200;
        }, 0);
        return result.OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task SendAsync(ShortcutTarget target, ShortcutKeyPlan plan, int holdMilliseconds)
    {
        if (busy || Environment.TickCount64 - lastSend < 300) throw new InvalidOperationException("Busy / 操作过快，请稍候。");
        if (lifetime.IsCancellationRequested) throw new OperationCanceledException();
        busy = true;
        var pressed = new List<ushort>();
        try
        {
            if (!Matches(target)) throw new InvalidOperationException("Target window closed or changed / 目标窗口已关闭或变化，请重新选择。");
            if (IsIconic(target.Handle)) ShowWindow(target.Handle, 9);
            if (GetForegroundWindow() != target.Handle) SetForegroundWindow(target.Handle);
            await Task.Delay(80, lifetime.Token);
            if (!Matches(target) || GetForegroundWindow() != target.Handle)
                throw new InvalidOperationException("Cannot focus target / 无法激活目标窗口，请先切换到游戏。");
            // Never release or alter keys held by the user. Extra modifiers change a chord's meaning.
            ushort[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];
            if (modifiers.Concat(plan.Keys).Concat(plan.ForbiddenKeys).Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
                throw new InvalidOperationException("Release keyboard keys first / 请先松开键盘上的按键。");
            Input[] down = plan.Keys.Select(k => KeyInput(k, false)).ToArray();
            uint sent = SendInput((uint)down.Length, down, Marshal.SizeOf<Input>());
            pressed.AddRange(plan.Keys.Take((int)sent));
            if (sent != down.Length)
                throw new InvalidOperationException("Input blocked; check target permissions / 按键发送被阻止，请检查目标窗口与程序权限。");
            long until = Environment.TickCount64 + Math.Clamp(holdMilliseconds, 60, 2000);
            while (Environment.TickCount64 < until)
            {
                await Task.Delay(20, lifetime.Token);
                if (!Matches(target) || GetForegroundWindow() != target.Handle)
                    throw new InvalidOperationException("Focus changed; keys released / 焦点发生变化，已释放按键。");
            }
        }
        finally
        {
            if (pressed.Count > 0)
            {
                Input[] up = pressed.AsEnumerable().Reverse().Select(k => KeyInput(k, true)).ToArray();
                if (SendInput((uint)up.Length, up, Marshal.SizeOf<Input>()) != up.Length)
                    MainWindow.LogApplicationIssueWithStack("Shortcut key release", new InvalidOperationException("Windows rejected key-release input."));
            }
            lastSend = Environment.TickCount64;
            busy = false;
        }
    }

    private static bool Matches(ShortcutTarget target)
    {
        if (!IsWindow(target.Handle) || !IsWindowVisible(target.Handle)) return false;
        GetWindowThreadProcessId(target.Handle, out uint pid);
        if (pid != target.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.StartTime.ToUniversalTime().Ticks == target.StartTicks;
        }
        catch { return false; }
    }

    internal static bool IsTargetForeground(ShortcutTarget target) => Matches(target) && GetForegroundWindow() == target.Handle;
    internal static bool FocusTarget(ShortcutTarget target)
    {
        if (!Matches(target)) return false;
        if (IsIconic(target.Handle)) ShowWindow(target.Handle, 9);
        return GetForegroundWindow() == target.Handle || SetForegroundWindow(target.Handle);
    }

    private static Input KeyInput(ushort key, bool up)
    {
        bool extended = key is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5D or 0x6F or 0xA3 or 0xA5;
        return new() { Type = 1, Data = new() { Keyboard = new()
        { Vk = key, Scan = (ushort)(MapVirtualKey(key, 0) & 0xFF), Flags = (up ? 2u : 0u) | (extended ? 1u : 0u) } } };
    }

    public void Dispose() => lifetime.Cancel();

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    private delegate bool EnumWindowProc(nint hwnd, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
