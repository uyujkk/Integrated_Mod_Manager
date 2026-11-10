using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Online page (stage J6, docs/development/UI-Structure-Plan.md): browsing, details, downloads and installs
/// from the online source. It came from the window's files as it was; what it still needs from the window goes
/// through <see cref="IOnlineHost"/>, never the window type.
/// </summary>
public sealed partial class OnlinePage : UserControl
{
    private readonly AppServices _services;
    private readonly IOnlineHost _host;

    internal OnlinePage(AppServices services, IOnlineHost host)
    {
        _services = services;
        _host = host;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        InitializeComponent();
    }

    private bool IsChineseUi => _services.Language.IsChinese;

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private string T(UiMessage message) => _services.Language.T(message);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    private void ShowAppNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Success, Exception? error = null)
        => _services.Status.Notify(message, severity, error);

    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    private Task<bool> ShowConfirmAsync(string content, string title, string primaryText) => _services.Dialogs.ConfirmAsync(title, content, primaryText);

    private Task<bool> ShowDangerConfirmAsync(string content, string title, string primaryText)
        => _services.Dialogs.DangerConfirmAsync(title, content, primaryText);
}
