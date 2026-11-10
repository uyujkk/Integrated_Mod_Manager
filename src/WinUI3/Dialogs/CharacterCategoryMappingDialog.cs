using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Correcting which online category a wiki character maps to (moved from the window in stage J8).</summary>
internal static class CharacterCategoryMappingDialog
{
    /// <summary>Primary saves the category ID for the character, Secondary clears its mapping, None cancels.</summary>
    public static async Task<(ContentDialogResult Result, OnlineCharacterOption? Character, string CategoryId)> ShowAsync(AppServices services,
        IReadOnlyDictionary<string, string> mappings, IReadOnlyList<OnlineCharacterOption> wikiCharacters, IReadOnlyList<OnlineCharacterOption> categoryCandidates)
    {
        string T(string key) => services.Language.T(key);
        ComboBox wikiCharacterBox = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = T("Repositories.SelectAWikiCharacter")
        };
        foreach (OnlineCharacterOption character in wikiCharacters)
        {
            wikiCharacterBox.Items.Add(new ComboBoxItem
            {
                Content = $"{character.DisplayNameZh} / {character.DisplayNameEn}",
                Tag = character
            });
        }

        ComboBox gameBananaCategoryBox = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = categoryCandidates.Count == 0
                ? T("Repositories.NoCategoriesDiscoveredEnterAnID")
                : T("Repositories.SelectADiscoveredGameBananaCategory")
        };
        foreach (OnlineCharacterOption candidate in categoryCandidates)
        {
            gameBananaCategoryBox.Items.Add(new ComboBoxItem
            {
                Content = $"{candidate.DisplayNameEn} · {candidate.CategoryId}",
                Tag = candidate
            });
        }

        TextBox categoryIdBox = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = T("Repositories.GameBananaCharacterCategoryID")
        };
        TextBlock mappingStatus = new()
        {
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap
        };

        void SyncSelectedWikiCharacter()
        {
            if (wikiCharacterBox.SelectedItem is not ComboBoxItem { Tag: OnlineCharacterOption character })
            {
                categoryIdBox.Text = string.Empty;
                mappingStatus.Text = T("Repositories.SelectTheWikiCharacterToCorrect");
                return;
            }

            string mappingKey = OnlineCharacterCatalogMatcher.MappingKey(character);
            bool hasManualMapping = mappings.TryGetValue(mappingKey, out string? mappedCategoryId)
                && !string.IsNullOrWhiteSpace(mappedCategoryId);
            categoryIdBox.Text = hasManualMapping ? mappedCategoryId! : character.CategoryId;
            gameBananaCategoryBox.SelectedItem = gameBananaCategoryBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is OnlineCharacterOption candidate
                    && string.Equals(candidate.CategoryId, categoryIdBox.Text, StringComparison.OrdinalIgnoreCase));
            mappingStatus.Text = hasManualMapping
                ? T("Repositories.AManualMappingIsActiveAnd")
                : string.IsNullOrWhiteSpace(character.CategoryId)
                    ? T("Repositories.NoCategoryHasBeenMatchedAutomatically")
                    : T("Repositories.ThisIsAnAutomaticMatchSaving");
        }

        wikiCharacterBox.SelectionChanged += (_, _) => SyncSelectedWikiCharacter();
        gameBananaCategoryBox.SelectionChanged += (_, _) =>
        {
            if (gameBananaCategoryBox.SelectedItem is ComboBoxItem { Tag: OnlineCharacterOption candidate })
            {
                categoryIdBox.Text = candidate.CategoryId;
            }
        };
        wikiCharacterBox.SelectedIndex = 0;

        StackPanel panel = new() { Spacing = 10, MinWidth = 520, MaxWidth = 620 };
        panel.Children.Add(new TextBlock
        {
            Text = T("Repositories.WhenWikiAndGameBananaNamesDiffer"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(DialogLayout.LabeledEditor(T("Repositories.WikiCharacter"), wikiCharacterBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Repositories.DiscoveredCategory"), gameBananaCategoryBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Common.CategoryID"), categoryIdBox));
        panel.Children.Add(mappingStatus);

        ContentDialog dialog = new()
        {
            Title = T("Common.CharacterCategoryMapping"),
            Content = panel,
            PrimaryButtonText = T("Repositories.SaveMapping"),
            SecondaryButtonText = T("Repositories.ClearMapping"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await services.Dialogs.ShowAsync(dialog);
        return (result, (wikiCharacterBox.SelectedItem as ComboBoxItem)?.Tag as OnlineCharacterOption, categoryIdBox.Text);
    }
}
