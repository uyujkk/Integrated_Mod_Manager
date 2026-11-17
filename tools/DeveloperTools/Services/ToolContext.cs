using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntegratedModManager.Core.DeveloperTools;

namespace IntegratedModManager.DeveloperTools.Services;

/// <summary>Saved choices of the tools (in %LOCALAPPDATA%\IntegratedModManagerDeveloperTools\settings.json).</summary>
internal sealed class ToolSettings
{
    public string RootPath { get; set; } = string.Empty;
    public string? Language { get; set; }
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    public bool AutoRefresh { get; set; }
    public string? StringsFolder { get; set; }

    /// <summary>Written by 3.9.5; read once to keep an English choice.</summary>
    public bool? English { get; set; }
}

/// <summary>Options from the command line, for scripts and screenshots; they are not saved.</summary>
internal sealed record ToolOptions(string? Root, string? Page, string? Language, string? Theme, string? Mod = null)
{
    public static ToolOptions Parse(IReadOnlyList<string> args)
    {
        string? Value(string name)
        {
            for (int i = 0; i < args.Count - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
        return new ToolOptions(Value("--root"), Value("--page"), Value("--language"), Value("--theme"), Value("--mod"));
    }
}

/// <summary>
/// What every page shares: the app folder being inspected, its settings file, the text, and the status bar. The tools
/// read the app's files; the only writes are their own (settings, diagnostics, snapshots) and cache clearing that the
/// user confirms.
/// </summary>
internal sealed class ToolContext
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public ToolContext(ToolOptions options)
    {
        Options = options;
        LoadSettings();
        Strings.Use(options.Language ?? Settings.Language ?? (Settings.English == true ? "en-US" : Strings.SystemDefault()));
        RootPath = ResolveInitialRoot(options.Root ?? Settings.RootPath);
    }

    public ToolOptions Options { get; }

    /// <summary>The main window, for file and folder pickers.</summary>
    public nint WindowHandle { get; set; }
    public ToolSettings Settings { get; private set; } = new();
    public ToolStrings Strings { get; } = new();
    public string RootPath { get; private set; } = string.Empty;
    public JsonNode? Shell { get; private set; }
    public string? ShellError { get; private set; }

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntegratedModManagerDeveloperTools");

    public string DiagnosticsDirectory => Path.Combine(SettingsDirectory, "diagnostics");
    public string SnapshotsDirectory => Path.Combine(SettingsDirectory, "snapshots");
    public string ShellConfigPath => Path.Combine(RootPath, "beta-shell.json");
    public string LegacyConfigPath => Path.Combine(RootPath, "config.ini");

    public event Action<string, Microsoft.UI.Xaml.Controls.InfoBarSeverity>? StatusChanged;

    public string T(string key, params object?[] values) => Strings.T(key, values);

    public void SetStatus(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity) => StatusChanged?.Invoke(message, severity);

    public void SetRoot(string path)
    {
        RootPath = Path.GetFullPath(path);
        if (Options.Root is null)
        {
            Settings.RootPath = RootPath;
            SaveSettings();
        }
    }

    /// <summary>Reads <c>beta-shell.json</c> again (the pages call this on refresh).</summary>
    public void Reload()
    {
        Shell = null;
        ShellError = null;
        if (!File.Exists(ShellConfigPath)) return;
        try { Shell = JsonNode.Parse(File.ReadAllText(ShellConfigPath)); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { ShellError = ex.Message; }
    }

    public JsonArray Array(string name) => Shell?[name] as JsonArray ?? [];

    public static bool IsMainAppRunning()
    {
        try { return Process.GetProcessesByName("ModFolderCopier.WinUI").Length > 0; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public void OpenFolder(string path)
    {
        try
        {
            string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            while (folder is not null && !Directory.Exists(folder)) folder = Path.GetDirectoryName(folder);
            if (folder is null) throw new DirectoryNotFoundException(path);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus(T("Status.OpenFolderFailed", ex.Message), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    /// <summary>Replaces this computer's private paths and the user name before anything leaves the tools.</summary>
    public string Sanitize(string value)
    {
        string sanitized = value;
        if (!string.IsNullOrWhiteSpace(RootPath)) sanitized = sanitized.Replace(RootPath, "<MAIN_APP_ROOT>", StringComparison.OrdinalIgnoreCase);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) sanitized = sanitized.Replace(profile, "<USER_PROFILE>", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(Environment.UserName) ? sanitized
            : sanitized.Replace(Environment.UserName, "<USER>", StringComparison.OrdinalIgnoreCase);
    }

    public void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            Settings.English = null;
            File.WriteAllText(Path.Combine(SettingsDirectory, "settings.json"), JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Saving a choice must never stop the tools.
        }
    }

    private void LoadSettings()
    {
        try
        {
            string path = Path.Combine(SettingsDirectory, "settings.json");
            if (File.Exists(path)) Settings = JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path), JsonOptions) ?? new ToolSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Settings = new ToolSettings();
        }
    }

    // The configured folder; else the app folder of the install the tools sit in (<install>\DeveloperTools);
    // else beside the tools; else a source checkout's Release build.
    private static string ResolveInitialRoot(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return Path.GetFullPath(configured);
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.GetParent(baseDirectory)?.FullName is { } install && DeveloperToolsLocation.AppFolderForInstallRoot(install) is { } installed)
            return installed;
        if (File.Exists(Path.Combine(baseDirectory, "ModFolderCopier.WinUI.exe"))) return baseDirectory;
        string current = baseDirectory;
        for (int depth = 0; depth < 9; depth++)
        {
            string releaseRoot = Path.Combine(current, "src", "WinUI3", "bin", "x64", "Release", "net8.0-windows10.0.19041.0", "win-x64");
            if (File.Exists(Path.Combine(releaseRoot, "ModFolderCopier.WinUI.exe"))) return releaseRoot;
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }
        return baseDirectory;
    }

    /// <summary>A source checkout's language files, when the tools run from inside one.</summary>
    public static string? FindSourceStrings()
    {
        string? current = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && current is not null; depth++)
        {
            string strings = Path.Combine(current, "src", "WinUI3", "Strings");
            if (Directory.Exists(strings)) return strings;
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
