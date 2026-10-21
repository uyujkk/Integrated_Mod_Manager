// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Modified 2026-10-21: integrated into Integrated Mod Manager; commands written by
// EndfieldBridge 0.3.x are recognised to keep the original restore point; see NOTICE.md.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IntegratedModManager.EndfieldLaunch;

/// <summary>
/// Reads and adapts the EFMI importer in XXMI Launcher Config.json so XXMI's Custom
/// Launch calls back into the manager. Only seven launch fields change; a field-level
/// restore point is kept and every write is refused if another program changed the file.
/// </summary>
public sealed class XxmiIntegration
{
    public const string GameOnlyArguments = "--endfield-game-only --from-xxmi";
    private static readonly string[] Fields =
        ["game_launch", "start_method", "game_folder", "game_process_exe_enabled", "game_process_exe", "custom_launch", "custom_launch_signature"];
    private static readonly Regex LegacyBridgeCommand = new(
        @"^""[^""]*\\EndfieldBridge\.exe"" --game-only(?: --from-xxmi)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal const int KeptSetupBackups = 5;

    private readonly LaunchStore _store;
    private readonly Func<string, bool> _isProcessRunning;

    public XxmiIntegration(string launchExecutable, LaunchStore store, Func<string, bool>? isProcessRunning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchExecutable);
        LaunchExecutable = Path.GetFullPath(launchExecutable);
        _store = store;
        _isProcessRunning = isProcessRunning ?? IsProcessRunning;
    }

    public string LaunchExecutable { get; }

    /// <summary>The Custom Launch command XXMI runs. It must stay byte-identical for XXMI's signature.</summary>
    public string Command => CommandLine.Quote(LaunchExecutable) + " " + GameOnlyArguments;

    public static bool IsLegacyBridgeCommand(string? command) => command is not null && LegacyBridgeCommand.IsMatch(command);

    private bool IsOwnedCommand(string? command) => command == Command || IsLegacyBridgeCommand(command);

    public static string ConfigPath(string xxmiExecutable)
    {
        if (string.IsNullOrWhiteSpace(xxmiExecutable))
        {
            throw new EndfieldLaunchException("请选择实际的 XXMI Launcher.exe。", "Select the actual XXMI Launcher.exe.");
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(xxmiExecutable))!);
        for (int depth = 0; depth < 4 && directory is not null; depth++, directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "XXMI Launcher Config.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new EndfieldLaunchException("没有找到 XXMI Launcher Config.json，请选择实际 XXMI Launcher.exe。",
            "XXMI Launcher Config.json was not found. Select the actual XXMI Launcher.exe.");
    }

    public static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new EndfieldLaunchException("XXMI 配置不是 JSON 对象。", "The XXMI configuration is not a JSON object.");

    public static JsonObject Efmi(JsonObject document) => document["Importers"]?["EFMI"]?["Importer"] as JsonObject
        ?? throw new EndfieldLaunchException("XXMI 配置缺少 EFMI，未执行写入。", "EFMI is missing from the XXMI configuration. Nothing was written.");

    private static string? FolderExecutable(JsonObject efmi)
    {
        string raw = efmi["game_folder"]?.GetValue<string>() ?? string.Empty;
        try
        {
            string executable = Path.Combine(raw, "Endfield.exe");
            return EndfieldGame.IsValidGame(executable) ? Path.GetFullPath(executable) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Verification only: never loads XXMI's private key or creates its trust signatures.</summary>
    public bool IsCommandAuthorized(LaunchProfile profile)
    {
        try
        {
            string config = ConfigPath(profile.Xxmi);
            JsonObject efmi = Efmi(Read(config));
            string command = efmi["custom_launch"]?.GetValue<string>() ?? string.Empty;
            string signature = efmi["custom_launch_signature"]?.GetValue<string>() ?? string.Empty;
            if (command != Command || string.IsNullOrWhiteSpace(signature)) return false;
            string publicKey = Path.Combine(Path.GetDirectoryName(config)!, "Resources", "Security", "public_key.der");
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(File.ReadAllText(publicKey).Trim()), out _);
            return key.VerifyData(Encoding.UTF8.GetBytes(command), Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException
            or InvalidOperationException or EndfieldLaunchException or JsonException)
        {
            return false;
        }
    }

    public LaunchText AuthorizationStatus(LaunchProfile profile) => IsCommandAuthorized(profile)
        ? new LaunchText("XXMI 命令授权：签名验证通过（仅使用公钥核验）。", "XXMI command authorization: signature verified using the public key.")
        : new LaunchText("XXMI 命令授权：尚未通过。首次启动核对命令指向管理器后，在 XXMI 弹窗选择“保持”；“重置”会清空命令。",
            "XXMI command authorization: pending. Verify the command points to the manager, then choose Keep in XXMI. Reset clears the command.");

    public LaunchText Inspect(LaunchProfile profile)
    {
        string path = ConfigPath(profile.Xxmi);
        JsonObject efmi = Efmi(Read(path));
        string folder = efmi["game_folder"]?.ToString() ?? string.Empty;
        string mode = $"{efmi["game_launch"]} / {efmi["start_method"]}";
        string process = $"{efmi["game_process_exe"]} / {efmi["xxmi_dll_inject_mode"]}";
        string current = efmi["custom_launch"]?.ToString() ?? string.Empty;
        LaunchText text = new(
            $"XXMI 配置：{path}\n游戏目录：{folder}\n启动模式：{mode}\n识别进程 / 注入模式：{process}\n当前命令：{current}\n目标命令：{Command}\n",
            $"XXMI configuration: {path}\nGame folder: {folder}\nLaunch mode: {mode}\nGame process / injection: {process}\nCurrent command: {current}\nExpected command: {Command}\n");
        text += AuthorizationStatus(profile);
        if (FolderExecutable(efmi) is null)
        {
            text += new LaunchText("\n注意：EFMI 游戏目录无效，不能填写快捷方式或管理器程序。", "\nWarning: invalid EFMI game folder. Do not enter a shortcut or the manager executable.");
        }

        if (IsLegacyBridgeCommand(efmi["custom_launch"]?.ToString()))
        {
            text += new LaunchText("\n检测到旧版 EndfieldBridge 的命令：请点击“备份并适配 EFMI”改为内置启动，原始恢复点会保留。",
                "\nA command from the old EndfieldBridge was found. Use Back up and set up EFMI to switch to the integrated launch; the original restore point is kept.");
        }

        return text;
    }

    /// <summary>Reads the game folder and launch arguments XXMI would use into <paramref name="profile"/>.</summary>
    public LaunchText Import(LaunchProfile profile, bool strict = false)
    {
        JsonObject efmi = Efmi(Read(ConfigPath(profile.Xxmi)));
        string? executable = FolderExecutable(efmi);
        if (executable is null && (strict || !EndfieldGame.IsValidGame(profile.Game)))
        {
            throw new EndfieldLaunchException("EFMI 的游戏目录无效。这里必须是包含 Endfield.exe 和 Endfield_Data 的实际目录，不能填写 .lnk 快捷方式。",
                "Invalid EFMI game folder. Select the folder containing Endfield.exe and Endfield_Data, not a shortcut.");
        }

        if (executable is not null) profile.Game = executable;
        string arguments = efmi["d3d11_mode_cmd_args"]?.GetValue<string>() ?? string.Empty;
        if (efmi["use_launch_options"]?.GetValue<bool>() == true)
        {
            arguments += " " + (efmi["launch_options"]?.GetValue<string>() ?? string.Empty);
        }

        profile.GameArguments = arguments.Trim();
        EndfieldGame.ValidateProfile(profile);
        return executable is null
            ? new LaunchText("EFMI 游戏目录无效：已保留当前有效的游戏路径，仅读取启动参数。适配时会修正 EFMI 目录。",
                "The EFMI game folder is invalid. The valid game path was kept; only arguments were imported. Setup will repair the folder.")
            : new LaunchText("已读取 EFMI 游戏目录和启动参数。", "EFMI game path and launch arguments imported.");
    }

    public void ValidateMode(LaunchProfile profile)
    {
        JsonObject efmi = Efmi(Read(ConfigPath(profile.Xxmi)));
        string injection = efmi["xxmi_dll_inject_mode"]?.ToString() ?? string.Empty;
        if (injection is not ("DIRECT" or "HOOK"))
        {
            throw new EndfieldLaunchException("EFMI 注入模式必须为 DIRECT 或 HOOK，当前未启用受支持的 Mod 加载方式。", "EFMI must use DIRECT or HOOK injection mode.");
        }

        if (!IsConfigured(profile))
        {
            throw new EndfieldLaunchException("EFMI 配置不完整，请使用“备份并适配 EFMI”修复目录、进程名和启动命令。",
                "EFMI setup is incomplete. Use Back up and set up EFMI to repair its folder, process name and launch command.");
        }

        if (HasBlockingPreLaunch(efmi))
        {
            throw new EndfieldLaunchException("请移除前置命令中的启动调用：应放在自定义启动命令中，前置等待会阻塞加载流程。",
                "Remove the launch call from the pre-launch command. Use Custom Launch; a waiting pre-launch command blocks the loader.");
        }
    }

    public bool IsConfigured(LaunchProfile profile)
    {
        try
        {
            JsonObject efmi = Efmi(Read(ConfigPath(profile.Xxmi)));
            string? executable = FolderExecutable(efmi);
            return efmi["game_launch"]?.ToString() == "CUSTOM"
                && efmi["start_method"]?.ToString() == "NATIVE"
                && efmi["custom_launch"]?.ToString() == Command
                && efmi["game_process_exe_enabled"]?.GetValue<bool>() == true
                && efmi["game_process_exe"]?.ToString() == "Endfield.exe"
                && executable is not null
                && Path.GetFullPath(profile.Game).Equals(executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or ArgumentException or EndfieldLaunchException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Backs up XXMI's config, records the first restore point, then points EFMI at the manager.</summary>
    /// <returns>The full-file backup path.</returns>
    public string Apply(LaunchProfile profile, string? restoreStatePath = null, bool allowConfigured = false)
    {
        string path = ConfigPath(profile.Xxmi);
        string stateFile = restoreStatePath ?? _store.RestoreStatePath;
        EnsureClosed();
        EndfieldGame.ValidateProfile(profile);
        if (!EndfieldGame.IsValidGame(profile.Game))
        {
            throw new EndfieldLaunchException("游戏路径必须包含 Endfield.exe 和同目录 Endfield_Data。", "The game path must contain Endfield.exe alongside Endfield_Data.");
        }

        if (File.Exists(stateFile) && Read(stateFile)["config"]?.ToString() is var stateConfig && !SamePath(stateConfig, path))
        {
            if (!string.IsNullOrWhiteSpace(stateConfig) && File.Exists(stateConfig))
            {
                throw new EndfieldLaunchException($"已有另一套 XXMI 的恢复点（{stateConfig}），请先恢复原配置，再适配新的 XXMI。",
                    $"A restore point exists for another XXMI installation ({stateConfig}). Restore it before setting up a different installation.");
            }

            // The XXMI installation that restore point belongs to is gone, so it can never be
            // restored. Retire it (kept on disk, never deleted) instead of blocking every setup.
            Retire(stateFile, "stale");
        }

        if (!allowConfigured && IsConfigured(profile)) throw new EndfieldLaunchException("已适配，不覆盖首次备份。", "Setup is already complete. The original backup was kept.");
        string original = File.ReadAllText(path);
        JsonObject document = JsonNode.Parse(original)!.AsObject();
        JsonObject efmi = Efmi(document);
        string mode = efmi["xxmi_dll_inject_mode"]?.ToString() ?? string.Empty;
        if (mode is not ("DIRECT" or "HOOK"))
        {
            throw new EndfieldLaunchException("当前 EFMI 注入模式不是 DIRECT 或 HOOK。", "EFMI injection mode is neither DIRECT nor HOOK.");
        }

        if (HasBlockingPreLaunch(efmi))
        {
            throw new EndfieldLaunchException("前置命令包含启动调用，请先移除，以免阻塞 Mod 加载。", "Remove the launch call from the pre-launch command to avoid blocking Mod loading.");
        }

        string backup = path + ".imm-endfield-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
        File.WriteAllText(backup, original, new UTF8Encoding(false));

        // Re-adapting over our own (or the old bridge's) command keeps the first restore point,
        // which still holds the user's fields from before any bridge was involved.
        bool preserve = false;
        if (File.Exists(stateFile) && IsOwnedCommand(efmi["custom_launch"]?.ToString()))
        {
            preserve = SamePath(Read(stateFile)["config"]?.ToString(), path);
        }

        JsonObject state;
        if (preserve) state = Read(stateFile);
        else
        {
            // The command is no longer ours, so the old restore point describes values the user
            // has since replaced. Keep it on disk rather than silently overwriting it.
            if (File.Exists(stateFile)) Retire(stateFile, "superseded");

            var fields = new JsonObject();
            foreach (string key in Fields)
            {
                fields[key] = new JsonObject { ["exists"] = efmi.ContainsKey(key), ["value"] = efmi[key]?.DeepClone() };
            }

            state = new JsonObject { ["schema"] = 3, ["config"] = path, ["backup"] = backup, ["fields"] = fields };
        }

        string previousCommand = efmi["custom_launch"]?.ToString() ?? string.Empty;
        efmi["game_launch"] = "CUSTOM";
        efmi["start_method"] = "NATIVE";
        efmi["game_folder"] = Path.GetDirectoryName(Path.GetFullPath(profile.Game));
        efmi["game_process_exe_enabled"] = true;
        efmi["game_process_exe"] = "Endfield.exe";
        efmi["custom_launch"] = Command;
        if (!string.Equals(previousCommand, Command, StringComparison.Ordinal)) efmi["custom_launch_signature"] = string.Empty;
        if (File.ReadAllText(path) != original)
        {
            throw new EndfieldLaunchException("XXMI 配置已被其他程序修改，已停止写入。", "Another program changed the XXMI configuration. No write was performed.");
        }

        // Keep the original values, but refresh the expected installed values after repair/migration.
        // XXMI adds its own authorization signature later; that change must not block restore.
        var applied = new JsonObject();
        foreach (string key in Fields.Where(key => key != "custom_launch_signature"))
            applied[key] = new JsonObject { ["exists"] = efmi.ContainsKey(key), ["value"] = efmi[key]?.DeepClone() };
        state["applied"] = applied;
        state["schema"] = 3;
        LaunchStore.WriteAtomic(stateFile, state.ToJsonString(JsonOptions));
        LaunchStore.WriteAtomic(path, document.ToJsonString(JsonOptions));
        PruneSetupBackups(path, state["backup"]?.ToString());
        return backup;
    }

    /// <summary>Returns the seven EFMI launch fields to their values before the first setup.</summary>
    public void Restore(string? restoreStatePath = null)
    {
        string stateFile = restoreStatePath ?? _store.RestoreStatePath;
        if (!File.Exists(stateFile))
        {
            throw new EndfieldLaunchException("没有找到 EFMI 恢复点：尚未通过管理器适配过 EFMI。", "No EFMI restore point was found. EFMI has not been set up through the manager.");
        }

        JsonObject state = Read(stateFile);
        string path = state["config"]!.GetValue<string>();
        if (!File.Exists(path))
        {
            throw new EndfieldLaunchException($"恢复点对应的 XXMI 配置已不存在：{path}\n无法恢复。适配新的 XXMI 时，这个失效的恢复点会自动存档作废。",
                $"The XXMI configuration of this restore point no longer exists: {path}\nIt cannot be restored. Setting up another XXMI retires it automatically.");
        }

        EnsureClosed();
        string original = File.ReadAllText(path);
        JsonObject document = JsonNode.Parse(original)!.AsObject();
        JsonObject efmi = Efmi(document);
        if (!IsOwnedCommand(efmi["custom_launch"]?.ToString()))
        {
            throw new EndfieldLaunchException("自定义命令已改变，停止恢复以保留你的新设置。", "The custom command changed. Restore was stopped to preserve your new settings.");
        }

        if (state["applied"] is JsonObject applied)
        {
            foreach (string key in Fields.Where(key => key != "custom_launch_signature"))
            {
                if (applied[key] is not JsonObject expected
                    || expected["exists"]?.GetValue<bool>() != efmi.ContainsKey(key)
                    || !SameFieldValue(key, expected["value"], efmi[key]))
                {
                    throw new EndfieldLaunchException($"EFMI 字段 {key} 已改变，停止恢复以保留你的新设置。", $"EFMI field {key} changed. Restore was stopped to preserve your new settings.");
                }
            }
        }
        else
        {
            // Older restore points did not record what was installed. Require re-adaptation
            // to establish those values while preserving the original pre-bridge fields.
            throw new EndfieldLaunchException("旧恢复点缺少适配后字段记录，请先重新适配 EFMI，再执行恢复。原始恢复点会保留。",
                "The old restore point has no installed-field record. Set up EFMI again before restoring; the original restore point is kept.");
        }

        File.Copy(path, path + ".before-restore-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".bak");
        if (state["fields"] is JsonObject fields)
        {
            foreach (KeyValuePair<string, JsonNode?> field in fields)
            {
                JsonObject saved = field.Value!.AsObject();
                if (saved["exists"]!.GetValue<bool>()) efmi[field.Key] = saved["value"]?.DeepClone();
                else efmi.Remove(field.Key);
            }
        }
        else
        {
            foreach (string key in new[] { "game_launch", "custom_launch", "custom_launch_signature" }) efmi[key] = state[key]?.DeepClone();
        }

        if (File.ReadAllText(path) != original)
        {
            throw new EndfieldLaunchException("配置已改变，停止恢复。", "The configuration changed. Restore was stopped.");
        }

        LaunchStore.WriteAtomic(path, document.ToJsonString(JsonOptions));
        File.Move(stateFile, stateFile + ".restored-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
    }

    /// <summary>
    /// Adopts the restore point written by a standalone EndfieldBridge so its original
    /// pre-bridge fields stay restorable after switching to the integrated command.
    /// </summary>
    public bool AdoptLegacyRestoreState(string legacyStatePath)
    {
        if (File.Exists(_store.RestoreStatePath) || !File.Exists(legacyStatePath)) return false;
        JsonObject legacy = Read(legacyStatePath);
        if (legacy["config"]?.GetValue<string>() is not { Length: > 0 }) return false;
        LaunchStore.WriteAtomic(_store.RestoreStatePath, legacy.ToJsonString(JsonOptions));
        return true;
    }

    /// <summary>
    /// XXMI may rewrite a folder when it saves (separators, case, a trailing slash), so
    /// game_folder compares as a path; every other field must match exactly.
    /// </summary>
    internal static bool SameFieldValue(string key, JsonNode? expected, JsonNode? actual)
    {
        if (JsonNode.DeepEquals(expected, actual)) return true;
        if (key != "game_folder" || expected is not JsonValue || actual is not JsonValue) return false;
        return expected.GetValueKind() == JsonValueKind.String && actual.GetValueKind() == JsonValueKind.String
            && SamePath(expected.GetValue<string>(), actual.GetValue<string>());
    }

    internal static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left.Trim())),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right.Trim())), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void Retire(string stateFile, string reason) =>
        File.Move(stateFile, stateFile + "." + reason + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));

    /// <summary>Keeps the first setup backup and the newest few; older re-adapt backups are removed.</summary>
    private static void PruneSetupBackups(string configPath, string? firstBackup)
    {
        try
        {
            string directory = Path.GetDirectoryName(configPath)!;
            string prefix = Path.GetFileName(configPath) + ".imm-endfield-";
            foreach (string old in Directory.GetFiles(directory, prefix + "*.bak")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(KeptSetupBackups)
                .Where(file => !SamePath(file, firstBackup)))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruning is housekeeping; a locked backup must not fail the setup.
        }
    }

    public void EnsureClosed()
    {
        if (_isProcessRunning("XXMI Launcher"))
        {
            throw new EndfieldLaunchException("请先退出 XXMI，避免其覆盖配置。不会结束现有 XXMI 进程。", "Exit XXMI before editing its configuration. Existing processes will not be terminated.");
        }

        if (_isProcessRunning("Endfield"))
        {
            throw new EndfieldLaunchException("请先退出终末地再修改适配配置。", "Exit Endfield before changing loader setup.");
        }
    }

    private static bool HasBlockingPreLaunch(JsonObject efmi)
    {
        string command = efmi["run_pre_launch"]?.ToString() ?? string.Empty;
        return efmi["run_pre_launch_enabled"]?.GetValue<bool>() == true
            && (command.Contains("EndfieldBridge", StringComparison.OrdinalIgnoreCase)
                || command.Contains("--endfield-game-only", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsProcessRunning(string name)
    {
        Process[] processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }
}
