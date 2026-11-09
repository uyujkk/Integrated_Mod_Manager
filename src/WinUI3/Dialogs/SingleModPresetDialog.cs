using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Saving one Mod's current state as a named preset from the Shortcuts overlay (moved from the window in stage J8).</summary>
internal static class SingleModPresetDialog
{
    /// <summary>The dialog, shown in the overlay; <paramref name="isDuplicate"/> says whether a name is taken for this Mod.</summary>
    public static (ContentDialog Dialog, TextBox Name) Create(AppServices services, XamlRoot overlayRoot, string relative, Func<string, bool> isDuplicate)
    {
        string T(string key) => services.Language.T(key);
        var nameBox = new TextBox { Header = T("ShortcutPanel.SinglePresetName"), MaxLength = 120 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = relative, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = T("ShortcutPanel.SinglePresetHint"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(nameBox);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = overlayRoot, RequestedTheme = ElementTheme.Dark,
            Title = T("ShortcutPanel.SaveSingleModPreset"), Content = content,
            PrimaryButtonText = T("PersistentSlots.SaveSlot"), CloseButtonText = T("Common.Cancel"),
            IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary
        };
        nameBox.TextChanged += (_, _) =>
        {
            string candidate = nameBox.Text.Trim();
            bool duplicate = isDuplicate(candidate);
            error.Text = duplicate ? T("ShortcutPanel.SinglePresetDuplicate") : string.Empty;
            dialog.IsPrimaryButtonEnabled = candidate.Length > 0 && !duplicate;
        };
        dialog.Opened += (_, _) => nameBox.Focus(FocusState.Programmatic);
        return (dialog, nameBox);
    }
}
