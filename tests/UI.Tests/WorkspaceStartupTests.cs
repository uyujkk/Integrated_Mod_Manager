using System.Diagnostics;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// What the repository scan, the workspace status line and the dashboard's path actions do while the Mod workspace has
// not been opened, written before stage J finish step 7b moves them out of the workspace page and creates the page on
// first use. Only the test's own copy and its synthetic data change.
public sealed class WorkspaceStartupTests
{
    private static void WaitUntil(Func<bool> condition, string what, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    private static string Decoded(AppSession app, string key)
    {
        string line = File.ReadAllLines(Path.Combine(app.AppFolder, "config.ini")).Single(line => line.StartsWith(key + "=", StringComparison.Ordinal));
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(line[(key.Length + 1)..]));
    }

    // The command palette lists the scanned characters and Mods.
    private static bool PaletteFinds(AppSession app, string text)
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
        AutomationElement search = app.FindByName(app.Text("CommandPalette.Placeholder"), TimeSpan.FromSeconds(5));
        search.AsTextBox().Text = text;
        Thread.Sleep(600);
        bool found = app.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Any(item => item.Name.Contains(text, StringComparison.Ordinal)
                || item.FindFirstDescendant(cf => cf.ByName(text)) is not null);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Thread.Sleep(300);
        return found;
    }

    private static string StatusLine(AppSession app) => app.Find("StatusTextBlock", TimeSpan.FromSeconds(10)).Name;

    private static void OpenWorkspace(AppSession app)
    {
        AppSession.Invoke(app.Find("RepositoryNavButton"));
        app.Find("FirstLevelListView", TimeSpan.FromSeconds(10));
        Thread.Sleep(800);
    }

    // The scan runs at startup whether or not the workspace is shown: it gives each deployed Mod its Default slot
    // (which the overlay offers) and fills the command palette's characters and Mods.
    [Fact]
    public void NeverOpened_TheStartupScanSeedsDefaultSlotsAndFillsThePalette()
    {
        using AppSession app = AppSession.Configured(OverlaySamples.Apply).Start();
        app.WaitForSaved(saved => saved["DefaultSlotSeeds"]?["ui-repository"]?.AsArray().Count == 2, "both deployed Mods offered a Default slot");
        JsonArray slots = app.ReadSaved()["ModPersistentSlots"]!.AsArray();
        Assert.Contains(slots, slot => (string?)slot!["ModRelativePath"] == @"TestCharacter\TestModA" && (bool?)slot["IsModDefault"] == true);
        Assert.Contains(slots, slot => (string?)slot!["ModRelativePath"] == @"TestCharacter\TestModB" && (bool?)slot["IsModDefault"] == true);

        Assert.True(PaletteFinds(app, "TestCharacter"), "the palette lists the scanned character");
        Assert.True(PaletteFinds(app, "TestModB"), "the palette lists the scanned Mods");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, FlaUI.Core.WindowsAPI.VirtualKeyShort.F9);
        Window? overlay = null;
        WaitUntil(() => (overlay = app.FindWindow(window => window.Title.StartsWith("IMM ", StringComparison.Ordinal))) is not null, "the overlay");
        Thread.Sleep(1200);
        AppSession.FindIn(overlay!, cf => cf.ByName(app.Text("PersistentSlots.DefaultSlotName")).And(cf.ByControlType(ControlType.Button)),
            "the overlay's Default slot button");
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }

    // The scan's result line, written while the dashboard is shown, is what the workspace shows when it is opened.
    [Fact]
    public void Status_TheStartupScanResultShowsWhenTheWorkspaceOpens()
    {
        using AppSession app = AppSession.Configured().Start();
        OpenWorkspace(app);
        Assert.Equal(string.Format(app.Text("LocalLibrary.LoadedFirstLevelFoldersAndSecond"), 1, 2), StatusLine(app));
        app.AssertNoErrors();
    }

    // Without a usable Mod folder, opening the workspace asks for one.
    [Fact]
    public void Status_WithoutAModFolderTheWorkspaceAsksForOne()
    {
        using AppSession app = AppSession.Configured((shell, session) =>
        {
            string missing = Path.Combine(session.TestData, "Missing");
            shell["Repositories"]![0]!["SourcePath"] = missing;
        });
        string config = Path.Combine(app.AppFolder, "config.ini");
        string missing = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Path.Combine(app.TestData, "Missing")));
        File.WriteAllLines(config, File.ReadAllLines(config).Select(line => line.StartsWith("source_dir=", StringComparison.Ordinal) ? "source_dir=" + missing : line));
        app.Start();
        OpenWorkspace(app);
        Assert.Equal(app.Text("LocalLibrary.ChooseAValidModStorageFolder"), StatusLine(app));
        app.AssertNoErrors();
    }

    // The dashboard's "choose Mod folder" saves the folder and scans it, whether or not the workspace was opened.
    [Fact]
    public void Dashboard_PickSourceSavesAndScansTheNewFolder()
    {
        using AppSession app = AppSession.Configured().Start();
        string other = Path.Combine(app.TestData, "OtherRepository");
        Directory.CreateDirectory(Path.Combine(other, "OtherCharacter", "OtherMod"));
        File.WriteAllText(Path.Combine(other, "OtherCharacter", "OtherMod", "mod.ini"), "; synthetic\n");
        app.Expand("DashboardPathsExpander");
        FileDialogs.Choose(app, app.Find("PickSourceButton"), other);

        WaitUntil(() => Decoded(app, "source_dir") == other, "the new Mod folder saved in config.ini");
        WaitUntil(() => PaletteFinds(app, "OtherCharacter"), "the new folder scanned", 20);
        OpenWorkspace(app);
        AppSession.Invoke(app.FindByName("OtherCharacter", TimeSpan.FromSeconds(10)));
        app.FindByName("OtherMod", TimeSpan.FromSeconds(10));
        app.AssertNoErrors();
    }

    [Fact]
    public void Dashboard_PickTargetAndLauncherAreSaved()
    {
        using AppSession app = AppSession.Configured().Start();
        string target = Path.Combine(app.TestData, "OtherLoader");
        Directory.CreateDirectory(target);
        string launcher = Path.Combine(app.TestData, "launcher.exe");
        File.WriteAllBytes(launcher, []); // never run: the test only picks it
        app.Expand("DashboardPathsExpander");
        FileDialogs.Choose(app, app.Find("PickTargetButton"), target);
        WaitUntil(() => Decoded(app, "target_dir") == target, "the new target saved in config.ini");
        FileDialogs.Choose(app, app.Find("PickLauncherButton"), launcher);
        WaitUntil(() => Decoded(app, "launcher_path") == launcher, "the launcher saved in config.ini");
        Assert.Equal(target, app.Find("TargetTextBox").Patterns.Value.Pattern.Value.Value);
        Assert.Equal(launcher, app.Find("LauncherTextBox").Patterns.Value.Pattern.Value.Value);
        app.AssertNoErrors();
    }

    // The dashboard's link switch is saved with the repository, whether or not the workspace was opened.
    [Fact]
    public void Dashboard_TheLinkSwitchIsSavedWithTheRepository()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["Repositories"]![0]!["UseDirectoryLinks"] = true).Start();
        app.Expand("DashboardPathsExpander");
        AutomationElement toggle = app.Find("LinkDeploymentToggleSwitch");
        AppSession.ScrollIntoView(toggle);
        toggle.Patterns.Toggle.Pattern.Toggle();
        app.WaitForSaved(saved => (bool?)saved["Repositories"]![0]!["UseDirectoryLinks"] == false, "copy mode saved with the repository");
        app.AssertNoErrors();
    }

    // A character on the dashboard opens the workspace on that character, with its Mods listed.
    [Fact]
    public void Dashboard_ACharacterOpensTheWorkspaceOnIt()
    {
        using AppSession app = AppSession.Configured().Start();
        AutomationElement grid = app.Find("DashboardCharactersGridView", TimeSpan.FromSeconds(10));
        AutomationElement chip = AppSession.FindIn(grid, cf => cf.ByControlType(ControlType.ListItem), "the TestCharacter chip");
        AppSession.ScrollIntoView(chip);
        chip.Click();
        app.FindByName("TestModA", TimeSpan.FromSeconds(10));
        app.FindByName("TestModB");
        Assert.True(app.Find("FirstLevelListView").FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)).Patterns.SelectionItem.Pattern.IsSelected.Value);
        app.AssertNoErrors();
    }
}
