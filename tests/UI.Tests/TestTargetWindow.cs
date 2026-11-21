using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IntegratedModManager.UI.Tests;

// A small window a test starts as the overlay's target and closes again; keys sent to it go nowhere else.
internal sealed class TestTargetWindow : IDisposable
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
