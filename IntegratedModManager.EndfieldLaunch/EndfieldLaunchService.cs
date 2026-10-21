// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Modified 2026-10-21: configuration operations formerly behind the bridge window, exposed
// to the manager's settings page; see NOTICE.md.
using System.Text.Json;

namespace IntegratedModManager.EndfieldLaunch;

/// <summary>The operations the manager's settings page offers. None of them starts the game.</summary>
public sealed class EndfieldLaunchService
{
    public EndfieldLaunchService(string dataDirectory, string launchExecutable, Func<string, bool>? isProcessRunning = null)
    {
        Store = new LaunchStore(dataDirectory);
        Integration = new XxmiIntegration(launchExecutable, Store, isProcessRunning);
    }

    public LaunchStore Store { get; }

    public XxmiIntegration Integration { get; }

    /// <summary>Saves settings for XXMI callbacks even before the first manager launch.</summary>
    public void SaveSettings(LaunchProfile profile)
    {
        if (LaunchCommand.IsLaunchRunning())
        {
            throw new EndfieldLaunchException("启动会话正在运行，请结束后再保存设置。", "A launch session is running. Wait for it to end before saving settings.");
        }

        EndfieldGame.ValidateProfile(profile);
        if (!EndfieldGame.IsValidGame(profile.Game))
        {
            throw new EndfieldLaunchException("游戏路径必须包含 Endfield.exe 和同目录 Endfield_Data。", "The game path must contain Endfield.exe alongside Endfield_Data.");
        }

        LaunchProfile saved = profile.Clone();
        saved.SessionId = string.Empty;
        Store.SaveProfile(saved);
    }

    /// <summary>Adapts EFMI and persists the callback profile. Returns null if already configured.</summary>
    public string? ConfigureXxmi(LaunchProfile profile)
    {
        Integration.EnsureClosed();
        if (LaunchCommand.IsLaunchRunning())
        {
            throw new EndfieldLaunchException("启动会话正在运行，请结束后再适配。", "A launch session is running. Wait for it to end before setup.");
        }

        bool configured = Integration.IsConfigured(profile);
        if (configured) Integration.ValidateMode(profile);
        string backup = Integration.Apply(profile, allowConfigured: true);
        LaunchProfile saved = profile.Clone();
        saved.WithMods = true;
        SaveSettings(saved);
        return configured ? null : backup;
    }

    /// <summary>Checks the game path, the official launcher signature and, with Mods, the EFMI setup.</summary>
    public LaunchText Check(LaunchProfile profile)
    {
        EndfieldGame.ValidateProfile(profile);
        if (!EndfieldGame.IsValidGame(profile.Game))
        {
            throw new EndfieldLaunchException("游戏路径必须包含 Endfield.exe 和同目录 Endfield_Data。", "The game path must contain Endfield.exe alongside Endfield_Data.");
        }

        LaunchText report = new("游戏程序有效。\n", "Game executable is valid.\n");
        var failures = new List<LaunchText>();
        string? verified = null;
        foreach (string candidate in EndfieldGame.ParentCandidates(profile, Store.ReadCachedParent(profile.Game)))
        {
            try
            {
                string signer = OfficialLauncher.Verify(candidate);
                verified = candidate;
                report += new LaunchText($"官方启动器签名通过（{signer}）：{candidate}\n", $"Official launcher signature verified ({signer}): {candidate}\n");
                break;
            }
            catch (OfficialSignatureException ex)
            {
                failures.Add(ex.Text);
            }
        }

        if (verified is null)
        {
            LaunchText message = new("未找到签名有效的官方 Games.exe。", "No official Games.exe with a valid signature was found. ");
            foreach (LaunchText failure in failures) message += new LaunchText("\n", "\n") + failure;
            throw new EndfieldLaunchException(message);
        }

        if (string.IsNullOrWhiteSpace(profile.Parent)) Store.WriteCachedParent(profile.Game, verified);
        if (!profile.WithMods) return report + new LaunchText("未启用 Mod：将直接以官方父进程启动游戏。", "Mods disabled: the game starts directly under the official parent.");

        Integration.ValidateMode(profile);
        return report + new LaunchText("EFMI 适配完整。\n", "EFMI setup is complete.\n") + Integration.AuthorizationStatus(profile);
    }

    /// <summary>
    /// Settings saved by a standalone EndfieldBridge 0.3.x installed under
    /// <paramref name="legacyInstallDirectory"/>, or <see langword="null"/> when none exist.
    /// </summary>
    public static LaunchProfile? ReadLegacyProfile(string legacyInstallDirectory)
    {
        foreach (string path in LegacyFiles(legacyInstallDirectory, "profile.json"))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = document.RootElement;
                string Read(string name) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : string.Empty;
                return new LaunchProfile
                {
                    Game = Read("Game"),
                    Parent = Read("Parent"),
                    Xxmi = Read("Xxmi"),
                    GameArguments = root.TryGetProperty("GameArguments", out JsonElement arguments) && arguments.ValueKind == JsonValueKind.String
                        ? arguments.GetString() ?? string.Empty
                        : "-force-d3d11",
                    WithMods = root.TryGetProperty("WithMods", out JsonElement mods) && mods.ValueKind == JsonValueKind.True
                };
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    /// <summary>Adopts a standalone bridge's EFMI restore point so its original fields stay restorable.</summary>
    public bool AdoptLegacyRestoreState(string legacyInstallDirectory) =>
        LegacyFiles(legacyInstallDirectory, "integration-backup.json").Any(Integration.AdoptLegacyRestoreState);

    private static IEnumerable<string> LegacyFiles(string legacyInstallDirectory, string name)
    {
        // 0.3.2 keeps state in data\; 0.3.0/0.3.1 kept it beside the executable.
        foreach (string path in new[] { Path.Combine(legacyInstallDirectory, "data", name), Path.Combine(legacyInstallDirectory, name) })
        {
            if (File.Exists(path)) yield return path;
        }
    }
}
