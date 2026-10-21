// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Modified 2026-10-21: the standalone bridge's command-line entry runs inside the manager's
// own executable without a window; see NOTICE.md.
using System.Runtime.InteropServices;

namespace IntegratedModManager.EndfieldLaunch;

/// <summary>
/// Window-less entry points handled by the manager executable before any UI starts:
/// <c>--endfield-launch</c> (started elevated by the manager) and
/// <c>--endfield-game-only --from-xxmi</c> (XXMI's Custom Launch command).
/// </summary>
public static class LaunchCommand
{
    public const string LaunchVerb = "--endfield-launch";
    public const string GameOnlyVerb = "--endfield-game-only";

    /// <summary>Held by every launch process; the updater refuses to replace files while it exists.</summary>
    public const string RunningMutexName = @"Local\IntegratedModManager.EndfieldLaunch.Running";
    private const string GameSessionMutexName = @"Local\IntegratedModManager.EndfieldGameSession";

    public static bool IsLaunchCommand(string[] args) =>
        args.Length > 0 && (args[0] == LaunchVerb || args[0] == GameOnlyVerb);

    public static bool IsLaunchRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(RunningMutexName, out Mutex? mutex)) return false;
            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Created by the elevated launch process: it exists even if this token cannot open it.
            return true;
        }
    }

    public static int Run(string[] args, string dataDirectory, string executablePath)
    {
        using var running = new Mutex(false, RunningMutexName);
        var store = new LaunchStore(dataDirectory);
        bool english = false;
        string session = string.Empty;
        bool fromXxmi = args.SequenceEqual(new[] { GameOnlyVerb, "--from-xxmi" });
        try
        {
            LaunchProfile profile = store.LoadProfile();
            english = profile.English;
            session = profile.SessionId;
            void Log(LaunchText text) => store.Log(text, english);
            if (!EndfieldGame.IsAdministrator())
            {
                throw fromXxmi
                    ? new EndfieldLaunchException("请以管理员身份运行 XXMI，或从管理器的“运行启动器”启动 Mod，避免在加载过程中提权超时。",
                        "Run XXMI as administrator, or start Mods from Run Launcher in the manager, to avoid elevation timeouts during loading.")
                    : new EndfieldLaunchException("启动游戏需要管理员权限，请在 UAC 提示中允许。", "Launching the game requires administrator rights. Allow the UAC prompt.");
            }

            var integration = new XxmiIntegration(executablePath, store);
            var launcher = new EndfieldLauncher(store, Log);
            if (args.SequenceEqual(new[] { LaunchVerb }))
            {
                store.WriteStatus(session, LaunchStates.Starting, new LaunchText("正在启动…", "Starting…"));
                if (!profile.WithMods) return RunGame(store, launcher, profile, session);
                int code = launcher.LaunchThroughXxmi(profile, integration, pid => store.WriteStatus(session, LaunchStates.LoaderStarted,
                    new LaunchText("已请求 XXMI / EFMI 启动游戏。", "XXMI / EFMI was asked to start the game."), pid));
                // The game-only callback reports its own result; only close out a request it never reached.
                if (store.ReadStatus() is { State: LaunchStates.LoaderStarted } status && status.SessionId == session)
                {
                    store.WriteStatus(session, LaunchStates.Exited, new LaunchText(
                        $"XXMI 已结束（退出码 {code}），没有收到游戏启动回调。请检查 XXMI 是否选择了“保持”。",
                        $"XXMI exited (code {code}) without starting the game through the manager. Check that Keep was chosen in XXMI."));
                }

                return code;
            }

            if (fromXxmi)
            {
                Log(integration.Import(profile, strict: true));
                integration.ValidateMode(profile);
                return RunGame(store, launcher, profile, session);
            }

            throw new EndfieldLaunchException("不支持的命令行参数。", "Unsupported command-line arguments.");
        }
        catch (Exception ex)
        {
            LaunchText message = ex is EndfieldLaunchException known
                ? known.Text
                : new LaunchText("启动未完成：" + ex.Message, "Launch incomplete: " + ex.Message);
            store.Log(message, english);
            if (ex is not EndfieldLaunchException) store.Log(LaunchText.Both(ex.ToString()), english);
            store.WriteStatus(session, LaunchStates.Failed, message);
            // XXMI starts this process long after the manager stopped waiting; tell the user directly.
            if (fromXxmi) ShowError(message.For(english), english);
            return 1;
        }
    }

    private static int RunGame(LaunchStore store, EndfieldLauncher launcher, LaunchProfile profile, string session)
    {
        using var mutex = new Mutex(false, GameSessionMutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned) throw new EndfieldLaunchException("启动会话已在运行。", "A launch session is already running.");
        try
        {
            int code = launcher.LaunchGame(profile, pid => store.WriteStatus(session, LaunchStates.GameStarted,
                new LaunchText("游戏进程已创建。这不代表游戏认证、Mod 加载成功或账号安全。",
                    "The game process was created. This does not confirm authentication, Mod loading or account safety."), pid));
            store.WriteStatus(session, LaunchStates.Exited, new LaunchText("游戏已退出，退出码 " + code, "The game exited with code " + code));
            return code;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void ShowError(string message, bool english)
    {
        try
        {
            _ = MessageBoxW(IntPtr.Zero, message, english ? "Integrated Mod Manager · Endfield launch" : "集成化 Mod 管理器 · 终末地启动", 0x10 | 0x40000);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
