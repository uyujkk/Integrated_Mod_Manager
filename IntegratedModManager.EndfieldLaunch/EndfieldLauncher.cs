// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Modified 2026-10-21: integrated into Integrated Mod Manager; candidates are verified once
// each during creation and the last verified launcher is cached; see NOTICE.md.
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace IntegratedModManager.EndfieldLaunch;

public static class EndfieldGame
{
    public static bool IsValidGame(string? executable) =>
        !string.IsNullOrWhiteSpace(executable)
        && File.Exists(executable)
        && Path.GetFileName(executable).Equals("Endfield.exe", StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "Endfield_Data"));

    public static void ValidateProfile(LaunchProfile profile)
    {
        if (!File.Exists(profile.Game) || !Path.GetFileName(profile.Game).Equals("Endfield.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new EndfieldLaunchException("请选择存在的 Endfield.exe。", "Select an existing Endfield.exe.");
        }

        if (profile.GameArguments.Contains('\r') || profile.GameArguments.Contains('\n') || profile.GameArguments.Contains('\0'))
        {
            throw new EndfieldLaunchException("启动参数不能包含换行或空字符。", "Launch arguments cannot contain newlines or null characters.");
        }
    }

    public static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Official launcher candidates in the order to try. An explicit choice is used alone;
    /// otherwise the last verified launcher for this game comes first, then discovered ones.
    /// Discovery does not verify signatures: <see cref="LaunchSession.Create(string,string,string,Action{LaunchText})"/>
    /// verifies the one actually used while holding it open.
    /// </summary>
    public static IEnumerable<string> ParentCandidates(LaunchProfile profile, string? cachedParent, Func<string, IEnumerable<string>>? discover = null)
    {
        if (!string.IsNullOrWhiteSpace(profile.Parent))
        {
            yield return profile.Parent;
            yield break;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? Candidate(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (File.Exists(full) && seen.Add(full)) return full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }

            return null;
        }

        if (!string.IsNullOrWhiteSpace(cachedParent) && Candidate(cachedParent) is { } cached) yield return cached;
        // Discovery runs only if the caller rejects the cached candidate and asks for another.
        foreach (string candidate in (discover ?? DiscoverParents)(profile.Game))
        {
            if (Candidate(candidate) is { } found) yield return found;
        }
    }

    /// <summary>Games.exe in versioned subfolders of the launcher root, newest first.</summary>
    public static IEnumerable<string> DiscoverParents(string game)
    {
        var roots = new List<string>();
        try
        {
            string? root = Directory.GetParent(Path.GetDirectoryName(game)!)?.Parent?.FullName;
            if (root is not null) roots.Add(root);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }

        roots.AddRange(RegisteredLauncherRoots());
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string candidate in VersionedGamesExecutables(root)) yield return candidate;
        }
    }

    internal static IEnumerable<string> VersionedGamesExecutables(string root)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return Directory.GetDirectories(root)
                .Select(directory => new
                {
                    Path = System.IO.Path.Combine(directory, "Games.exe"),
                    Version = Version.TryParse(System.IO.Path.GetFileName(directory), out Version? version) ? version : new Version(0, 0)
                })
                .OrderByDescending(item => item.Version)
                .Where(item => File.Exists(item.Path))
                .Select(item => item.Path)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> RegisteredLauncherRoots()
    {
        var roots = new List<string>();
        try
        {
            using RegistryKey classes = Registry.ClassesRoot;
            foreach (string name in classes.GetSubKeyNames().Where(name => name.StartsWith("hypergryphlauncher", StringComparison.OrdinalIgnoreCase)))
            {
                using RegistryKey? key = classes.OpenSubKey(name + @"\shell\open\command");
                string command = key?.GetValue(null)?.ToString() ?? string.Empty;
                if (!command.StartsWith('"')) continue;
                int end = command.IndexOf('"', 1);
                if (end <= 1) continue;
                string? root = Path.GetDirectoryName(command[1..end]);
                if (root is not null) roots.Add(root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
        }

        return roots;
    }
}

/// <summary>Runs one launch: directly under the official parent, or through XXMI / EFMI.</summary>
public sealed class EndfieldLauncher
{
    private readonly LaunchStore _store;
    private readonly Action<LaunchText> _log;
    private readonly Func<string, bool> _isProcessRunning;

    public EndfieldLauncher(LaunchStore store, Action<LaunchText> log, Func<string, bool>? isProcessRunning = null)
    {
        _store = store;
        _log = log;
        _isProcessRunning = isProcessRunning ?? XxmiIntegration.IsProcessRunning;
    }

    /// <summary>Creates the game under a verified official parent and waits for it to exit.</summary>
    public int LaunchGame(LaunchProfile profile, Action<int>? started = null) =>
        LaunchGame(profile, started, (parent, game, arguments) => LaunchSession.Create(parent, game, arguments, _log), null);

    internal int LaunchGame(LaunchProfile profile, Action<int>? started,
        Func<string, string, string, LaunchSession> createSession, Func<string, IEnumerable<string>>? discover)
    {
        Directory.CreateDirectory(_store.DataDirectory);
        using FileStream sessionLock = File.Open(_store.SessionLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        EndfieldGame.ValidateProfile(profile);
        if (_isProcessRunning("Endfield"))
        {
            throw new EndfieldLaunchException("检测到终末地已在运行，请先退出，不会结束现有进程。", "Endfield is already running. Exit it first; it will not be terminated.");
        }

        var failures = new List<LaunchText>();
        foreach (string parent in EndfieldGame.ParentCandidates(profile, _store.ReadCachedParent(profile.Game), discover))
        {
            LaunchSession session;
            try
            {
                session = createSession(parent, profile.Game, profile.GameArguments);
            }
            catch (OfficialSignatureException ex)
            {
                // A stale or tampered candidate: try the next one instead of failing the launch.
                failures.Add(new LaunchText(Path.GetFileName(Path.GetDirectoryName(parent)) + "：", Path.GetFileName(Path.GetDirectoryName(parent)) + ": ") + ex.Text);
                continue;
            }

            using (session)
            {
                if (string.IsNullOrWhiteSpace(profile.Parent)) _store.WriteCachedParent(profile.Game, parent);
                started?.Invoke((int)session.TargetId);
                int code = session.Wait();
                _log(new LaunchText("游戏进程已退出，退出码 " + code, "Game process exited with code " + code));
                return code;
            }
        }

        LaunchText message = new("未找到签名有效的官方 Games.exe。", "No official Games.exe with a valid signature was found. ");
        if (failures.Count == 0) message += new LaunchText("请手动选择官方文件。", "Select the official file manually.");
        foreach (LaunchText failure in failures) message += new LaunchText("\n", "\n") + failure;
        throw new EndfieldLaunchException(message);
    }

    /// <summary>Asks XXMI to start EFMI; XXMI then calls back with <see cref="XxmiIntegration.GameOnlyArguments"/>.</summary>
    public int LaunchThroughXxmi(LaunchProfile profile, XxmiIntegration integration, Action<int>? started = null)
    {
        if (!File.Exists(profile.Xxmi)) throw new EndfieldLaunchException("XXMI 程序不存在。", "XXMI executable was not found.");
        if (_isProcessRunning("Endfield")) throw new EndfieldLaunchException("游戏已经运行，请先退出。", "The game is already running. Exit it first.");
        integration.ValidateMode(profile);
        _log(integration.Import(profile, strict: true));
        if (_isProcessRunning("XXMI Launcher"))
        {
            throw new EndfieldLaunchException("请退出已运行的 XXMI 后再启动，避免请求被现有窗口接管。", "Exit the running XXMI instance before launching, so the request is not taken over by it.");
        }

        _log(new LaunchText("调用 XXMI --nogui --xxmi EFMI。首次弹窗核对命令指向管理器后选“保持”；“重置”会清空命令。",
            "Calling XXMI --nogui --xxmi EFMI. Verify the command points to the manager and choose Keep if prompted; Reset clears it."));
        using Process process = Process.Start(new ProcessStartInfo(profile.Xxmi, "--nogui --xxmi EFMI")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(XxmiIntegration.ConfigPath(profile.Xxmi))!
        }) ?? throw new EndfieldLaunchException("无法创建 XXMI 进程。", "Unable to create the XXMI process.");
        started?.Invoke(process.Id);
        process.WaitForExit();
        _log(new LaunchText($"XXMI 已结束，退出码 {process.ExitCode}。这不能证明 Mod 成功或账号安全。",
            $"XXMI exited with code {process.ExitCode}. This does not confirm Mod loading or account safety."));
        return process.ExitCode;
    }
}
