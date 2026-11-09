using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The title bar's search box can be turned off in Settings (Appearance and language); Ctrl+K opens the
// command palette either way, so turning it off only frees the title bar.
public sealed partial class MainWindow
{
    private bool _showTitleBarSearch = true;
    private bool _isApplyingTitleBarSearchSetting;

    private void ApplyTitleBarSearchVisibility()
        => TitleBarSearchBox.Visibility = _showTitleBarSearch ? Visibility.Visible : Visibility.Collapsed;

    private void RefreshTitleBarSearchSetting()
    {
        _isApplyingTitleBarSearchSetting = true;
        SettingsView.ShowTitleBarSearch = _showTitleBarSearch;
        _isApplyingTitleBarSearchSetting = false;
    }
}
