using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// The redesigned overlay on the test data with OverlaySamples (enabled test Mods and state slots). Keys are never sent:
// a target, when one is needed, is a small window this test starts and closes.
public sealed class OverlayTests
{
    private static AppSession StartWithSamples() => AppSession.Configured(OverlaySamples.Apply).Start();

    private static Window OpenOverlay(AppSession app)
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.F9);
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Thread.Sleep(250);
            if (app.FindWindow(window => window.Title.StartsWith("IMM ", StringComparison.Ordinal)) is { } overlay)
            {
                Thread.Sleep(1200); // the height settles after the first layout
                return overlay;
            }
        }
        throw new InvalidOperationException("The overlay did not open.");
    }

    private static void WaitUntil(Func<bool> condition, string what, int seconds = 10)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    private static Rectangle Bounds(Window overlay) => overlay.BoundingRectangle;

    // Fitted to its content, the main area has nothing to scroll to: no key or slot is cut off at the bottom.
    private static void AssertNothingHidden(Window overlay, string view)
    {
        AutomationElement main = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayMainScroll"), "main area");
        Assert.False(main.Patterns.Scroll.Pattern.VerticallyScrollable.Value, $"Part of the {view} is cut off and scrolls");
    }

    [Fact]
    public void Overlay_HeightFollowsTheContent()
    {
        using AppSession app = StartWithSamples();
        Window overlay = OpenOverlay(app);
        int full = Bounds(overlay).Height;
        // The keys and slots of TestModA are shown, and the panel is no taller than they need.
        Assert.NotNull(overlay.FindFirstDescendant(cf => cf.ByName("H")));
        Assert.True(full < 860 * 1.5, $"The overlay should fit its content, not the old fixed height: {full}");
        AssertNothingHidden(overlay, "full view");

        // The compact view hides the controls and the footer: the panel gets shorter, and back again.
        AppSession.Invoke(SmokeTests.OverlayMenuItem(app, overlay, "OverlayCompactButton"));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        WaitUntil(() => Bounds(overlay).Height < full - 40, "shorter in the compact view");
        Thread.Sleep(500);
        AssertNothingHidden(overlay, "compact view");
        AppSession.Invoke(SmokeTests.OverlayMenuItem(app, overlay, "OverlayCompactButton"));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        WaitUntil(() => Math.Abs(Bounds(overlay).Height - full) <= 4, "as tall as before");
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_ReadResultIsBehindTheStatusIcon()
    {
        using AppSession app = StartWithSamples();
        Window overlay = OpenOverlay(app);
        string read = string.Format(app.Text("ShortcutPanel.ReadBindingsInGameConditionsAre"), 2);
        AutomationElement icon = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayStatusButton"), "status icon");
        WaitUntil(() => icon.HelpText == read, "the read result on the status icon");
        Assert.Equal(app.Text("Overlay.Status"), icon.Name);
        // Not an error: no notice takes room on the panel.
        Assert.True(overlay.FindFirstDescendant(cf => cf.ByAutomationId("OverlayStatusBar")) is null or { IsOffscreen: true },
            "Only failures should take room on the panel");
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_TargetIsChosenInTheOverlayAndTheSendSwitchShowsItsState()
    {
        using var target = TestTargetWindow.Start();
        using AppSession app = StartWithSamples();
        Window overlay = OpenOverlay(app);
        AutomationElement dot = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayStateDot"), "state dot");
        Assert.Contains(app.Text("ShortcutPanel.NoTargetYet"), dot.Name);

        AppSession.Invoke(AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayRefreshTargetsButton"), "refresh windows"));
        ComboBox picker = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlayTargetPicker"), "target picker").AsComboBox();
        picker.Expand();
        Thread.Sleep(500);
        AutomationElement item = picker.Items.FirstOrDefault(entry => entry.Name.Contains(TestTargetWindow.Title, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The test window is not in the overlay's list.");
        item.Patterns.SelectionItem.Pattern.Select();
        picker.Collapse();
        WaitUntil(() => dot.Name.Contains(TestTargetWindow.Title, StringComparison.Ordinal), "the chosen window in the title bar");
        Assert.Contains(app.Text("ShortcutPanel.InputOff"), dot.Name);

        AutomationElement send = AppSession.FindIn(overlay, cf => cf.ByAutomationId("OverlaySendSwitch"), "send switch");
        send.Patterns.Toggle.Pattern.Toggle();
        WaitUntil(() => send.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On && dot.Name.Contains(app.Text("ShortcutPanel.InputOn"), StringComparison.Ordinal),
            "sending on");
        send.Patterns.Toggle.Pattern.Toggle();
        WaitUntil(() => send.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off && dot.Name.Contains(app.Text("ShortcutPanel.InputOff"), StringComparison.Ordinal),
            "sending off");
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_MenuHoldsTheViewSettings()
    {
        using AppSession app = StartWithSamples();
        Window overlay = OpenOverlay(app);
        foreach (string id in new[] { "OverlayClickThroughButton", "OverlayOpacitySlider", "OverlayRefreshButton", "OverlayCompactButton" })
            Assert.NotNull(SmokeTests.OverlayMenuItem(app, overlay, id));
        ComboBox size = SmokeTests.OverlayMenuItem(app, overlay, "OverlayCompactFontSize").AsComboBox();
        size.Expand();
        Thread.Sleep(400);
        size.Items.First(entry => entry.Name == "18").Patterns.SelectionItem.Pattern.Select();
        app.WaitForSaved(saved => (int?)saved["OverlayCompactFontSize"] == 18, "The compact text size should be saved");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        // With the menu closed, its settings are not on the panel itself.
        Thread.Sleep(500);
        Assert.True(overlay.FindFirstDescendant(cf => cf.ByAutomationId("OverlayOpacitySlider")) is null or { IsOffscreen: true },
            "The opacity slider should be in the menu only");
        app.AssertNoErrors();
    }

    [Fact]
    public void Overlay_SizeAndPlaceSurviveARestart()
    {
        using AppSession app = StartWithSamples();
        Window overlay = OpenOverlay(app);
        Rectangle before = Bounds(overlay);
        // Drag the title bar left and down, then the bottom right corner wider and shorter, with the mouse.
        Drag(new Point(before.X + 60, before.Y + 18), -300, 120);
        Thread.Sleep(600);
        Rectangle moved = Bounds(overlay);
        Assert.True(moved.X < before.X - 200, $"The title bar should drag the overlay: {before} -> {moved}");
        Drag(new Point(moved.Right - 3, moved.Bottom - 3), 120, -60);
        Thread.Sleep(800);
        Rectangle after = Bounds(overlay);
        Assert.True(after.Width > moved.Width + 80, $"The corner should resize the overlay: {moved} -> {after}");
        app.WaitForSaved(saved => saved["OverlayPlacements"]?[0]?["Width"]?.GetValue<double>() > 0, "The place should be saved");

        app.Restart();
        Window reopened = OpenOverlay(app);
        Rectangle restored = Bounds(reopened);
        Assert.True(Math.Abs(restored.X - after.X) <= 4 && Math.Abs(restored.Y - after.Y) <= 4 && Math.Abs(restored.Width - after.Width) <= 4
            && Math.Abs(restored.Height - after.Height) <= 4, $"Restored {restored}, left at {after}");
        app.AssertNoErrors();
    }

    // A drag in small relative steps, which window moves and resizes follow (absolute jumps are dropped).
    private static void Drag(Point from, int dx, int dy)
    {
        Mouse.Position = from;
        Thread.Sleep(200);
        mouse_event(0x0002, 0, 0, 0, 0); // left down
        Thread.Sleep(200);
        int steps = 30;
        for (int i = 0; i < steps; i++)
        {
            mouse_event(0x0001, dx / steps, dy / steps, 0, 0);
            Thread.Sleep(15);
        }
        Thread.Sleep(200);
        mouse_event(0x0004, 0, 0, 0, 0); // left up
    }

    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, nint extra);

    /// <summary>A plain window to choose as the target; keys are never sent to it.</summary>
    private sealed class TestTargetWindow : IDisposable
    {
        public const string Title = "IMM overlay test target";
        private readonly Process _process;

        private TestTargetWindow(Process process) => _process = process;

        public static TestTargetWindow Start()
        {
            string script = "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; "
                + $"$f.Text = '{Title}'; $f.Width = 360; $f.Height = 240; [System.Windows.Forms.Application]::Run($f)";
            var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
            var window = new TestTargetWindow(process);
            var watch = Stopwatch.StartNew();
            while (FindWindowW(null, Title) == 0)
            {
                if (watch.ElapsedMilliseconds > 20000) { window.Dispose(); throw new TimeoutException("The test target window did not open."); }
                Thread.Sleep(200);
            }
            return window;
        }

        public void Dispose()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            _process.Dispose();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string? className, string title);
    }
}
