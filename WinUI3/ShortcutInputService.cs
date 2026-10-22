using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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

    public async Task<string> SendAsync(ShortcutTarget target, ShortcutKeyPlan plan, int holdMilliseconds,
        ShortcutInputMode mode = ShortcutInputMode.ScanCode)
    {
        if (busy || Environment.TickCount64 - lastSend < 300) throw new InvalidOperationException("Busy / 操作过快，请稍候。");
        if (lifetime.IsCancellationRequested) throw new OperationCanceledException();
        busy = true;
        var pressed = new List<ushort>();
        Input[] down = [];
        Input[] up = [];
        uint downSent = 0, upSent = 0;
        bool? keyStateObserved = null;
        string? failure = null;
        string security = string.Empty;
        string report = string.Empty;
        int lastError = 0;
        var timing = Stopwatch.StartNew();
        try
        {
            if (!Matches(target)) throw new InvalidOperationException("Target window closed or changed / 目标窗口已关闭或变化，请重新选择。");
            ShortcutSecurity own = ShortcutProcessSecurity.Read((uint)Environment.ProcessId);
            ShortcutSecurity destination = ShortcutProcessSecurity.Read(target.ProcessId);
            security = $"sender={own.Summary}; target={destination.Summary}";
            if (!own.UiAccess && own.Integrity is int senderLevel && destination.Integrity is int targetLevel && targetLevel > senderLevel)
                throw new InvalidOperationException("目标窗口权限高于管理器，Windows 会阻止输入。请以相同权限重新打开管理器。 / Target has higher integrity; restart the manager at the same permission level.");
            if (IsIconic(target.Handle)) ShowWindow(target.Handle, 9);
            if (GetForegroundWindow() != target.Handle) SetForegroundWindow(target.Handle);
            await Task.Delay(80, lifetime.Token);
            if (!Matches(target) || GetForegroundWindow() != target.Handle)
                throw new InvalidOperationException("Cannot focus target / 无法激活目标窗口，请先切换到游戏。");
            // Never release or alter keys held by the user. Extra modifiers change a chord's meaning.
            ushort[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];
            if (modifiers.Concat(plan.Keys).Concat(plan.ForbiddenKeys).Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
                throw new InvalidOperationException("Release keyboard keys first / 请先松开键盘上的按键。");
            if (mode == ShortcutInputMode.ScanCode && plan.Keys.Any(k => k is >= 0x60 and <= 0x69 or 0x6E)
                && (GetKeyState(0x90) & 1) == 0)
                throw new InvalidOperationException("扫描码小键盘数字需要 Num Lock 开启，请点击“切换 Num Lock”或改用虚拟键模式。 / Turn Num Lock on for scan-code numpad digits, or use virtual keys.");
            uint thread = GetWindowThreadProcessId(target.Handle, out _);
            nint layout = GetKeyboardLayout(thread);
            down = plan.Keys.Select(k => KeyInput(k, false, mode, layout)).ToArray();
            up = plan.Keys.Reverse().Select(k => KeyInput(k, true, mode, layout)).ToArray();
            downSent = SendInput((uint)down.Length, down, Marshal.SizeOf<Input>());
            lastError = downSent == down.Length ? 0 : Marshal.GetLastWin32Error();
            pressed.AddRange(plan.Keys.Take((int)downSent));
            if (downSent != down.Length)
                throw new InvalidOperationException($"Windows 拒绝输入（{downSent}/{down.Length}, error={lastError}）；请检查权限。 / Windows rejected input; check permissions.");
            long until = Environment.TickCount64 + Math.Clamp(holdMilliseconds, 60, 2000);
            while (Environment.TickCount64 < until)
            {
                await Task.Delay(20, lifetime.Token);
                if (!Matches(target) || GetForegroundWindow() != target.Handle)
                    throw new InvalidOperationException("Focus changed; keys released / 焦点发生变化，已释放按键。");
                keyStateObserved = keyStateObserved == true || plan.Keys.All(k => (GetAsyncKeyState(k) & 0x8000) != 0);
            }
            report = keyStateObserved == true
                ? "Windows 已接收，已观察到按下状态；游戏响应待确认。 / Windows accepted input and key-down state was observed; game response is unverified."
                : "Windows 已接收，但未观察到完整按下状态；请查看输入诊断。 / Windows accepted events, but full key-down state was not observed; see diagnostics.";
        }
        catch (Exception ex) { failure = ex.Message; throw; }
        finally
        {
            if (pressed.Count > 0)
            {
                // Reuse the exact layout and encoding from key-down, including partial sends.
                Input[] release = downSent == down.Length ? up : up.Skip(up.Length - pressed.Count).ToArray();
                upSent = SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
                if (upSent != release.Length)
                {
                    failure = "Windows rejected key-release input.";
                    MainWindow.LogApplicationIssueWithStack("Shortcut key release", new InvalidOperationException("Windows rejected key-release input."));
                    report = "按键释放被 Windows 拒绝，请立即松开并检查键盘状态。 / Windows rejected key release; check keyboard state.";
                }
            }
            WriteInputDiagnostic(new { Utc = DateTimeOffset.UtcNow, TargetPid = target.ProcessId,
                Mode = mode.ToString(), Keys = plan.Keys.Select(k => $"0x{k:X2}").ToArray(),
                Packets = down.Select(i => new { i.Data.Keyboard.Vk, i.Data.Keyboard.Scan, i.Data.Keyboard.Flags }).ToArray(),
                DurationMs = holdMilliseconds, ElapsedMs = timing.ElapsedMilliseconds,
                DownAccepted = downSent, UpAccepted = upSent, KeyStateObserved = keyStateObserved,
                Security = security, Foreground = GetForegroundWindow().ToInt64(), Failure = failure, LastError = lastError });
            lastSend = Environment.TickCount64;
            busy = false;
        }
        return report;
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

    private static Input KeyInput(ushort key, bool up, ShortcutInputMode mode, nint layout)
    {
        ushort physical = key switch { 0x10 => 0xA0, 0x11 => 0xA2, 0x12 => 0xA4, _ => key };
        var packet = KeyboardInputPacket.Create(key, MapVirtualKeyEx(physical, 4, layout), up, mode);
        return new() { Type = 1, Data = new() { Keyboard = new()
        { Vk = packet.VirtualKey, Scan = packet.Scan, Flags = packet.Flags } } };
    }

    private static void WriteInputDiagnostic(object entry)
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "shortcut-input.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch { /* A local diagnostic failure must not interrupt key release. */ }
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
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")] private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
