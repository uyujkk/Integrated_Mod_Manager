using System.Runtime.InteropServices;

namespace ModFolderCopier.WinUI;

// Explicitly enabled only for a local diagnostic run. No upload or crash suppression.
internal static class UiCrashDiagnostics
{
    internal static bool Enabled { get; } = Environment.GetEnvironmentVariable("IMM_UI_DIAGNOSTICS") == "1";
    private static readonly object Gate = new();
    private static IntegratedModManager.Core.UiActionTrail? _actionTrail;

    internal static void RecordAction(string stage, int itemId = 0, double width = 0, double height = 0, bool closed = false)
    {
        try
        {
            lock (Gate)
            {
                _actionTrail ??= new(Path.Combine(AppContext.BaseDirectory, "diagnostics", "ui-action-context.json"),
                    IntegratedModManager.Core.ApplicationReleasePolicy.VersionLabel);
                _actionTrail.Record(stage, itemId, width, height, closed);
            }
        }
        catch { /* Recording must not suppress or introduce the original failure. */ }
    }

    internal static void Write(string stage, string details = "")
    {
        if (!Enabled) return;
        try
        {
            string line = $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} thread={Environment.CurrentManagedThreadId} {stage} {details}\n";
            lock (Gate)
            {
                string path = Path.Combine(AppContext.BaseDirectory, "ui-diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, line);
            }
            OutputDebugString(line);
        }
        catch { /* Diagnostics must never change the original failure. */ }
    }

    [DllImport("kernel32.dll", EntryPoint = "OutputDebugStringW", CharSet = CharSet.Unicode)]
    private static extern void OutputDebugString(string message);
}
