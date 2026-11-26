using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

// The title bar's search box can be turned off in Settings (Appearance and language); Ctrl+K opens the
// command palette either way, so turning it off only frees the title bar. The search style (brief or detailed, the
// default) sits beside it.
public sealed partial class MainWindow
{
    private bool _isApplyingTitleBarSearchSetting;

    // Narrower than the compact navigation threshold (1120 px, so also at 1000 px) the box shrinks to a button.
    private const double TitleBarSearchButtonBelowWidth = 1120;

    private void ApplyTitleBarSearchVisibility()
    {
        bool narrow = Bounds.Width < TitleBarSearchButtonBelowWidth;
        TitleBarSearchBox.Visibility = _shell.ShowTitleBarSearch && !narrow ? Visibility.Visible : Visibility.Collapsed;
        TitleBarSearchButton.Visibility = _shell.ShowTitleBarSearch && narrow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTitleBarSearchButtonClicked(object sender, RoutedEventArgs e) => OpenCommandPalette();

    private void RefreshTitleBarSearchSetting()
    {
        _isApplyingTitleBarSearchSetting = true;
        SettingsView.ShowTitleBarSearch = _shell.ShowTitleBarSearch;
        SettingsView.SearchStyleIndex = _shell.DetailedSearch ? 1 : 0;
        _isApplyingTitleBarSearchSetting = false;
    }
}
