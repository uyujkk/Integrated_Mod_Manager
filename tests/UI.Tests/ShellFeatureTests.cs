using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// What the command palette, the tray, the app update offer and the Mod workspace's labels do, written before stage J
// finish step 8c moves that code out of the window. Nothing reaches the network: the update test uses a synthetic
// package beside the test copy and declines it. Only the test's own copy changes.
public sealed class ShellFeatureTests
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

    private static AutomationElement OpenPalette(AppSession app)
    {
        app.MainWindow.SetForeground();
        Thread.Sleep(300);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
        return app.FindByName(app.Text("CommandPalette.Placeholder"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Palette_PickingAPageOpensIt()
    {
        using AppSession app = AppSession.Configured().Start();
        AutomationElement search = OpenPalette(app);
        search.AsTextBox().Text = app.Text("Shell.Settings");
        Thread.Sleep(500);
        Keyboard.Type(VirtualKeyShort.ENTER);

        app.WaitForSaved(saved => (string?)saved["CurrentPrimarySection"] == "settings", "The Settings page should be open and saved");
        app.Find("LanguageComboBox", TimeSpan.FromSeconds(10));
        Assert.True(app.MainWindow.FindFirstDescendant(cf => cf.ByName(app.Text("CommandPalette.Placeholder"))) is null or { IsOffscreen: true },
            "The palette should close");
        app.AssertNoErrors();
    }

    [Fact]
    public void Palette_PickingAModShowsItSelectedInTheWorkspace()
    {
        using AppSession app = AppSession.Configured().Start();
        app.Resize(1600, 1000);
        AutomationElement search = OpenPalette(app);
        search.AsTextBox().Text = "TestModB";
        Thread.Sleep(500);
        Keyboard.Type(VirtualKeyShort.ENTER);

        AutomationElement? item = null;
        WaitUntil(() =>
        {
            item = app.MainWindow.FindAllDescendants(cf => cf.ByName("TestModB")).Select(found =>
            {
                AutomationElement? current = found;
                while (current is not null && current.ControlType != ControlType.ListItem) current = current.Parent;
                return current;
            }).FirstOrDefault(found => found is not null);
            return item is not null && item.Patterns.SelectionItem.Pattern.IsSelected.Value;
        }, "TestModB selected in the workspace");
        app.AssertNoErrors();
    }

    [Fact]
    public void Tray_MinimizingWithTheTraySettingHidesTheWindow()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["MinimizeToTray"] = true).Start();
        nint handle = app.MainWindow.Properties.NativeWindowHandle.Value;
        Assert.True(IsWindowVisible(handle));

        app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);

        WaitUntil(() => !IsWindowVisible(handle), "the window hidden in the tray");
        app.AssertNoErrors();
    }

    [Fact]
    public void Tray_MinimizingWithoutTheTraySettingKeepsTheWindowOnTheTaskbar()
    {
        using AppSession app = AppSession.Configured().Start();
        nint handle = app.MainWindow.Properties.NativeWindowHandle.Value;

        app.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);
        Thread.Sleep(1500);

        Assert.True(IsWindowVisible(handle), "Without the setting a minimized window stays on the taskbar");
        app.AssertNoErrors();
    }

    [Fact]
    public void Updates_ALocalPackageIsOfferedAtStartupAndDecliningKeepsTheApp()
    {
        using AppSession app = AppSession.Configured((shell, session) =>
            File.WriteAllBytes(Path.Combine(session.Root, "Integrated_Mod_Manager-v99.0.0.zip"), [0x50, 0x4B, 0x05, 0x06, .. new byte[18]])).Start();

        app.FindByName(app.Text("Updates.InstallLocalUpdate"), TimeSpan.FromSeconds(20));
        AppSession.Invoke(app.Find("CloseButton"));
        Thread.Sleep(1000);
        Assert.False(app.MainWindow.Properties.ProcessId.Value is int pid && Process.GetProcessById(pid).HasExited, "Declining keeps the app running");
        Assert.True(File.Exists(Path.Combine(app.Root, "Integrated_Mod_Manager-v99.0.0.zip")), "Declining leaves the package");

        AppSession.Invoke(app.Find("SettingsNavButton"));
        string detected = string.Format(app.Text("Updates.LocalPackageDetected"), "99.0.0");
        app.FindByName(detected, TimeSpan.FromSeconds(10));
        app.AssertNoErrors();
    }

    [Fact]
    public void Workspace_LabelsFollowTheLanguage()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();
        app.Resize(1700, 1000);
        AutomationElement import = app.Find("ImportZipButton", TimeSpan.FromSeconds(10));
        Assert.Equal(app.Text("Localization.ImportMod"), import.Name);
        Assert.Equal(app.Text("Common.CreateFirstLevelFolder"), app.Find("CreateFirstLevelButton").Name);

        AppSession.Invoke(app.Find("SettingsNavButton"));
        ComboBox languages = app.Find("LanguageComboBox", TimeSpan.FromSeconds(10)).AsComboBox();
        languages.Select(1);
        string englishTitle = AppSession.TextIn("en-US", "Common.IntegratedModManager").Split('{')[0].Trim();
        WaitUntil(() => app.MainWindow.Title.StartsWith(englishTitle, StringComparison.Ordinal), "the window in English");
        AppSession.Invoke(app.Find("RepositoryNavButton"));

        WaitUntil(() => app.Find("ImportZipButton", TimeSpan.FromSeconds(10)).Name == AppSession.TextIn("en-US", "Localization.ImportMod"), "the import button in English");
        Assert.Equal(AppSession.TextIn("en-US", "Common.CreateFirstLevelFolder"), app.Find("CreateFirstLevelButton").Name);
        Assert.Equal(AppSession.TextIn("en-US", "Localization.DeleteSelectedMod"), app.Find("DeleteSecondLevelButton").Name);
        app.AssertNoErrors();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
}
