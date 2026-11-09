using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Binding a character folder to a character of the online list, for its avatar (moved from the window in stage J8).</summary>
internal static class CharacterAvatarDialog
{
    /// <summary>The chosen character key ("" for automatic by folder name), or null when cancelled.</summary>
    public static async Task<string?> ChooseAsync(AppServices services, string folderName, IReadOnlyList<OnlineCharacterOption> catalog, string? current)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var choices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        choices.Items.Add(new ComboBoxItem { Content = T("CharacterAvatars.AutomaticByFolderName"), Tag = string.Empty });
        foreach (OnlineCharacterOption character in catalog)
        {
            string label = string.Equals(character.DisplayNameZh, character.DisplayNameEn, StringComparison.OrdinalIgnoreCase)
                ? character.DisplayNameZh
                : $"{character.DisplayNameZh} / {character.DisplayNameEn}";
            choices.Items.Add(new ComboBoxItem { Content = label, Tag = character.Key });
        }
        choices.SelectedItem = choices.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(choice => string.Equals((string)choice.Tag, current ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? choices.Items[0];

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = T("CharacterAvatars.ChooseTheCharacterForFolder", folderName), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(choices);
        var dialog = new ContentDialog
        {
            Title = T("CharacterAvatars.BindToCharacter"),
            Content = panel,
            PrimaryButtonText = T("Dialogs.OK"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return choices.SelectedItem is ComboBoxItem { Tag: string characterKey } ? characterKey : string.Empty;
    }
}
