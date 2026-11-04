using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace IntegratedModManager.UI.Tests;

/// <summary>
/// A private copy of the built application in a temporary folder, with the synthetic TestData beside it,
/// so a test never touches a real installation or another test's settings.
/// </summary>
public sealed class AppSession : IDisposable
{
    private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);
    private static readonly Lazy<Dictionary<string, string>> ChineseText = new(() => LoadText("zh-CN"));
    // A first install picks Chinese on a Chinese system and English elsewhere, such as a CI runner.
    private static readonly Lazy<Dictionary<string, string>> FirstInstallText = new(() => LoadText(
        System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US"));
    private Lazy<Dictionary<string, string>> _text = ChineseText;
    // The app shows its what's-new dialog once per feature set; a configured session has seen it.
    private static readonly Lazy<string> WhatsNewFeatureSet = new(() => System.Text.RegularExpressions.Regex.Match(
        File.ReadAllText(Path.Combine(RepositoryRoot.Value, "src", "WinUI3", "MainWindow.WhatsNew.cs")), "WhatsNewFeatureSet = \"([^\"]+)\"").Groups[1].Value);
    private readonly UIA3Automation _automation = new();
    private Application? _application;

    public string Root { get; }
    public string AppFolder => Path.Combine(Root, "WinUI3");
    public string TestData => Path.Combine(Root, "TestData");
    public Window MainWindow { get; private set; } = null!;
    public UIA3Automation Automation => _automation;

    private AppSession()
    {
        Root = Path.Combine(Path.GetTempPath(), "imm-ui-" + Guid.NewGuid().ToString("N")[..12]);
        CopyDirectory(Path.Combine(ApplicationSource(), "WinUI3"), AppFolder);
        CopyDirectory(Path.Combine(RepositoryRoot.Value, "tests", "TestData"), TestData);
    }

    /// <summary>A copy that has never run: the next start is a first install.</summary>
    public static AppSession FirstInstall() => new() { _text = FirstInstallText };

    /// <summary>A copy that already has settings, so no first-run dialog appears.</summary>
    public static AppSession Configured(Action<JsonObject, AppSession>? configure = null)
    {
        var session = new AppSession();
        string mods = Path.Combine(session.TestData, "ModRepository"), target = Path.Combine(session.TestData, "Loader", "Mods");
        var shell = new JsonObject
        {
            ["SelectedRepositoryId"] = "ui-repository",
            ["CurrentPrimarySection"] = "dashboard",
            ["CheckAppUpdatesOnStartup"] = false,
            ["WhatsNewSeenVersion"] = WhatsNewFeatureSet.Value,
            ["RepositoryGamesExplicit"] = true,
            ["ShortcutPanelHotkeyEnabled"] = true,
            ["ShortcutPanelHotkeyModifiers"] = 3,
            ["ShortcutPanelHotkeyKey"] = 0x78,
            ["Repositories"] = new JsonArray(new JsonObject
            {
                ["Id"] = "ui-repository", ["Name"] = "UI repository", ["SourcePath"] = mods, ["TargetPath"] = target,
                ["PersistentUserIniPath"] = Path.Combine(session.TestData, "Loader", "d3dx_user.ini"), ["OnlineSourceSite"] = "GameBanana"
            })
        };
        configure?.Invoke(shell, session);
        File.WriteAllText(Path.Combine(session.AppFolder, "beta-shell.json"), shell.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        // language=zh keeps the window text in step with ChineseText; the value is base64 like the app writes it.
        static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
        File.WriteAllLines(Path.Combine(session.AppFolder, "config.ini"),
            [$"source_dir={Encode(mods)}", $"target_dir={Encode(target)}", "launcher_path=", $"theme={Encode("dark")}", $"language={Encode("zh")}"]);
        return session;
    }

    public AppSession Start()
    {
        _application = Application.Launch(new ProcessStartInfo(Path.Combine(AppFolder, "ModFolderCopier.WinUI.exe")) { WorkingDirectory = AppFolder });
        MainWindow = Retry.WhileNull(() => _application.GetMainWindow(_automation, TimeSpan.FromSeconds(2)),
            TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(500), throwOnTimeout: true).Result!;
        // Startup work (scans, the shell rendering) finishes after the window appears.
        Thread.Sleep(3000);
        return this;
    }

    public bool IsRunning => _application is { HasExited: false };

    // Text in the language this session's app shows: Chinese once configured, the system's choice on a first install.
    public string Text(string key) => _text.Value.TryGetValue(key, out string? text) ? text : throw new KeyNotFoundException(key);

    // Text from another language file, for tests that switch the language.
    public static string TextIn(string language, string key) => LoadText(language)[key];

    public static IEnumerable<string> LanguageFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Value, "src", "WinUI3", "Strings"), "*.json").Select(Path.GetFileNameWithoutExtension)!;

    public AutomationElement Find(string automationId, TimeSpan? timeout = null) => FindIn(MainWindow, cf => cf.ByAutomationId(automationId), automationId, timeout);

    public AutomationElement FindByName(string name, TimeSpan? timeout = null) => FindIn(MainWindow, cf => cf.ByName(name), name, timeout);

    /// <summary>Whether the element is in the automation tree now; collapsed elements are not.</summary>
    public bool Shows(string automationId) => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null;

    /// <summary>Sets the main window's outer size, so a test can pick a layout breakpoint.</summary>
    public void Resize(int width, int height)
    {
        if (MainWindow.Patterns.Window.Pattern.WindowVisualState.Value != FlaUI.Core.Definitions.WindowVisualState.Normal)
            MainWindow.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);
        MainWindow.Patterns.Transform.Pattern.Move(20, 20);
        MainWindow.Patterns.Transform.Pattern.Resize(width, height);
        Thread.Sleep(1200);
    }

    public static AutomationElement FindIn(AutomationElement parent, Func<FlaUI.Core.Conditions.ConditionFactory, FlaUI.Core.Conditions.ConditionBase> condition,
        string description, TimeSpan? timeout = null)
        => Retry.WhileNull(() => parent.FindFirstDescendant(condition), timeout ?? TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250),
                ignoreException: true).Result
            ?? throw new InvalidOperationException("Not found: " + description);

    /// <summary>Windows of this application other than the main one, such as the overlay.</summary>
    public Window? FindWindow(Func<Window, bool> match) =>
        _application?.GetAllTopLevelWindows(_automation).FirstOrDefault(window => window.Properties.ProcessId.ValueOrDefault == _application.ProcessId && match(window));

    public static void Invoke(AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported) element.Patterns.Invoke.Pattern.Invoke();
        else if (element.Patterns.Toggle.IsSupported) element.Patterns.Toggle.Pattern.Toggle();
        else if (element.Patterns.SelectionItem.IsSupported) element.Patterns.SelectionItem.Pattern.Select();
        else element.Click();
        Thread.Sleep(600);
    }

    public JsonNode ShellConfig() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppFolder, "beta-shell.json")))!;

    /// <summary>Unhandled exceptions and issues the app logged during the session.</summary>
    public IReadOnlyList<string> Errors()
    {
        var errors = new List<string>();
        string startup = Path.Combine(AppFolder, "startup.log");
        if (File.Exists(startup)) errors.AddRange(File.ReadAllLines(startup).Where(line => line.Contains("Unhandled", StringComparison.Ordinal)));
        string issues = Path.Combine(AppFolder, "app-errors.log");
        if (File.Exists(issues)) errors.AddRange(File.ReadAllLines(issues).Where(line => line.Length > 0).Take(20));
        return errors;
    }

    public void AssertNoErrors()
    {
        IReadOnlyList<string> errors = Errors();
        Assert.True(errors.Count == 0, "The app reported:\n" + string.Join("\n", errors));
    }

    /// <summary>Saves a picture of the main window beside the test output, for a failure that needs looking at.</summary>
    public void Capture(string name)
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(folder);
            FlaUI.Core.Capturing.Capture.Element(MainWindow).ToFile(Path.Combine(folder, name + ".png"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            // A picture is a convenience; it never decides a test.
        }
    }

    public void Dispose()
    {
        try
        {
            if (_application is { HasExited: false })
            {
                using Process process = Process.GetProcessById(_application.ProcessId);
                _application.Close();
                if (!process.WaitForExit(8000)) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
        _application?.Dispose();
        _automation.Dispose();
        for (int attempt = 0; attempt < 10 && Directory.Exists(Root); attempt++)
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
        }
    }

    // The tested runtime: IMM_UI_APP, or the staging folder scripts\test-all.ps1 leaves behind.
    private static string ApplicationSource()
    {
        string folder = Environment.GetEnvironmentVariable("IMM_UI_APP")
            ?? Path.Combine(RepositoryRoot.Value, "artifacts", "verification", "ReleaseSource");
        if (!File.Exists(Path.Combine(folder, "WinUI3", "ModFolderCopier.WinUI.exe")))
            throw new InvalidOperationException("No built application in " + folder + ". Run scripts\\test-all.ps1 first, or set IMM_UI_APP.");
        return folder;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "IntegratedModManager.sln"))) return folder.FullName;
        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, string> LoadText(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepositoryRoot.Value, "src", "WinUI3", "Strings", language + ".json")))!;

    private static void CopyDirectory(string source, string target)
    {
        foreach (string folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, folder)));
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}
