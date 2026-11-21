using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Downloading and installing Mods from the online source (stage J finish, step 5a, moved from the Online page
/// unchanged): refreshing a Mod's files, the download location and duplicate checks, downloading, extracting, the
/// install records, retrying, and the character, category and detail lookups the flow uses. Downloads keep working
/// without the Online page: the Updates page retries and restored tasks run here. While the page exists it is the
/// <see cref="View"/>, which shows the status line, the progress bar and the card and detail states.
/// </summary>
internal sealed partial class OnlineDownloadService(AppServices services)
{
    private readonly AppServices _services = services;
    private IOnlineHost _host = null!;

    private DispatcherQueue DispatcherQueue { get; set; } = null!;

    /// <summary>Called once by the window: what the flow still uses from it, and the UI thread's queue.</summary>
    public void Attach(IOnlineHost host, DispatcherQueue ui)
    {
        _host = host;
        DispatcherQueue = ui;
    }

    /// <summary>The Online page while it exists (it sets itself); the flow shows its progress there.</summary>
    public IOnlineDownloadView? View { get; set; }

    private bool IsChineseUi => _services.Language.IsChinese;

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private string T(UiMessage message) => _services.Language.T(message);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    private void ShowAppNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Success, Exception? error = null)
        => _services.Status.Notify(message, severity, error);

    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    private Task<bool> ShowDangerConfirmAsync(string content, string title, string primaryText)
        => _services.Dialogs.DangerConfirmAsync(title, content, primaryText);

    // The page's parts of a download; nothing to show while the page does not exist.
    private void SetOnlineStatus(UiMessage status) => View?.SetOnlineStatus(status);

    private void SetOnlineDownloadProgress(bool isVisible, double percent, string text, bool isIndeterminate = false)
        => View?.SetOnlineDownloadProgress(isVisible, percent, text, isIndeterminate);

    private void RefreshOnlineDetailActionState() => View?.RefreshOnlineDetailActionState();

    private void RefreshOnlinePaneV2() => View?.RefreshOnlinePaneV2();
}

/// <summary>What the Online page shows of a download while it exists (stage J finish, step 5a).</summary>
internal interface IOnlineDownloadView
{
    void SetOnlineStatus(UiMessage status);

    void SetOnlineDownloadProgress(bool isVisible, double percent, string text, bool isIndeterminate = false);

    void RefreshOnlineDetailActionState();

    void RefreshOnlinePaneV2();
}
