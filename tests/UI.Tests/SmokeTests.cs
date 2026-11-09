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

    // Regression: going back to the dashboard from another page ended the app with a layout cycle when the
    // current repository had no folders (so no characters). Since J1 a page leaves the window while another
    // is shown and is laid out again on return; the empty characters grid's scroll bar then flipped on and off.
    // Every page is left and opened again several times, in a normal and in a large window.
    [Theory]
    [InlineData(1400, 900, false)]
    [InlineData(2048, 1110, false)]
    [InlineData(1400, 900, true)]
    [InlineData(2048, 1110, true)]
    public void Navigation_LeavingAndReturningToEveryPageKeepsRunning(int width, int height, bool repositoryWithoutFolders)
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "online";
            if (!repositoryWithoutFolders) return;
            shell["Repositories"]!.AsArray().Add(new JsonObject
            {
                ["Id"] = "empty-repository", ["Name"] = "Empty repository", ["SourcePath"] = "", ["TargetPath"] = ""
            });
            shell["SelectedRepositoryId"] = "empty-repository";
        }).Start();
        app.Resize(width, height);
        string[] pages = ["OnlineNavButton", "DashboardNavButton", "RepositoryNavButton", "DashboardNavButton", "UpdatesNavButton",
            "DashboardNavButton", "ShortcutsNavButton", "DashboardNavButton", "SettingsNavButton", "DashboardNavButton"];
        for (int round = 0; round < 3; round++)
        {
            foreach (string page in pages)
            {
                AppSession.Invoke(app.Find(page));
                Assert.True(app.IsRunning, $"The app ended after opening {page} (round {round + 1})");
            }
        }

        app.Capture(nameof(Navigation_LeavingAndReturningToEveryPageKeepsRunning));
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

    // Stage J5b: the overlay is its own class; its compact view goes through the page's settings and is saved.
    [Fact]
    public void Overlay_CompactViewIsSaved()
    {
        using AppSession app = AppSession.Configured().Start();
        PressHotkey();
        Window overlay = Retry(() => app.FindWindow(window => window.Title.StartsWith("IMM ", StringComparison.Ordinal)))
            ?? throw new InvalidOperationException("The overlay did not open.");

        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayCompactButton"), "compact button"));
        Thread.Sleep(600);

        Assert.True(app.ShellConfig()["OverlayCompactView"]!.GetValue<bool>());
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

    // Stage J5: the page reads the selected Mod's bindings, and a page setting reaches the window and is saved.
    [Fact]
    public void Shortcuts_PageReadsTheBindingsAndSavesItsSettings()
    {
        using AppSession app = AppSession.Configured().Start();
        AppSession.Invoke(app.Find("ShortcutsNavButton"));

        app.FindByName(string.Format(app.Text("ShortcutPanel.ReadBindingsInGameConditionsAre"), 2), TimeSpan.FromSeconds(15));
        AppSession.Invoke(app.Find("ShortcutHideUnsupportedToggle"));
        Thread.Sleep(600);

        Assert.True(app.ShellConfig()["ShortcutHideUnsupported"]!.GetValue<bool>());
        app.AssertNoErrors();
    }

    // Stage J5: the global hotkey is a switch; there is no Apply button to forget.
    [Fact]
    public void Shortcuts_GlobalHotkeySwitchTurnsTheHotkeyOffAndOn()
    {
        using AppSession app = AppSession.Configured().Start();
        AppSession.Invoke(app.Find("ShortcutsNavButton"));
        // The overlay settings are below the bindings; their rows exist only once open and on screen.
        var bounds = app.Find("ShortcutsScrollViewer").BoundingRectangle;
        Mouse.MoveTo(new System.Drawing.Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        app.Expand("ShortcutsOverlayExpander");
        AutomationElement? toggle = null;
        for (int step = 0; step < 12 && toggle is null; step++)
        {
            toggle = app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ShortcutHotkeyToggle"))
                ?? app.MainWindow.FindFirstDescendant(cf => cf.ByName(app.Text("ShortcutPanel.GlobalHotkey")).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)));
            if (toggle is null)
            {
                Mouse.Scroll(-2);
                Thread.Sleep(400);
            }
        }
        Assert.NotNull(toggle);

        AppSession.Invoke(toggle);
        Assert.False(app.ShellConfig()["ShortcutPanelHotkeyEnabled"]!.GetValue<bool>());
        AppSession.Invoke(toggle);
        Assert.True(app.ShellConfig()["ShortcutPanelHotkeyEnabled"]!.GetValue<bool>());
        app.AssertNoErrors();
    }

    [Fact]
    public void Shortcuts_InputMethodIsSaved()
    {
        using AppSession app = AppSession.Configured().Start();
        AppSession.Invoke(app.Find("ShortcutsNavButton"));

        // The sending settings are at the bottom of the page, folded; their rows exist only once on screen and open.
        void ScrollToEnd()
        {
            var bounds = app.Find("ShortcutsScrollViewer").BoundingRectangle;
            Mouse.MoveTo(new System.Drawing.Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
            Mouse.Scroll(-40);
            Thread.Sleep(600);
        }
        ScrollToEnd();
        app.Expand("ShortcutsSendingExpander");
        ScrollToEnd();
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
        // Stage J4: the paths are folded into an expander while they are all set.
        app.Expand("DashboardPathsExpander");

        Assert.EndsWith("ModRepository", app.Find("SourceTextBox").Patterns.Value.Pattern.Value.Value);
        Assert.True(app.Shows("TargetTextBox"));
        app.AssertNoErrors();
    }

    // Smoke test of the dashboard page (stage J4): its path boxes edit the repository the rest of the app uses.
    [Fact]
    public void Dashboard_EditedTargetPathIsSavedWithTheRepository()
    {
        using AppSession app = AppSession.Configured().Start();
        string newTarget = Path.Combine(app.TestData, "Loader");
        app.Expand("DashboardPathsExpander");

        app.Find("TargetTextBox").Patterns.Value.Pattern.SetValue(newTarget);
        AppSession.Invoke(app.Find("RepositoryNavButton"));

        Assert.True(SpinWait.SpinUntil(() => (string?)app.ShellConfig()["Repositories"]![0]!["TargetPath"] == newTarget, TimeSpan.FromSeconds(5)),
            (string?)app.ShellConfig()["Repositories"]![0]!["TargetPath"]);
        AppSession.Invoke(app.Find("DashboardNavButton"));
        app.Expand("DashboardPathsExpander");
        Assert.Equal(newTarget, app.Find("TargetTextBox").Patterns.Value.Pattern.Value.Value);
        app.Capture(nameof(Dashboard_EditedTargetPathIsSavedWithTheRepository));
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
        // Stage J3-b: the check button heads the page; the interval is in the Mod updates view, "Clear" in the download history.
        foreach (string id in new[] { "CheckModUpdatesButton", "ModUpdateIntervalComboBox" })
            Assert.True(app.Shows(id), id);
        AppSession.Invoke(app.Find("UpdatesViewDownloads"));
        Assert.True(app.Shows("ClearDownloadTasksButton"));
        AppSession.Invoke(app.Find("UpdatesViewMods"));
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
        AppSession.Invoke(app.Find("UpdatesViewRecovery"));
        Assert.True(app.Shows("OpenCombinationPresetsButton"));
        bool before = (bool?)app.ShellConfig()["EnableConflictDetection"] ?? true;

        AppSession.Invoke(app.Find("ConflictDetectionToggleSwitch"));

        Assert.True(SpinWait.SpinUntil(() => (bool?)app.ShellConfig()["EnableConflictDetection"] == !before, TimeSpan.FromSeconds(5)),
            "EnableConflictDetection stayed " + before);
        app.Capture(nameof(Updates_ConflictDetectionSwitchIsSaved));
        app.AssertNoErrors();
    }

    // Stage J3-b: the views are named for Narrator, switch with the arrow keys and are remembered.
    [Fact]
    public void Updates_ViewsSwitchByKeyboardAndAreSaved()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "updates").Start();
        AutomationElement mods = app.Find("UpdatesViewMods");
        Assert.Equal(app.Text("Updates.ViewModUpdates"), mods.Name);
        Assert.Equal(app.Text("Updates.ViewDownloadHistory"), app.Find("UpdatesViewDownloads").Name);
        Assert.Equal(app.Text("Updates.ViewInstallAndRecovery"), app.Find("UpdatesViewRecovery").Name);
        Assert.False(app.Shows("ConflictDetectionToggleSwitch"));

        // Tab from the check button reaches the selector bar the way a keyboard user does.
        app.Find("CheckModUpdatesButton").Focus();
        Thread.Sleep(300);
        Keyboard.Type(VirtualKeyShort.TAB);
        Thread.Sleep(300);
        string afterTab = app.MainWindow.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault ?? "";
        Keyboard.Type(VirtualKeyShort.RIGHT);
        Thread.Sleep(300);
        Keyboard.Type(VirtualKeyShort.RIGHT);
        Thread.Sleep(300);
        Keyboard.Type(VirtualKeyShort.SPACE);
        Thread.Sleep(300);

        Assert.True(SpinWait.SpinUntil(() => app.Shows("ConflictDetectionToggleSwitch"), TimeSpan.FromSeconds(5)),
            "The recovery view did not open; selected: " + string.Join(",", new[] { "UpdatesViewMods", "UpdatesViewDownloads", "UpdatesViewRecovery" }
                .Where(id => app.Find(id).Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault == true))
            + "; after Tab: " + afterTab + "; focused: " + app.MainWindow.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault);
        Assert.True(SpinWait.SpinUntil(() => (string?)app.ShellConfig()["UpdatesView"] == "recovery", TimeSpan.FromSeconds(5)),
            (string?)app.ShellConfig()["UpdatesView"]);
        app.Capture(nameof(Updates_ViewsSwitchByKeyboardAndAreSaved));
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
