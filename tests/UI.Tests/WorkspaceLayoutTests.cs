using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace IntegratedModManager.UI.Tests;

// Stage J7-b: the Mod workspace's columns can be dragged and keep their width across a restart. Only AppSession's
// disposable copy and data are used.
public sealed class WorkspaceLayoutTests
{
    private const uint MouseEventMove = 0x0001;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extraInfo);

    private static void Wait(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), what);
            Thread.Sleep(100);
        }
    }

    [Fact]
    public void Workspace_DraggedColumnWidthSurvivesARestart()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();
        app.Resize(1400, 900);
        app.FindByName("TestCharacter", TimeSpan.FromSeconds(15));
        Thread.Sleep(800);

        AutomationElement splitter = app.Find("WorkspaceCharacterSplitter", TimeSpan.FromSeconds(10));
        double before = splitter.BoundingRectangle.Left - app.MainWindow.BoundingRectangle.Left;
        var start = splitter.GetClickablePoint();
        // A real drag: press, then relative mouse moves (absolute cursor jumps do not reach a captured pointer as
        // movement from this DPI-unaware test process), then release.
        Mouse.MoveTo(start);
        Thread.Sleep(200);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(100);
        for (int step = 0; step < 15; step++)
        {
            mouse_event(MouseEventMove, 4, 0, 0, IntPtr.Zero);
            Thread.Sleep(30);
        }
        Thread.Sleep(100);
        Mouse.Up(MouseButton.Left);
        Thread.Sleep(800);
        double dragged = app.Find("WorkspaceCharacterSplitter").BoundingRectangle.Left - app.MainWindow.BoundingRectangle.Left;
        Assert.True(dragged - before > 30, $"The splitter should move right: {before} -> {dragged}");

        // The width is saved when the drag ends (the file is written just after; wait for it).
        Wait(() => app.TryShellConfig()?["WorkspaceCharacterWidth"]?.GetValue<double>() is > 0, "The width should be saved");

        app.Restart();
        app.Resize(1400, 900);
        app.FindByName("TestCharacter", TimeSpan.FromSeconds(15));
        Thread.Sleep(800);
        double restored = app.Find("WorkspaceCharacterSplitter", TimeSpan.FromSeconds(10)).BoundingRectangle.Left - app.MainWindow.BoundingRectangle.Left;
        Assert.True(Math.Abs(restored - dragged) <= 2, $"The column should keep its width: {dragged} -> {restored}");
        app.AssertNoErrors();
    }

    [Fact]
    public void Workspace_NarrowWindowShowsDetailsInADrawerThatCloses()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();
        app.Resize(670, 530);
        app.FindByName("TestCharacter", TimeSpan.FromSeconds(15));
        Thread.Sleep(800);
        bool DrawerOpen() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CloseModDetailsDrawerButton")) is { IsOffscreen: false };
        AutomationElement Visible(string name) => app.MainWindow.FindAllDescendants(cf => cf.ByName(name)).First(element => !element.IsOffscreen);

        // Tapping a Mod opens its details over the list; Esc closes them.
        Assert.False(DrawerOpen());
        Mouse.Click(Visible("TestModB").GetClickablePoint());
        Wait(DrawerOpen, "Tapping a Mod should open the drawer");
        Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        Wait(() => !DrawerOpen(), "Esc should close the drawer");

        // The Details button opens them too; the drawer's close button closes them.
        AppSession.Invoke(app.Find("RepositoryOpenDetailsButton"));
        Wait(DrawerOpen, "The Details button should open the drawer");
        AppSession.Invoke(app.Find("CloseModDetailsDrawerButton"));
        Wait(() => !DrawerOpen(), "The close button should close the drawer");

        // A window wide enough for three columns shows the details inline, without the drawer.
        AppSession.Invoke(app.Find("RepositoryOpenDetailsButton"));
        Wait(DrawerOpen, "The drawer should open again");
        app.Resize(1400, 900);
        Wait(() => !DrawerOpen(), "A wide window should close the drawer");
        app.AssertNoErrors();
    }
}
