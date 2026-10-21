// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.
// Modified 2026-10-21: integrated into Integrated Mod Manager; see NOTICE.md.
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntegratedModManager.EndfieldLaunch;

/// <summary>The settings one launch uses. The manager writes it before starting a launch.</summary>
public sealed class LaunchProfile
{
    public string Game { get; set; } = string.Empty;

    /// <summary>Optional explicit official Games.exe. Empty means discover it.</summary>
    public string Parent { get; set; } = string.Empty;

    public string Xxmi { get; set; } = string.Empty;

    public string GameArguments { get; set; } = "-force-d3d11";

    public string Language { get; set; } = "zh-CN";

    public bool WithMods { get; set; }

    /// <summary>Correlates status updates with the manager request that started the launch.</summary>
    public string SessionId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool English => Language == "en-US";

    public LaunchProfile Clone() => (LaunchProfile)MemberwiseClone();
}

public static class LaunchStates
{
    public const string Starting = "starting";
    public const string GameStarted = "game-started";
    public const string LoaderStarted = "loader-started";
    public const string Exited = "exited";
    public const string Failed = "failed";

    public static bool IsTerminal(string state) => state is GameStarted or Exited or Failed;
}

public sealed record LaunchStatus(string SessionId, string State, string MessageZh, string MessageEn, int ProcessId, DateTimeOffset At)
{
    public LaunchText Message => new(MessageZh, MessageEn);
}

/// <summary>
/// Profile, restore metadata, verified-launcher cache, status and log in one folder.
/// Every write is atomic so a crash or a concurrent reader never sees half a file.
/// </summary>
public sealed class LaunchStore
{
    private const long MaximumLogBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object LogGate = new();

    public LaunchStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    public string ProfilePath => Path.Combine(DataDirectory, "profile.json");

    public string RestoreStatePath => Path.Combine(DataDirectory, "integration-backup.json");

    public string StatusPath => Path.Combine(DataDirectory, "status.json");

    public string LogPath => Path.Combine(DataDirectory, "launch.log");

    public string SessionLockPath => Path.Combine(DataDirectory, "game-session.lock");

    private string ParentCachePath => Path.Combine(DataDirectory, "parent-cache.json");

    public LaunchProfile LoadProfile()
    {
        if (!File.Exists(ProfilePath))
        {
            throw new EndfieldLaunchException("尚未保存终末地启动设置。", "Endfield launch settings have not been saved yet.");
        }

        return JsonSerializer.Deserialize<LaunchProfile>(File.ReadAllText(ProfilePath))
            ?? throw new EndfieldLaunchException("启动配置为空。", "The launch profile is empty.");
    }

    public void SaveProfile(LaunchProfile profile) => WriteJson(ProfilePath, profile);

    public string? ReadCachedParent(string game)
    {
        try
        {
            if (!File.Exists(ParentCachePath)) return null;
            var cache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ParentCachePath));
            return cache is not null && cache.TryGetValue("game", out string? cachedGame) && cache.TryGetValue("parent", out string? parent)
                && string.Equals(Normalize(cachedGame), Normalize(game), StringComparison.OrdinalIgnoreCase) && File.Exists(parent)
                ? parent
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public void WriteCachedParent(string game, string parent) =>
        WriteJson(ParentCachePath, new Dictionary<string, string> { ["game"] = Normalize(game), ["parent"] = Normalize(parent) });

    public void WriteStatus(string sessionId, string state, LaunchText message, int processId = 0)
    {
        try
        {
            WriteJson(StatusPath, new LaunchStatus(sessionId, state, message.Zh, message.En, processId, DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Status is a courtesy for the manager window; the launch itself must continue.
        }
    }

    public LaunchStatus? ReadStatus()
    {
        try
        {
            return File.Exists(StatusPath) ? JsonSerializer.Deserialize<LaunchStatus>(File.ReadAllText(StatusPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Log(LaunchText text, bool english)
    {
        try
        {
            lock (LogGate)
            {
                Directory.CreateDirectory(DataDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaximumLogBytes)
                {
                    File.Move(LogPath, LogPath + ".previous", overwrite: true);
                }

                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {text.For(english)}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never stop a launch.
        }
    }

    internal static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteJson<T>(string path, T value) => WriteAtomic(path, JsonSerializer.Serialize(value, JsonOptions));

    private static string Normalize(string path) => Path.GetFullPath(path);
}
