using System.Diagnostics;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// State slots on the synthetic test data (OverlaySamples: TestModA's Battle, Casual and Everyday slots), written before
// stage J finish step 6 moves the slot code out of the window, to pin what restoring, capturing and switching from the
// overlay write. Only the test's own d3dx_user.ini changes; the reload key (F10) goes to TestTargetWindow only.
public sealed class StateSlotTests
{
    private static string UserIni(AppSession app) => Path.Combine(app.TestData, "Loader", "d3dx_user.ini");

    private static string Value(AppSession app, string mod, string variable)
    {
        string prefix = $@"$\Mods\{mod}\mod.ini\{variable} = ";
        return File.ReadAllLines(UserIni(app)).Single(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))[prefix.Length..].Trim();
    }

    private static void AssertOthersUntouched(AppSession app)
    {
        // Not part of TestModA's slots: never written.
        Assert.Equal("55", Value(app, "TestModA", "temporary"));
        Assert.Equal("2", Value(app, "TestModB", "style"));
        Assert.Equal("88", Value(app, "Unrelated", "hair"));
    }

    // TestModA's slots as capturing saves them: the variable with its "$". (OverlaySamples' slots leave the "$" out,
    // which the loader-state engine refuses; Workspace_AnUnsupportedSlotIsRefusedAndNothingIsWritten pins that.)
    private static void ValidSlots(JsonObject shell, AppSession app)
    {
        OverlaySamples.Apply(shell, app);
        foreach (JsonNode? slot in shell["ModPersistentSlots"]!.AsArray())
            foreach (JsonNode? value in slot!["Values"]!.AsArray())
                value!["Variable"] = "$" + (string?)value["Variable"];
    }

    private static int SlotCount(AppSession app, string mod)
        => app.ReadSaved()["ModPersistentSlots"]!.AsArray().Count(slot => (string?)slot!["ModRelativePath"] == @"TestCharacter\" + mod);

    private static JsonNode? Slot(AppSession app, string name)
        => app.ReadSaved()["ModPersistentSlots"]!.AsArray().FirstOrDefault(slot => (string?)slot!["Name"] == name);

    private static void WaitUntil(Func<bool> condition, string what, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what + LastSeen());
            Thread.Sleep(100);
        }
    }

    // What the last overlay check read, for a timeout's message.
    [ThreadStatic] private static string? _lastSeen;

    private static string LastSeen() => _lastSeen is null ? string.Empty : " (last seen: " + _lastSeen + ")";

    private static AppSession StartOnSlots(bool validSlots = true)
    {
        AppSession app = AppSession.Configured((shell, session) =>
        {
            if (validSlots) ValidSlots(shell, session);
            else OverlaySamples.Apply(shell, session);
            shell["CurrentPrimarySection"] = "repository";
        }).Start();
        try
        {
            app.Resize(1600, 1000);
            AppSession.Invoke(app.FindByName("TestCharacter"));
            // The list item holds the Mod's name as text.
            AutomationElement? mod = app.FindByName("TestModA", TimeSpan.FromSeconds(10));
            while (mod is not null && mod.ControlType != ControlType.ListItem) mod = mod.Parent;
            Assert.NotNull(mod);
            mod.Patterns.SelectionItem.Pattern.Select();
            app.Find("PersistentSlotsTab", TimeSpan.FromSeconds(10)).Patterns.SelectionItem.Pattern.Select();
            WaitUntil(() => app.Find("PersistentSlotStatusTextBlock").Name.Contains("TestModA", StringComparison.Ordinal), "TestModA's slots");
            return app;
        }
        catch
        {
            // A session that never reached the test's "using" would keep the overlay hotkey for the next tests.
            app.Dispose();
            throw;
        }
    }

    private static void ChooseSlot(AppSession app, string name)
    {
        ComboBox combo = app.Find("PersistentSlotComboBox").AsComboBox();
        combo.Expand();
        Thread.Sleep(400);
        combo.Items.First(item => item.Name.StartsWith(name + " ·", StringComparison.Ordinal)).Patterns.SelectionItem.Pattern.Select();
        combo.Collapse();
        Thread.Sleep(500);
    }

    // Renaming and updating the selected slot are buttons beside the slot list (once only under "More…").
    [Fact]
    public void Workspace_TheSelectedSlotIsRenamedWithTheButtonBesideTheList()
    {
        using AppSession app = StartOnSlots();
        ChooseSlot(app, "Battle");
        Assert.True(app.Find("UpdatePersistentSlotButton").IsEnabled);
        Assert.Equal(app.Text("PersistentVariants.UpdateSelectedSlot"), app.Find("UpdatePersistentSlotButton").Name);

        AppSession.Invoke(app.Find("RenamePersistentSlotButton"));
        app.Find("PromptTextBox", TimeSpan.FromSeconds(10)).Patterns.Value.Pattern.SetValue("Battle renamed");
        AppSession.Invoke(app.FindByName(app.Text("Dialogs.Rename")));
        WaitUntil(() => Slot(app, "Battle renamed") is not null, "the slot renamed");
        Assert.Null(Slot(app, "Battle"));
        app.AssertNoErrors();
    }

    [Fact]
    public void Workspace_TheSlotListHasTheDefaultAndSavedSlots()
    {
        using AppSession app = StartOnSlots();
        ComboBox combo = app.Find("PersistentSlotComboBox").AsComboBox();
        combo.Expand();
        Thread.Sleep(400);
        string[] items = combo.Items.Select(item => item.Name).ToArray();
        combo.Collapse();
        // The Default slot is seeded from the Mod's own values; the active slot is marked.
        Assert.Equal(4, items.Length);
        Assert.StartsWith(app.Text("PersistentSlots.DefaultSlotName") + " ·", items[0]);
        Assert.Contains(items, item => item.StartsWith("Everyday ·", StringComparison.Ordinal) && item.EndsWith(" · 当前", StringComparison.Ordinal));
        // Each deployed Mod gets its Default slot: TestModA's three plus its Default, and TestModB's Default.
        Assert.Equal(4, SlotCount(app, "TestModA"));
        Assert.Equal(1, SlotCount(app, "TestModB"));
        app.AssertNoErrors();
    }

    [Fact]
    public void Workspace_RestoreWritesOnlyTheSlotsValuesAndMarksItCurrent()
    {
        using AppSession app = StartOnSlots();
        ChooseSlot(app, "Battle");
        AppSession.Invoke(app.Find("RestorePersistentRuntimeButton"));
        app.FindByName(app.Text("Common.IHaveFullyClosedTheGame"), TimeSpan.FromSeconds(10)).Patterns.Toggle.Pattern.Toggle();
        AppSession.Invoke(app.Find("PrimaryButton"));

        WaitUntil(() => Value(app, "TestModA", "hair") == "2", "hair written");
        Assert.Equal("1", Value(app, "TestModA", "coat"));
        AssertOthersUntouched(app);
        WaitUntil(() => Slot(app, "Battle")?["ActivatedAtUtc"] is not null, "Battle marked as the current slot");
        Assert.False(File.GetAttributes(UserIni(app)).HasFlag(FileAttributes.ReadOnly));
        app.AssertNoErrors();
    }

    [Fact]
    public void Workspace_AnUnsupportedSlotIsRefusedAndNothingIsWritten()
    {
        using AppSession app = StartOnSlots(validSlots: false);
        string before = File.ReadAllText(UserIni(app));
        ChooseSlot(app, "Battle");
        AppSession.Invoke(app.Find("RestorePersistentRuntimeButton"));
        app.FindByName(app.Text("PersistentSlots.RestoreFailedCheckBackups"), TimeSpan.FromSeconds(10));
        AppSession.Invoke(app.Find("CloseButton"));

        Assert.Equal(before, File.ReadAllText(UserIni(app)));
        Assert.Null(Slot(app, "Battle")!["ActivatedAtUtc"]);
        app.AssertNoErrors();
    }

    [Fact]
    public void Workspace_CaptureSavesTheDeclaredPersistentValues()
    {
        using AppSession app = StartOnSlots();
        AppSession.Invoke(app.Find("CapturePersistentSlotButton"));
        app.Find("PromptTextBox", TimeSpan.FromSeconds(10)).Patterns.Value.Pattern.SetValue("Pinned");
        AppSession.Invoke(app.Find("PrimaryButton"));

        WaitUntil(() => Slot(app, "Pinned") is not null, "the new slot saved");
        JsonArray values = Slot(app, "Pinned")!["Values"]!.AsArray();
        // Only the Mod's "global persist" variables, with the values d3dx_user.ini has now.
        Assert.Equal(["$coat=0", "$hair=1"], values.Select(value => $"{value!["Variable"]}={value["Value"]}").Order().ToArray());
        Assert.Equal(5, SlotCount(app, "TestModA"));
        AssertOthersUntouched(app);
        app.AssertNoErrors();
    }

    private static Window OpenOverlay(AppSession app)
    {
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

    [Fact]
    public void Overlay_ASlotButtonWithoutSendingChangesNothing()
    {
        using AppSession app = AppSession.Configured(OverlaySamples.Apply).Start();
        string before = File.ReadAllText(UserIni(app));
        Window overlay = OpenOverlay(app);
        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByName("Battle").And(cf.ByControlType(ControlType.Button)), "Battle slot"));

        WaitUntil(() => StatusText(overlay) == app.Text("HotReload.NotArmed"), "the not-armed hint");
        Assert.Equal(before, File.ReadAllText(UserIni(app)));
        Assert.Null(Slot(app, "Battle")!["ActivatedAtUtc"]);
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_ASlotButtonWritesTheSlotAndSendsTheReloadKey()
    {
        using var target = TestTargetWindow.Start();
        using AppSession app = AppSession.Configured(ValidSlots).Start();
        Window overlay = OpenOverlay(app);
        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayRefreshTargetsButton"), "refresh windows"));
        ComboBox picker = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayTargetPicker"), "target picker").AsComboBox();
        picker.Expand();
        Thread.Sleep(500);
        picker.Items.First(entry => entry.Name.Contains(TestTargetWindow.Title, StringComparison.Ordinal)).Patterns.SelectionItem.Pattern.Select();
        picker.Collapse();
        AutomationElement send = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlaySendSwitch"), "send switch");
        send.Patterns.Toggle.Pattern.Toggle();
        WaitUntil(() => send.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On, "sending on");

        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByName("Battle").And(cf.ByControlType(ControlType.Button)), "Battle slot"));

        string expected = "Battle · " + app.Text("HotReload.ReloadSent");
        WaitUntil(() => (_lastSeen = StatusText(overlay)) == expected, "the reload sent");
        Assert.Equal("2", Value(app, "TestModA", "hair"));
        Assert.Equal("1", Value(app, "TestModA", "coat"));
        AssertOthersUntouched(app);
        Assert.NotNull(Slot(app, "Battle")!["ActivatedAtUtc"]);
        // Read-only only while the loader reloads.
        WaitUntil(() => !File.GetAttributes(UserIni(app)).HasFlag(FileAttributes.ReadOnly), "d3dx_user.ini writable again", 20);
        app.AssertNoErrors();
    }
}
