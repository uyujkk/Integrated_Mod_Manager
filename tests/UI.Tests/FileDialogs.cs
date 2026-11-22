using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace IntegratedModManager.UI.Tests;

/// <summary>The system's file and folder dialogs, which the app opens for picking a path.</summary>
internal static class FileDialogs
{
    // The system's file dialog (IFileOpenDialog) the app opens for a folder or a file: the path goes into its name box.
    // The app shows it modally inside the click's handler. An automation Invoke would stay in progress until the dialog
    // closes and block every automation call to the app, so the button gets a real mouse click.
    public static void Choose(AppSession app, AutomationElement button, string path)
    {
        uint process = (uint)app.MainWindow.Properties.ProcessId.Value;
        app.MainWindow.SetForeground();
        AppSession.ScrollIntoView(button);
        button.Click();
        AutomationElement? dialog = null;
        WaitUntil(() => (dialog = FileDialog(app, process)) is not null, "the file dialog");
        Thread.Sleep(800);
        AutomationElement Box() => AppSession.FindIn(dialog!, cf => cf.ByAutomationId("1152").And(cf.ByControlType(ControlType.Edit))
            .Or(cf.ByAutomationId("1148").And(cf.ByControlType(ControlType.Edit))), "the file name box");
        // Open is a split button in the file dialog and a plain button in the folder dialog.
        AutomationElement Open() => AppSession.FindIn(dialog!, cf => cf.ByAutomationId("1")
            .And(cf.ByControlType(ControlType.Button).Or(cf.ByControlType(ControlType.SplitButton))), "the open button");
        Box().Patterns.Value.Pattern.SetValue(path);
        Thread.Sleep(300);
        Open().Click();
        // A folder path typed into the folder dialog first opens that folder; choosing again (with the box empty) picks it.
        Thread.Sleep(1500);
        if (FileDialog(app, process) is not null)
        {
            Box().Patterns.Value.Pattern.SetValue(string.Empty);
            Thread.Sleep(300);
            Open().Click();
        }
        WaitUntil(() => FileDialog(app, process) is null, "the file dialog closed");
    }

    // The dialog is a top-level window of the app. While it is open the app's own window does not answer UI Automation
    // (its UI thread is inside the click), so it is found by its window class instead.
    private static AutomationElement? FileDialog(AppSession app, uint process)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            if (IsWindowVisible(window) && GetWindowThreadProcessId(window, out uint owner) != 0 && owner == process
                && GetClassName(window, name, name.Capacity) > 0 && name.ToString() == "#32770")
            {
                found = window;
                return false;
            }
            return true;
        }, 0);
        return found == 0 ? null : app.Automation.FromHandle(found);
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int capacity);

    private static void WaitUntil(Func<bool> condition, string what, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }
}
