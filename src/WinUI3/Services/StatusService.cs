using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>App-wide notifications; the window shows them in its InfoBar.</summary>
internal sealed class StatusService
{
    public event EventHandler<StatusNotification>? NotificationRequested;

    public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
        => NotificationRequested?.Invoke(this, new StatusNotification(message, severity));
}

internal sealed record StatusNotification(string Message, InfoBarSeverity Severity);
