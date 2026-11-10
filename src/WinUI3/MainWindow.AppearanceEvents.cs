using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    // Language, theme and density changes are announced by the services, and each page responds in
    // its own handler. Handlers run in the order they subscribe here, which is the order the window
    // used to update the pages in.
    private void SubscribeToAppearanceEvents()
    {
        LanguageService language = _services.Language;
        // Mod workspace: the default slots are named in the interface language.
        language.LanguageChanged += (_, _) => RelabelDefaultSlots();
        // Shell, and the static text of every page.
        language.LanguageChanged += (_, _) => ApplyLanguage();
        // Shortcuts: binding descriptions and the page built in code.
        language.LanguageChanged += (_, _) =>
        {
            _workspaceView.RefreshVisibleShortcutDescriptionsForLanguage();
            RebuildShortcutPaneForLanguage();
        };
        language.LanguageChanged += (_, _) => RefreshSettingsPane();
        language.LanguageChanged += (_, _) => RefreshUpdatesPane();
        // The choice is saved after the slots were relabelled, so the new names are saved too.
        language.LanguageChanged += (_, _) => SaveConfig();
        // Online: re-project the open details from the same raw cache; the older translation is rejected.
        language.LanguageChanged += (_, _) =>
        {
            if (_onlineView.IsOnlineDetailOpen && _onlineView._activeOnlineDetailMod is OnlineModCard mod)
                _ = _onlineView.ShowOnlineModDetailsAsync(mod);
        };

        AppearanceService appearance = _services.Appearance;
        // Mod workspace: the summary colours are set in code.
        appearance.ThemeChanged += (_, _) => _workspaceView.RenderModSummary();
        // Shell: the selected navigation item.
        appearance.ThemeChanged += (_, _) => RefreshPrimaryNavigationVisuals();
        // Mod workspace: the shortcut editor's key boxes (the Shortcuts page redraws its own cards).
        appearance.ThemeChanged += (_, _) => _workspaceView.UpdateShortcutKeyFocusVisuals();

        appearance.DensityChanged += (_, _) => ApplyShellDensity();
        appearance.DensityChanged += (_, _) => ApplyWorkspaceDensity(appearance.IsCompact);
    }

    // Each page applies the density itself (MainWindow.AppearanceEvents.cs).
    private void ApplyInterfaceDensity() => _services.Appearance.ApplyDensity(_interfaceDensity == InterfaceDensity.Compact);

    // The navigation view keeps its own spacing; the density changes the page margins.
    private void ApplyShellDensity() => ApplyPageMargins();

    private void ApplyWorkspaceDensity(bool compact)
    {
        _workspaceView.FirstLevelListView.Padding = new Thickness(compact ? 3 : 6);
        _workspaceView.SecondLevelListView.Padding = new Thickness(compact ? 3 : 6);
        _workspaceView.ShortcutRowsPanel.Spacing = compact ? 6 : 10;
    }
}
