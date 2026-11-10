using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// An exception that escapes an async void event handler is rethrown on the UI
// dispatcher, where WinUI fails fast (often reported as 0xc000027b). Every async
// void handler and async event lambda in the window routes escaped exceptions here:
// log them with a stack trace, tell the user, and keep the app running.
public sealed partial class MainWindow
{
    private async Task RunUiActionAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ReportUiActionFailure(operation, ex);
        }
    }

    private void ReportUiActionFailure(string operation, Exception exception)
    {
        try
        {
            if (exception is OperationCanceledException)
            {
                // A cancellation that reached the handler is not a failure the user must act on.
                UiCrashDiagnostics.RecordAction("UI action cancelled: " + operation);
                return;
            }

            UiCrashDiagnostics.RecordAction("UI action failed: " + operation);
            LogApplicationIssueWithStack("UI action " + operation, exception);
            if (_services.IsClosing)
            {
                return;
            }

            string summary = SanitizeDiagnosticText(exception.Message);
            ShowAppNotification(T("UiActionGuard.Stopped", summary), InfoBarSeverity.Error, exception);
        }
        catch
        {
            // Reporting must never turn a recoverable failure into a crash.
        }
    }

    internal static void LogApplicationIssueWithStack(string operation, Exception exception)
    {
        LogApplicationIssue(operation, exception);
        try
        {
            string stack = SanitizeDiagnosticText(exception.ToString());
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
