using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Writing a state slot or a user.ini snapshot back to the loader (moved from the window in stage J8).</summary>
internal static class LoaderStateRestoreDialog
{
    /// <summary>Restore a state slot: whether to go ahead, and whether to apply it while the game runs.</summary>
    public static async Task<(bool Restore, bool HotReload)> ConfirmSlotAsync(AppServices services, UIElement? savedLook, string summary, bool canHotReload)
    {
        string T(string key) => services.Language.T(key);
        var closedCheck = new CheckBox
        {
            Content = T("Common.IHaveFullyClosedTheGame")
        };
        var content = new StackPanel { Spacing = 12 };
        if (savedLook is not null) content.Children.Add(savedLook);
        content.Children.Add(new TextBlock
        {
            Text = summary,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(closedCheck);
        var dialog = new ContentDialog
        {
            Title = T("PersistentSlots.RestoreLoaderState"),
            Content = content,
            PrimaryButtonText = T("PersistentSlots.BackUpAndRestore"),
            CloseButtonText = T("Common.Cancel"),
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Close
        };
        CheckBox hotReload = HotReloadOption(services, canHotReload, closedCheck, dialog);
        content.Children.Add(hotReload);
        closedCheck.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        closedCheck.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = hotReload.IsChecked == true;
        bool restore = await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
        return (restore, hotReload.IsChecked == true);
    }

    /// <summary>Restore a user.ini snapshot: the chosen snapshot (null when cancelled), and whether to apply it while the game runs.</summary>
    public static async Task<(UserIniSnapshot? Snapshot, bool HotReload)> ChooseSnapshotAsync(AppServices services, IReadOnlyList<UserIniSnapshot> snapshots, string userIni, bool canHotReload)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var choices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (UserIniSnapshot snapshot in snapshots)
        {
            choices.Items.Add(new ComboBoxItem { Content = $"{snapshot.SavedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {snapshot.Name}", Tag = snapshot });
        }
        choices.SelectedIndex = 0;
        var closed = new CheckBox { Content = T("Common.IHaveFullyClosedTheGame") };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = T("PersistentVariants.RestoreSnapshotExplanation", userIni), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(choices);
        panel.Children.Add(closed);
        var dialog = new ContentDialog
        {
            Title = T("PersistentVariants.RestoreUserIniSnapshot"),
            Content = panel,
            PrimaryButtonText = T("PersistentSlots.BackUpAndRestore"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };
        CheckBox hotReload = HotReloadOption(services, canHotReload, closed, dialog);
        panel.Children.Add(hotReload);
        closed.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        closed.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = hotReload.IsChecked == true;
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary || choices.SelectedItem is not ComboBoxItem { Tag: UserIniSnapshot chosen })
            return (null, false);
        return (chosen, hotReload.IsChecked == true);
    }

    // "Apply while the game runs": with it the game need not be closed.
    private static CheckBox HotReloadOption(AppServices services, bool canHotReload, CheckBox closedCheck, ContentDialog dialog)
    {
        string T(string key) => services.Language.T(key);
        var hot = new CheckBox
        {
            Content = canHotReload ? T("HotReload.ApplyWhileRunning") : T("HotReload.ApplyWhileRunningUnavailable"),
            IsEnabled = canHotReload
        };
        void Update(object sender, RoutedEventArgs e) => dialog.IsPrimaryButtonEnabled = closedCheck.IsChecked == true || hot.IsChecked == true;
        hot.Checked += Update;
        hot.Unchecked += Update;
        return hot;
    }
}
