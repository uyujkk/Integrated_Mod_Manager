using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>App-wide notifications; the window shows them in its InfoBar.</summary>
internal sealed class StatusService
{
    public event EventHandler<StatusNotification>? NotificationRequested;

    /// <param name="error">The failure behind an error notification; the InfoBar then offers its details.</param>
    public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Success, Exception? error = null)
        => NotificationRequested?.Invoke(this, new StatusNotification(message, severity, error));
}

internal sealed record StatusNotification(string Message, InfoBarSeverity Severity, Exception? Error = null);
