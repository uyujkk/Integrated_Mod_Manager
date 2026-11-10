using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mod workspace page (stage J7, docs/development/UI-Structure-Plan.md): characters, Mods, details, the shortcut
/// editor and the combination preset view. It came from the window's files as it was; what it still needs from the
/// window goes through <see cref="IWorkspaceHost"/>, never the window type.
/// </summary>
public sealed partial class WorkspacePage : UserControl
{
    private readonly AppServices _services;
    private readonly IWorkspaceHost _host;

    internal WorkspacePage(AppServices services, IWorkspaceHost host)
    {
        _services = services;
        _host = host;
        InitializeComponent();
    }

    private RepositoryPathsViewModel Paths => _services.Paths;

    private SettingsViewModel SettingsView => _services.Settings;

    private bool IsChineseUi => _services.Language.IsChinese;

    // The App*Brush tokens of Themes/Tokens.xaml, in the theme the window shows (stage J finish, step 2).
    private Brush GetAppThemeBrush(string resourceKey) => ThemeTokens.Get(resourceKey, _services.Appearance.ThemeKey);

    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private string T(UiMessage message) => _services.Language.T(message);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    private void ShowAppNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Success, Exception? error = null)
        => _services.Status.Notify(message, severity, error);

    private Task ShowMessageAsync(string content, string title) => _services.Dialogs.MessageAsync(title, content);

    private Task<string?> PromptForTextAsync(string content, string title, string defaultValue, string primaryText)
        => _services.Dialogs.PromptAsync(title, content, defaultValue, primaryText);

    private Task<bool> ShowConfirmAsync(string content, string title, string primaryText) => _services.Dialogs.ConfirmAsync(title, content, primaryText);

    private Task<bool> ShowDangerConfirmAsync(string content, string title, string primaryText)
        => _services.Dialogs.DangerConfirmAsync(title, content, primaryText);
}
