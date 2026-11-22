using IntegratedModManager.Core.Infrastructure;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The app's logs beside the executable (stage J finish, step 8a: moved from the window unchanged): startup.log for
/// startup stages, and app-errors.log for failures, with the message (and when asked the stack) sanitized.
/// </summary>
internal static class AppLog
{
    private static readonly object ApplicationErrorLogSync = new();

    public static void TraceStartupStage(string stage)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "startup.log"),
                $"{DateTimeOffset.Now:O} {stage}{Environment.NewLine}");
        }
        catch
        {
            // Startup tracing must never prevent the application from opening.
        }
    }

    public static void LogPersistentDataStoreIssue(string operation, Exception exception)
        => LogApplicationIssue("SQLite " + operation, exception);

    public static void LogApplicationIssue(string operation, Exception exception)
    {
        try
        {
            string summary = DiagnosticText.Sanitize(exception.Message);
            lock (ApplicationErrorLogSync)
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "app-errors.log");
                LogFileRotation.RotateIfLarger(logPath);
                File.AppendAllText(
                    logPath,
                    $"{DateTimeOffset.Now:O} {operation}: {exception.GetType().Name} (0x{exception.HResult:X8}) {summary}{Environment.NewLine}");
            }
        }
        catch
        {
            // Error logging must never interrupt the user operation being diagnosed.
        }
    }

    public static void LogApplicationIssueWithStack(string operation, Exception exception)
    {
        LogApplicationIssue(operation, exception);
        try
        {
            string stack = DiagnosticText.Sanitize(exception.ToString());
            lock (ApplicationErrorLogSync)
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "app-errors.log"),
                    $"    {stack.Replace(Environment.NewLine, Environment.NewLine + "    ")}{Environment.NewLine}");
            }
        }
        catch
        {
            // Error logging must never interrupt the user operation being diagnosed.
        }
    }
}
