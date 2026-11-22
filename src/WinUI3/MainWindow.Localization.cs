
namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private bool IsChineseUi => _services.Language.IsChinese;

    // Text lookups forward to the language service, so pages can use it without the window.
    private string T(string key) => _services.Language.T(key);

    private string T(string key, params object?[] values) => _services.Language.T(key, values);

    private string T(UiMessage message) => _services.Language.T(message);

    private static UiMessage M(string key, params object?[] values) => LanguageService.M(key, values);

    private string NormalizeUiText(string? value) => _services.Language.NormalizeUiText(value);

    // For text pairs built at runtime, such as stored status messages.
    private string L(string zh, string en)
    {
        return BetaLocalization.Resolve(!IsChineseUi, zh, en);
    }

    private void ApplyLanguage()
    {
        Title = T("Common.IntegratedModManager", AppVersion);

        ApplyPrimaryNavigationContent();

        // The Online page labels itself once it exists (stage J finish, step 5b); the character list is kept without it.
        if (_onlineView is null) EnsureOnlineCharacterOptions(_services.Repositories.Selected);
        else _onlineView.ApplyWindowLanguage();

        // The dashboard labels itself (Pages/DashboardPage.xaml.cs).
        RefreshDashboard();

        // The Settings page labels itself (Pages/SettingsPage.xaml.cs).
        RefreshAppearanceSettings();
        RefreshSettingsOnlineConfigSummary();

        AppTitleBar.Title = T("Localization.IntegratedModManager");
        AppTitleBar.Subtitle = AppVersion;

        if (_workspaceView is { } workspace) workspace.ApplyLanguage();
        else
        {
            // As the page did with no character selected: the welcome line until a scan found characters.
            _services.Workspace.RefreshCopyStates();
            if (_services.Workspace.Characters.All.Count == 0) _services.Workspace.SetDefaultStatus();
        }
    }
}
