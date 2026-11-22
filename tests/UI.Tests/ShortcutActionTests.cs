using System.Diagnostics;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// What the overlay's and the Shortcuts page's actions do, written before stage J finish step 8b moves the shortcut and
// overlay code out of the window: sending a binding, saving presets from the overlay, the slot picture gallery,
// character follow without a region, and reading a Mod's bindings from its files. Only the test's own copy changes;
// keys, the reload key (F10) included, go to TestTargetWindow only.
public sealed class ShortcutActionTests
{
    private static string UserIni(AppSession app) => Path.Combine(app.TestData, "Loader", "d3dx_user.ini");

    private static void WaitUntil(Func<bool> condition, string what, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    // TestModA's slots with the "$" capturing saves (as StateSlotTests.ValidSlots).
    private static void ValidSlots(JsonObject shell, AppSession app)
    {
        OverlaySamples.Apply(shell, app);
        foreach (JsonNode? slot in shell["ModPersistentSlots"]!.AsArray())
            foreach (JsonNode? value in slot!["Values"]!.AsArray())
                value!["Variable"] = "$" + (string?)value["Variable"];
    }

    private static Window OpenOverlay(AppSession app)
    {
        app.MainWindow.SetForeground();
        Thread.Sleep(300);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.F9);
        Window? overlay = null;
        WaitUntil(() => (overlay = app.FindWindow(window => window.Title.StartsWith("IMM ", StringComparison.Ordinal))) is not null, "the overlay");
        Thread.Sleep(1200);
        return overlay!;
    }

    // A result is behind the status icon; a failure is in the notice above the save button.
    private static string StatusText(Window overlay)
    {
        if (overlay.FindFirstDescendant(cf => cf.ByAutomationId("OverlayStatusBar")) is { IsOffscreen: false } bar)
            return "error: " + string.Join(" ", bar.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(text => text.Properties.Name.ValueOrDefault));
        return overlay.FindFirstDescendant(cf => cf.ByAutomationId("OverlayStatusButton"))?.HelpText ?? string.Empty;
    }

    private static void ArmOnTestWindow(Window overlay)
    {
        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayRefreshTargetsButton"), "refresh windows"));
        ComboBox picker = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayTargetPicker"), "target picker").AsComboBox();
        picker.Expand();
        Thread.Sleep(500);
        picker.Items.First(entry => entry.Name.Contains(TestTargetWindow.Title, StringComparison.Ordinal)).Patterns.SelectionItem.Pattern.Select();
        picker.Collapse();
        AutomationElement send = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlaySendSwitch"), "send switch");
        send.Patterns.Toggle.Pattern.Toggle();
        WaitUntil(() => send.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On, "sending on");
    }

    private static void ChooseSaveMenuItem(AppSession app, Window overlay, string textKey)
    {
        AutomationElement save = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlaySavePresetButton"), "save preset button");
        save.Patterns.ExpandCollapse.Pattern.Expand();
        Thread.Sleep(500);
        AutomationElement item = AppSession.FindIn(overlay, cf => cf.ByName(app.Text(textKey)).And(cf.ByControlType(ControlType.MenuItem)), textKey);
        AppSession.Invoke(item);
    }

    // The test window writes nothing after F10, so the overlay asks whether to save from d3dx_user.ini as it is.
    private static void ReadTheExistingFile(AppSession app, Window overlay)
    {
        AutomationElement? read = null;
        WaitUntil(() => (read = overlay.FindFirstDescendant(cf => cf.ByName(app.Text("ShortcutCapture.ReadExisting")).And(cf.ByControlType(ControlType.Button)))) is not null,
            "the read-existing question", 20);
        AppSession.Invoke(read!);
    }

    [Fact]
    public void Overlay_ABindingIsSentToTheChosenWindow()
    {
        using var target = TestTargetWindow.Start();
        using AppSession app = AppSession.Configured(OverlaySamples.Apply).Start();
        Window overlay = OpenOverlay(app);
        ArmOnTestWindow(overlay);
        AutomationElement hair = AppSession.FindIn(overlay, cf => cf.ByControlType(ControlType.Button).And(cf.ByName("H", FlaUI.Core.Definitions.PropertyConditionFlags.MatchSubstring)), "the H binding");
        Assert.StartsWith("H · ", hair.Name, StringComparison.Ordinal);
        string before = File.ReadAllText(UserIni(app));

        AppSession.Invoke(hair);

        string seen = string.Empty;
        WaitUntil(() => (seen = StatusText(overlay)).StartsWith("H · ", StringComparison.Ordinal), "the send report");
        Assert.DoesNotContain("error:", seen, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(UserIni(app)));
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_SingleModPresetIsSavedFromTheFileWhenTheLoaderWritesNothing()
    {
        using var target = TestTargetWindow.Start();
        using AppSession app = AppSession.Configured(ValidSlots).Start();
        Window overlay = OpenOverlay(app);
        ArmOnTestWindow(overlay);

        ChooseSaveMenuItem(app, overlay, "ShortcutPanel.SaveSingleModPreset");
        AutomationElement? name = null;
        WaitUntil(() => (name = overlay.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))) is not null, "the preset name box");
        name!.Patterns.Value.Pattern.SetValue("Overlay pinned");
        AutomationElement saveSlot = AppSession.FindIn(overlay, cf => cf.ByName(app.Text("PersistentSlots.SaveSlot")).And(cf.ByControlType(ControlType.Button)), "save slot");
        WaitUntil(() => saveSlot.IsEnabled, "the save button enabled");
        AppSession.Invoke(saveSlot);
        ReadTheExistingFile(app, overlay);

        JsonNode saved = app.WaitForSaved(shell => shell["ModPersistentSlots"]!.AsArray().Any(slot => (string?)slot!["Name"] == "Overlay pinned"), "the overlay's slot saved");
        JsonNode slot = saved["ModPersistentSlots"]!.AsArray().First(item => (string?)item!["Name"] == "Overlay pinned")!;
        Assert.Equal(@"TestCharacter\TestModA", (string?)slot["ModRelativePath"]);
        Assert.Equal(["$coat=0", "$hair=1"], slot["Values"]!.AsArray().Select(value => $"{value!["Variable"]}={value["Value"]}").Order().ToArray());
        WaitUntil(() => !File.GetAttributes(UserIni(app)).HasFlag(FileAttributes.ReadOnly), "d3dx_user.ini writable", 20);
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_CombinationPresetIsSavedFromTheFileWhenTheLoaderWritesNothing()
    {
        using var target = TestTargetWindow.Start();
        using AppSession app = AppSession.Configured(ValidSlots).Start();
        int before = app.ReadSaved()["ConfigurationProfiles"]?.AsArray().Count ?? 0;
        Window overlay = OpenOverlay(app);
        ArmOnTestWindow(overlay);

        ChooseSaveMenuItem(app, overlay, "Overlay.SaveCombinationPreset");
        ReadTheExistingFile(app, overlay);

        JsonNode saved = app.WaitForSaved(shell => (shell["ConfigurationProfiles"]?.AsArray().Count ?? 0) == before + 1, "the combination preset saved");
        JsonNode profile = saved["ConfigurationProfiles"]!.AsArray().Last()!;
        Assert.Equal((string?)profile["Id"], (string?)saved["SelectedConfigurationProfileId"]);
        WaitUntil(() => !File.GetAttributes(UserIni(app)).HasFlag(FileAttributes.ReadOnly), "d3dx_user.ini writable", 20);
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_TheSlotGalleryShowsEverySlotAndChoosingOneWithoutSendingWritesNothing()
    {
        using AppSession app = AppSession.Configured(ValidSlots).Start();
        string before = File.ReadAllText(UserIni(app));
        Window overlay = OpenOverlay(app);
        string noPicture = app.Text("PresetPreview.NoPicture");

        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByName(app.Text("PresetPreview.PickByPicture")).And(cf.ByControlType(ControlType.Button)), "pick by picture"));
        // The automatic Default slot and the three saved ones.
        WaitUntil(() => overlay.FindAllDescendants(cf => cf.ByName(noPicture)).Length == 4, "four slot cards without pictures");
        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByName(app.Text("Common.Cancel")).And(cf.ByControlType(ControlType.Button)), "cancel"));
        WaitUntil(() => overlay.FindAllDescendants(cf => cf.ByName(noPicture)).Length == 0, "the gallery closed");

        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByName(app.Text("PresetPreview.PickByPicture")).And(cf.ByControlType(ControlType.Button)), "pick by picture"));
        AutomationElement? casual = null;
        WaitUntil(() => (casual = overlay.FindAllDescendants(cf => cf.ByName("Casual").And(cf.ByControlType(ControlType.Button)))
            .FirstOrDefault(button => button.FindFirstDescendant(cf => cf.ByName(noPicture)) is not null)) is not null, "the Casual card");
        AppSession.Invoke(casual!);

        WaitUntil(() => StatusText(overlay) == app.Text("HotReload.NotArmed"), "the not-armed hint");
        Assert.Equal(before, File.ReadAllText(UserIni(app)));
        Assert.Null(app.ReadSaved()["ModPersistentSlots"]!.AsArray().First(slot => (string?)slot!["Name"] == "Casual")!["ActivatedAtUtc"]);
        app.AssertNoErrors();
    }

    [Fact]
    public void Shortcuts_CharacterFollowWithoutARegionTurnsItselfOff()
    {
        using AppSession app = AppSession.Configured(OverlaySamples.Apply).Start();
        AppSession.Invoke(app.Find("ShortcutsNavButton"));
        app.FindByName(string.Format(app.Text("ShortcutPanel.ReadBindingsInGameConditionsAre"), 2), TimeSpan.FromSeconds(15));
        app.Expand("ShortcutsFollowExpander");
        AutomationElement toggle = app.Find("ShortcutCharacterFollowToggle", TimeSpan.FromSeconds(10));
        AppSession.ScrollIntoView(toggle);
        toggle.Patterns.Toggle.Pattern.Toggle();

        app.FindByName(app.Text("CharacterFollow.SelectARegionAndSaveReferences"), TimeSpan.FromSeconds(10));
        WaitUntil(() => toggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off, "the switch back off");
        app.AssertNoErrors();
    }

    [Fact]
    public void Workspace_SelectingAModWithoutBindingsReadsThemFromItsFiles()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();
        string source = Path.Combine(app.TestData, "ModRepository", "TestCharacter", "TestModA");
        string prefix = "binding=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(source)) + "\t";
        string config = Path.Combine(app.AppFolder, "config.ini");
        Assert.DoesNotContain(File.ReadAllLines(config), line => line.StartsWith(prefix, StringComparison.Ordinal));

        app.Resize(1600, 1000);
        AppSession.Invoke(app.FindByName("TestCharacter"));
        // TestModB has no key sections; choosing it first makes choosing TestModA a change of selection.
        foreach (string name in new[] { "TestModB", "TestModA" })
        {
            AutomationElement? mod = app.FindByName(name, TimeSpan.FromSeconds(10));
            while (mod is not null && mod.ControlType != ControlType.ListItem) mod = mod.Parent;
            Assert.NotNull(mod);
            mod.Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(800);
        }

        WaitUntil(() =>
        {
            try { return File.ReadAllLines(config).Count(line => line.StartsWith(prefix, StringComparison.Ordinal)) == 2; }
            catch (IOException) { return false; }
        }, "TestModA's two bindings in config.ini");
        app.AssertNoErrors();
    }
}
