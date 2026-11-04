using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// Each test starts its own copy of the app on the synthetic TestData. They need a desktop session and
// run one at a time; scripts\test-ui.ps1 runs them.
public sealed class SmokeTests
{
    [Fact]
    public void FirstInstall_SkippingTheGameLeavesTheRepositoryWithoutOne()
    {
        using AppSession app = AppSession.FirstInstall().Start();

        AppSession.Invoke(app.FindByName(app.Text("FirstRun.Later"), TimeSpan.FromSeconds(20)));

        JsonNode repository = app.ShellConfig()["Repositories"]![0]!;
        Assert.Equal(string.Empty, (string?)repository["OnlineGameName"] ?? string.Empty);
        Assert.Equal(string.Empty, (string?)repository["OnlineCategoryId"] ?? string.Empty);
        Assert.True(app.ShellConfig()["RepositoryGamesExplicit"]!.GetValue<bool>());
        app.AssertNoErrors();
    }

    [Fact]
    public void FirstInstall_ChoosingAGameFillsTheRepository()
    {
        using AppSession app = AppSession.FirstInstall().Start();

        AppSession.Invoke(app.Find("FirstRunGame_genshin", TimeSpan.FromSeconds(20)));
        AppSession.Invoke(app.FindByName(app.Text("FirstRun.Use")));

        JsonNode repository = app.ShellConfig()["Repositories"]![0]!;
        Assert.Equal("Genshin Impact", (string?)repository["OnlineGameName"]);
        Assert.Equal("18140", (string?)repository["OnlineCategoryId"]);
        app.AssertNoErrors();
    }

    [Fact]
    public void Navigation_EveryPageOpensWithoutErrors()
    {
        using AppSession app = AppSession.Configured().Start();

        foreach (string page in new[] { "RepositoryNavButton", "OnlineNavButton", "UpdatesNavButton", "ShortcutsNavButton", "SettingsNavButton", "DashboardNavButton" })
        {
            AppSession.Invoke(app.Find(page));
            Assert.True(app.IsRunning, "The app ended after opening " + page);
        }

        app.Capture(nameof(Navigation_EveryPageOpensWithoutErrors));
        app.AssertNoErrors();
    }

    [Fact]
    public void Dashboard_SwitchingToARepositoryWithoutFoldersKeepsRunning()
    {
        // Regression: an empty source path made the character lookup throw and ended the process.
        using AppSession app = AppSession.Configured((shell, session) =>
        {
            shell["Repositories"]!.AsArray().Add(new JsonObject
            {
                ["Id"] = "empty-repository", ["Name"] = "Empty repository", ["SourcePath"] = "", ["TargetPath"] = "", ["UseDirectoryLinks"] = true
            });
            shell["Repositories"]![0]!["UseDirectoryLinks"] = true;
        }).Start();

        foreach (AutomationElement button in app.MainWindow.FindAllDescendants(cf => cf.ByName(app.Text("Dashboard.SwitchToThisRepository"))))
            AppSession.Invoke(button);

        Assert.True(app.IsRunning);
        Assert.Equal("empty-repository", (string?)app.ShellConfig()["SelectedRepositoryId"]);
        app.AssertNoErrors();
    }

    [Fact]
    public void Repository_BatchDialogOpensWithTheListedMods()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();

        AppSession.Invoke(app.Find("BatchModsButton"));
        AutomationElement dialog = app.FindByName(app.Text("BatchMods.Title"));
        app.Capture(nameof(Repository_BatchDialogOpensWithTheListedMods));
        AppSession.Invoke(app.Find("CloseButton"));

        Assert.NotNull(dialog);
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }

    [Fact]
    public void Presets_ShareButtonsAreThere()
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["RepositoryWorkspaceView"] = "presets";
        }).Start();

        Assert.True(app.Find("PresetShareImportButton").IsEnabled);
        // Nothing to share before a preset exists.
        Assert.False(app.Find("PresetShareExportButton").IsEnabled);
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_ClickThroughIgnoresTheMouseUntilTheHotkey()
    {
        using AppSession app = AppSession.Configured().Start();
        PressHotkey();
        Window overlay = Retry(() => app.FindWindow(window => window.Title.StartsWith("IMM ", StringComparison.Ordinal)))
            ?? throw new InvalidOperationException("The overlay did not open.");
        AutomationElement clickThrough = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayClickThroughButton"), "click-through button");
        var middle = overlay.BoundingRectangle;
        var point = new Point { X = middle.X + middle.Width / 2, Y = middle.Y + middle.Height * 3 / 5 };
        nint handle = overlay.Properties.NativeWindowHandle.Value;

        Assert.Equal(handle, TopWindowAt(point));
        AppSession.Invoke(clickThrough);
        Assert.NotEqual(handle, TopWindowAt(point));
        PressHotkey();
        Thread.Sleep(800);
        Assert.Equal(handle, TopWindowAt(point));
        app.AssertNoErrors();
    }

    [Fact]
    public void Shortcuts_SummaryShowsTheSavedHotkey()
    {
        using AppSession app = AppSession.Configured().Start();

        AppSession.Invoke(app.Find("ShortcutsNavButton"));

        app.FindByName(string.Format(app.Text("ShortcutPanel.OverlayHotkeySummary"), "Ctrl+Alt", "F9"));
        app.AssertNoErrors();
    }

    [Fact]
    public void Shortcuts_InputMethodIsSaved()
    {
        using AppSession app = AppSession.Configured().Start();
        AppSession.Invoke(app.Find("ShortcutsNavButton"));

        // The settings sit in collapsed sections, which are not in the automation tree until opened.
        foreach (AutomationElement section in app.MainWindow.FindAllDescendants(cf => cf.ByClassName("Microsoft.UI.Xaml.Controls.Expander")))
            if (section.Patterns.ExpandCollapse.IsSupported) section.Patterns.ExpandCollapse.Pattern.Expand();
        Thread.Sleep(800);
        AutomationElement picker = app.Find("ShortcutInputModePicker");
        picker.Patterns.ExpandCollapse.Pattern.Expand();
        Thread.Sleep(500);
        AppSession.Invoke(app.FindByName(app.Text("ShortcutPanel.VirtualKeysLegacyCompatibility")));
        Thread.Sleep(1000);

        Assert.Equal("VirtualKey", (string?)app.ShellConfig()["ShortcutInputMode"]);
        app.AssertNoErrors();
    }

    [Fact]
    public void Dashboard_ShowsTheRepositoryPaths()
    {
        using AppSession app = AppSession.Configured().Start();

        Assert.EndsWith("ModRepository", app.Find("SourceTextBox").Patterns.Value.Pattern.Value.Value);
        Assert.True(app.Shows("TargetTextBox"));
        app.AssertNoErrors();
    }

    [Fact]
    public void Repository_ShowsTheCharacterAndItsMods()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();

        AppSession.Invoke(app.FindByName("TestCharacter"));

        app.FindByName("TestModA");
        app.FindByName("TestModB");
        Assert.True(app.Shows("FirstLevelSearchTextBox"));
        Assert.True(app.Shows("RefreshButton"));
        app.AssertNoErrors();
    }

    [Theory]
    [InlineData("Covers")]
    [InlineData("Presets")]
    [InlineData("List")]
    public void Repository_ViewButtonsSwitchAndAreSaved(string view)
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["RepositoryWorkspaceView"] = view == "List" ? "covers" : "list";
        }).Start();

        AppSession.Invoke(app.Find("RepositoryView" + view));

        Assert.Equal(view, (string?)app.ShellConfig()["RepositoryWorkspaceView"], ignoreCase: true);
        Assert.Equal(view == "Presets", app.Shows("PresetShareImportButton"));
        Assert.Equal(view == "List", app.Shows("SecondLevelListView"));
        app.AssertNoErrors();
    }

    // One id in each group of the settings page, from top to bottom.
    private static readonly string[] SettingsCardControls = ["LanguageComboBox", "CheckUpdatesButton", "EditOnlineRepositoryButton", "ExportDiagnosticsButton", "ProjectResourcesTitleTextBlock"];

    // Stage J2: one long page instead of section buttons, so a narrow window has every group on it, to scroll to.
    [Fact]
    public void Settings_NarrowWindowHasEveryGroupOnOnePage()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        app.Resize(1000, 760);

        Assert.All(SettingsCardControls, id => Assert.True(app.Shows(id), id));
        Assert.False(app.Shows("SettingsSection0"));
        app.Capture(nameof(Settings_NarrowWindowHasEveryGroupOnOnePage));
        app.AssertNoErrors();
    }

    // The language list is made from the language files, and a choice relabels the window and is saved.
    [Fact]
    public void Settings_LanguageListComesFromTheFilesAndSwitchesTheWindow()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        app.Resize(1700, 1000);

        ComboBox languages = app.Find("LanguageComboBox").AsComboBox();
        languages.Expand();
        string[] names = languages.Items.Select(item => item.Name).ToArray();
        languages.Collapse();
        Assert.Equal(AppSession.LanguageFiles().Count(), names.Length);
        Assert.Equal(["中文", "English"], names.Take(2));

        languages.Select(1);
        string englishTitle = AppSession.TextIn("en-US", "Common.IntegratedModManager").Split('{')[0].Trim();
        Assert.True(SpinWait.SpinUntil(() => app.MainWindow.Title.StartsWith(englishTitle, StringComparison.Ordinal), TimeSpan.FromSeconds(5)), app.MainWindow.Title);
        string[] config = File.ReadAllLines(Path.Combine(app.AppFolder, "config.ini"));
        Assert.Contains("language=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("en")), config);
        app.Capture(nameof(Settings_LanguageListComesFromTheFilesAndSwitchesTheWindow));
        app.AssertNoErrors();
    }

    // Smoke test of the Settings page (stage J2): open it and change the theme, which is applied and saved.
    [Fact]
    public void Settings_ThemeChoiceIsSaved()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        app.Resize(1700, 1000);

        ComboBox themes = app.Find("ThemeComboBox").AsComboBox();
        themes.Select(2);

        Assert.True(SpinWait.SpinUntil(() => (string?)app.ShellConfig()["ThemePreference"] == "dark", TimeSpan.FromSeconds(5)),
            (string?)app.ShellConfig()["ThemePreference"]);
        themes.Select(1);
        Assert.True(SpinWait.SpinUntil(() => (string?)app.ShellConfig()["ThemePreference"] == "light", TimeSpan.FromSeconds(5)),
            (string?)app.ShellConfig()["ThemePreference"]);
        app.Capture(nameof(Settings_ThemeChoiceIsSaved));
        app.AssertNoErrors();
    }

    [Fact]
    public void Settings_WideWindowShowsEveryCard()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        app.Resize(1700, 1000);

        Assert.All(SettingsCardControls, id => Assert.True(app.Shows(id), id));
        app.Capture(nameof(Settings_WideWindowShowsEveryCard));
        app.AssertNoErrors();
    }

    [Theory]
    [InlineData("Grid", "OnlineVirtualizedGridView")]
    [InlineData("List", "OnlineVirtualizedListView")]
    public void Online_LayoutButtonsSwitchAndAreSaved(string layout, string shownView)
    {
        // The test repository has no online category, so the page opens without loading anything.
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "online";
            shell["OnlineCardLayout"] = layout == "Grid" ? "list" : "grid";
        }).Start();

        Assert.True(app.Shows("OnlineSearchTextBox"));
        AppSession.Invoke(app.Find("Online" + layout + "LayoutButton"));

        Assert.Equal(layout, (string?)app.ShellConfig()["OnlineCardLayout"], ignoreCase: true);
        Assert.True(app.Shows(shownView), shownView);
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }

    [Fact]
    public void Updates_ShowsItsSectionsAndSavesTheCheckInterval()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "updates").Start();
        foreach (string id in new[] { "CheckModUpdatesButton", "ClearDownloadTasksButton", "ModUpdateIntervalComboBox" })
            Assert.True(app.Shows(id), id);
        string? before = (string?)app.ShellConfig()["ModUpdateCheckInterval"];

        AutomationElement picker = app.Find("ModUpdateIntervalComboBox");
        picker.Patterns.ExpandCollapse.Pattern.Expand();
        Thread.Sleep(500);
        AutomationElement[] options = picker.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
        AutomationElement other = options.First(option => !option.Patterns.SelectionItem.Pattern.IsSelected.Value);
        AppSession.Invoke(other);
        Thread.Sleep(800);

        string? after = (string?)app.ShellConfig()["ModUpdateCheckInterval"];
        Assert.False(string.IsNullOrEmpty(after));
        Assert.NotEqual(before, after);
        app.AssertNoErrors();
    }

    // Smoke test of the Updates page (stage J3): open it and turn conflict detection off, which is saved.
    [Fact]
    public void Updates_ConflictDetectionSwitchIsSaved()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "updates").Start();
        Assert.True(app.Shows("OpenCombinationPresetsButton"));
        bool before = (bool?)app.ShellConfig()["EnableConflictDetection"] ?? true;

        AppSession.Invoke(app.Find("ConflictDetectionToggleSwitch"));

        Assert.True(SpinWait.SpinUntil(() => (bool?)app.ShellConfig()["EnableConflictDetection"] == !before, TimeSpan.FromSeconds(5)),
            "EnableConflictDetection stayed " + before);
        app.Capture(nameof(Updates_ConflictDetectionSwitchIsSaved));
        app.AssertNoErrors();
    }

    // Ctrl+Alt+F9, set by AppSession.Configured.
    private static void PressHotkey()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.F9);
        Thread.Sleep(1500);
    }

    private static T? Retry<T>(Func<T?> find) where T : class
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (find() is { } found) return found;
            Thread.Sleep(250);
        }
        return null;
    }

    private static nint TopWindowAt(Point point) => GetAncestor(WindowFromPoint(point), 2);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}
