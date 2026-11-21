using System.Diagnostics;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// Combination presets on the synthetic test data, written before stage J finish step 7 moves the combination code out
// of the window, to pin what saving, updating, deleting, restoring, bundles and shared presets write. The sample Mods
// are the manager's copies (OverlaySamples); only the test's own copy and a temporary export folder change.
public sealed class CombinationPresetTests
{
    private static string UserIni(AppSession app) => Path.Combine(app.TestData, "Loader", "d3dx_user.ini");
    private static string Deployed(AppSession app, string mod) => Path.Combine(app.TestData, "Loader", "Mods", mod);
    private static string Source(AppSession app, string mod) => Path.Combine(app.TestData, "ModRepository", "TestCharacter", mod);

    private static string Value(AppSession app, string mod, string variable)
    {
        string prefix = $@"$\Mods\{mod}\mod.ini\{variable} = ";
        return File.ReadAllLines(UserIni(app)).Single(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))[prefix.Length..].Trim();
    }

    private static JsonObject StateValue(string mod, string variable, string value) => new()
    {
        ["RelativeIniPath"] = "mod.ini", ["Variable"] = "$" + variable, ["RuntimeKey"] = $@"$\Mods\{mod}\mod.ini\{variable}", ["Value"] = value
    };

    private static JsonObject Profile(string id, string name, string[] mods, params (string Mod, JsonObject[] Values)[] states) => new()
    {
        ["Id"] = id, ["RepositoryId"] = "ui-repository", ["Name"] = name,
        ["ModRelativePaths"] = new JsonArray([.. mods.Select(mod => (JsonNode)(@"TestCharacter\" + mod))]),
        ["IncludesPersistentState"] = states.Length > 0,
        ["PersistentStates"] = new JsonArray([.. states.Select(state => (JsonNode)new JsonObject
        {
            ["ModRelativePath"] = @"TestCharacter\" + state.Mod, ["Values"] = new JsonArray([.. state.Values])
        })])
    };

    // Copy mode with the sample Mods recorded as the manager's copies, opened on the workspace's preset view.
    private static AppSession StartOnPresets(Action<JsonObject>? configure = null, Action<AppSession>? beforeStart = null)
    {
        AppSession app = AppSession.Configured((shell, session) =>
        {
            OverlaySamples.Apply(shell, session);
            shell["Repositories"]![0]!["UseDirectoryLinks"] = false;
            shell["CurrentPrimarySection"] = "repository";
            shell["RepositoryWorkspaceView"] = "presets";
            configure?.Invoke(shell);
        });
        try
        {
            beforeStart?.Invoke(app);
            app.Start();
            app.Resize(1600, 1000);
            app.Find("CreateConfigurationProfileButton", TimeSpan.FromSeconds(15));
            return app;
        }
        catch
        {
            // A session that never reached the test's "using" would keep the overlay hotkey for the next tests.
            app.Dispose();
            throw;
        }
    }

    private static JsonArray Profiles(AppSession app) => app.ReadSaved()["ConfigurationProfiles"]?.AsArray() ?? [];

    private static JsonNode? ProfileNamed(AppSession app, string name)
        => Profiles(app).FirstOrDefault(profile => (string?)profile!["Name"] == name);

    private static string[] ModsOf(JsonNode profile) => [.. profile["ModRelativePaths"]!.AsArray().Select(path => (string)path!).Order()];

    private static string[] ValuesOf(JsonNode profile) => [.. profile["PersistentStates"]!.AsArray()
        .SelectMany(state => state!["Values"]!.AsArray().Select(value => $"{state["ModRelativePath"]}:{value!["Variable"]}={value["Value"]}")).Order()];

    private static void WaitUntil(Func<bool> condition, string what, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    private static void ChooseProfile(AppSession app, string name)
    {
        ComboBox combo = app.Find("ConfigurationProfileComboBox").AsComboBox();
        combo.Expand();
        Thread.Sleep(400);
        combo.Items.First(item => item.Name.StartsWith(name + "  ·", StringComparison.Ordinal)).Patterns.SelectionItem.Pattern.Select();
        combo.Collapse();
        Thread.Sleep(500);
    }

    // config.ini keeps bindings and links by the Mod's full path (fields Base64, separated by tabs).
    private static string Fields(params string[] values) => string.Join('\t', values.Select(value => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))));

    private static string[] Decoded(AppSession app, string key) => [.. File.ReadAllLines(Path.Combine(app.AppFolder, "config.ini"))
        .Where(line => line.StartsWith(key, StringComparison.Ordinal))
        .Select(line => string.Join("|", line[key.Length..].Split('\t').Select(field => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(field))))).Order()];

    private static string ExportFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "imm-ui-export-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void Save_CapturesTheEnabledModsAndTheirValues()
    {
        using AppSession app = StartOnPresets();
        var state = app.Find("CaptureCombinationStateCheckBox").Patterns.Toggle.Pattern;
        if (state.ToggleState.Value != ToggleState.On) state.Toggle();
        AppSession.Invoke(app.Find("CreateConfigurationProfileButton"));
        app.Find("PromptTextBox", TimeSpan.FromSeconds(10)).Patterns.Value.Pattern.SetValue("Pinned set");
        AppSession.Invoke(app.Find("PrimaryButton"));

        WaitUntil(() => ProfileNamed(app, "Pinned set") is not null, "the preset saved");
        JsonNode profile = ProfileNamed(app, "Pinned set")!;
        Assert.Equal([@"TestCharacter\TestModA", @"TestCharacter\TestModB"], ModsOf(profile));
        Assert.True((bool)profile["IncludesPersistentState"]!);
        // Each Mod's "global persist" variables with the values d3dx_user.ini has now.
        Assert.Equal([@"TestCharacter\TestModA:$coat=0", @"TestCharacter\TestModA:$hair=1", @"TestCharacter\TestModB:$style=2"], ValuesOf(profile));
        Assert.Equal((string?)profile["Id"], (string?)app.ReadSaved()["SelectedConfigurationProfileId"]);
        Assert.Equal(UserIni(app), (string?)app.ReadSaved()["Repositories"]![0]!["PersistentUserIniPath"]);
        app.AssertNoErrors();
    }

    [Fact]
    public void UpdateAndDelete_ChangeOnlyTheSelectedPreset()
    {
        using AppSession app = StartOnPresets(shell =>
        {
            shell["ConfigurationProfiles"] = new JsonArray(
                Profile("keep", "Keep", ["TestModB"]),
                Profile("replace", "Replace", ["TestModB"]));
            shell["SelectedConfigurationProfileId"] = "replace";
        });
        // Update: the selected preset now holds both enabled Mods.
        AppSession.Invoke(app.Find("UpdateConfigurationProfileButton"));
        AppSession.Invoke(app.Find("PrimaryButton", TimeSpan.FromSeconds(10)));
        WaitUntil(() => ProfileNamed(app, "Replace") is { } saved && ModsOf(saved).Length == 2, "the preset replaced");
        Assert.Equal("replace", (string?)ProfileNamed(app, "Replace")!["Id"]);
        Assert.Equal([@"TestCharacter\TestModB"], ModsOf(ProfileNamed(app, "Keep")!));

        // Delete: the files stay, only the preset goes.
        AppSession.Invoke(app.Find("DeleteConfigurationProfileButton"));
        AppSession.Invoke(app.Find("PrimaryButton", TimeSpan.FromSeconds(10)));
        WaitUntil(() => ProfileNamed(app, "Replace") is null, "the preset deleted");
        Assert.NotNull(ProfileNamed(app, "Keep"));
        Assert.True(Directory.Exists(Deployed(app, "TestModA")));
        Assert.True(Directory.Exists(Deployed(app, "TestModB")));
        app.AssertNoErrors();
    }

    // Restoring deploys exactly the preset's Mods, writes its values, and leaves the shortcut bindings and links of
    // every Mod (deployed or not) where they were: they follow the Mod, not the combination.
    [Fact]
    public void Restore_DeploysThePresetWritesItsValuesAndKeepsBindingsAndLinks()
    {
        using AppSession app = StartOnPresets(shell =>
        {
            shell["ConfigurationProfiles"] = new JsonArray(Profile("only-b", "Only B", ["TestModB"], ("TestModB", [StateValue("TestModB", "style", "5")])));
            shell["SelectedConfigurationProfileId"] = "only-b";
        }, app => File.AppendAllLines(Path.Combine(app.AppFolder, "config.ini"),
        [
            "binding=" + Fields(Source(app, "TestModA"), "Ctrl+H", "Hair"),
            "binding=" + Fields(Source(app, "TestModB"), "Ctrl+S", "Style"),
            "mod_link=" + Fields(Source(app, "TestModA"), "https://example.invalid/mods/a"),
            "mod_link=" + Fields(Source(app, "TestModB"), "https://example.invalid/mods/b")
        ]));
        AppSession.Invoke(app.Find("ApplyConfigurationProfileButton"));
        app.FindByName(app.Text("Common.IHaveFullyClosedTheGame"), TimeSpan.FromSeconds(15)).Patterns.Toggle.Pattern.Toggle();
        AppSession.Invoke(app.FindByName(app.Text("CombinationPresets.BackUpAndRestore")));
        app.FindByName(app.Text("CombinationPresets.CombinationRestored"), TimeSpan.FromSeconds(30));
        AppSession.Invoke(app.Find("CloseButton"));

        Assert.False(Directory.Exists(Deployed(app, "TestModA")), "A Mod outside the preset is removed");
        Assert.True(File.Exists(Path.Combine(Deployed(app, "TestModB"), "mod.ini")));
        Assert.Equal("5", Value(app, "TestModB", "style"));
        Assert.Equal("1", Value(app, "TestModA", "hair"));
        Assert.Equal("88", Value(app, "Unrelated", "hair"));
        // The removed copy's record goes; the kept one stays.
        WaitUntil(() => app.ReadSaved()["CopyDeployments"]!.AsArray().Count == 1, "one copy record left");
        Assert.Equal(@"TestCharacter\TestModB", (string?)app.ReadSaved()["CopyDeployments"]![0]!["SourceRelativePath"]);
        Assert.Equal([Source(app, "TestModA") + "|Ctrl+H|Hair", Source(app, "TestModB") + "|Ctrl+S|Style"], Decoded(app, "binding="));
        Assert.Equal([Source(app, "TestModA") + "|https://example.invalid/mods/a", Source(app, "TestModB") + "|https://example.invalid/mods/b"], Decoded(app, "mod_link="));
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }

    [Fact]
    public void Bundle_ExportThenInstallSavesTheBundleAsANewPreset()
    {
        string folder = ExportFolder();
        try
        {
            using AppSession app = StartOnPresets(shell =>
            {
                shell["ConfigurationProfiles"] = new JsonArray(Profile("both", "Both", ["TestModA", "TestModB"],
                    ("TestModA", [StateValue("TestModA", "hair", "2")]), ("TestModB", [StateValue("TestModB", "style", "3")])));
                shell["SelectedConfigurationProfileId"] = "both";
            });
            AppSession.Invoke(app.FindByName(app.Text("CombinationBundles.ExportBundle")));
            FileDialogs.Choose(app, app.Find("PrimaryButton", TimeSpan.FromSeconds(10)), folder);
            app.FindByName(app.Text("CombinationBundles.ExportComplete"), TimeSpan.FromSeconds(30));
            AppSession.Invoke(app.Find("CloseButton"));
            string bundle = Assert.Single(Directory.GetFiles(folder, "*.imm-preset.zip"));

            FileDialogs.Choose(app, app.FindByName(app.Text("CombinationBundles.InstallBundle")), bundle);
            AppSession.Invoke(app.FindByName(app.Text("CombinationBundles.InstallAndSavePreset"), TimeSpan.FromSeconds(30)));

            // The same name is taken: the installed preset gets the next free one, with the bundle's Mods and values.
            WaitUntil(() => ProfileNamed(app, "Both (2)") is not null, "the bundle's preset saved", 30);
            JsonNode installed = ProfileNamed(app, "Both (2)")!;
            Assert.Equal([@"TestCharacter\TestModA", @"TestCharacter\TestModB"], ModsOf(installed));
            Assert.Equal([@"TestCharacter\TestModA:$hair=2", @"TestCharacter\TestModB:$style=3"], ValuesOf(installed));
            Assert.Equal((string?)installed["Id"], (string?)app.ReadSaved()["SelectedConfigurationProfileId"]);
            Assert.Equal("1", Value(app, "TestModA", "hair"));
            Assert.True(File.Exists(Path.Combine(Source(app, "TestModA"), "mod.ini")));
            app.AssertNoErrors();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Share_ExportThenImportAddsAPresetAndAStateSlotPerMod()
    {
        string folder = ExportFolder();
        try
        {
            using AppSession app = StartOnPresets(shell =>
            {
                shell["ConfigurationProfiles"] = new JsonArray(Profile("look", "Look", ["TestModA", "TestModB"],
                    ("TestModA", [StateValue("TestModA", "hair", "2")]), ("TestModB", [StateValue("TestModB", "style", "3")])));
                shell["SelectedConfigurationProfileId"] = "look";
            });
            FileDialogs.Choose(app, app.Find("PresetShareExportButton"), folder);
            string shared = Path.Combine(folder, "Look.imm-share.json");
            WaitUntil(() => File.Exists(shared), "the shared preset written");

            FileDialogs.Choose(app, app.Find("PresetShareImportButton"), shared);
            AppSession.Invoke(app.FindByName(app.Text("PresetShare.ImportMatched"), TimeSpan.FromSeconds(15)));

            WaitUntil(() => ProfileNamed(app, "Look (2)") is not null, "the shared preset imported");
            JsonNode imported = ProfileNamed(app, "Look (2)")!;
            Assert.Equal([@"TestCharacter\TestModA", @"TestCharacter\TestModB"], ModsOf(imported));
            Assert.Equal([@"TestCharacter\TestModA:$hair=2", @"TestCharacter\TestModB:$style=3"], ValuesOf(imported));
            // A state slot per Mod under the preset's name, so each look can be restored on its own.
            JsonNode[] slots = [.. app.ReadSaved()["ModPersistentSlots"]!.AsArray().Where(slot => (string?)slot!["Name"] == "Look (2)").Select(slot => slot!)];
            Assert.Equal([@"TestCharacter\TestModA", @"TestCharacter\TestModB"], slots.Select(slot => (string)slot["ModRelativePath"]!).Order().ToArray());
            // Importing writes nothing to the loader.
            Assert.Equal("1", Value(app, "TestModA", "hair"));
            Assert.Equal("2", Value(app, "TestModB", "style"));
            app.AssertNoErrors();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    // The overlay saves the enabled combination; without a game window to send the reload key to, it asks to read the
    // parameter file as it is.
    [Fact]
    public void Overlay_SavesTheEnabledCombinationFromTheFileOnDisk()
    {
        using AppSession app = AppSession.Configured((shell, session) =>
        {
            OverlaySamples.Apply(shell, session);
            shell["Repositories"]![0]!["UseDirectoryLinks"] = false;
        }).Start();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.F9);
        Window? overlay = null;
        WaitUntil(() => (overlay = app.FindWindow(window => window.Title.StartsWith("IMM ", StringComparison.Ordinal))) is not null, "the overlay");
        Thread.Sleep(1200);
        AutomationElement save = AppSession.FindIn(overlay!, cf => cf.ByAutomationId("OverlaySavePresetButton"), "save preset");
        save.Patterns.ExpandCollapse.Pattern.Expand();
        AppSession.Invoke(AppSession.FindIn(overlay!, cf => cf.ByName(app.Text("Overlay.SaveCombinationPreset")), "save combination", TimeSpan.FromSeconds(5)));
        AppSession.Invoke(AppSession.FindIn(overlay!, cf => cf.ByName(app.Text("ShortcutCapture.ReadExisting")), "read the existing file", TimeSpan.FromSeconds(10)));

        WaitUntil(() => Profiles(app).Count == 1, "the overlay's preset saved");
        JsonNode profile = Profiles(app)[0]!;
        Assert.Equal([@"TestCharacter\TestModA", @"TestCharacter\TestModB"], ModsOf(profile));
        Assert.True((bool)profile["IncludesPersistentState"]!);
        Assert.Equal([@"TestCharacter\TestModA:$coat=0", @"TestCharacter\TestModA:$hair=1", @"TestCharacter\TestModB:$style=2"], ValuesOf(profile));
        app.AssertNoErrors();
    }
}
