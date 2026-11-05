using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>The current repository's online source, game, category, wiki and notes (Settings page).</summary>
internal static class OnlineRepositoryConfigDialog
{
    public static async Task<OnlineRepositoryConfigResult?> ShowAsync(AppServices services, WorkspaceRepository initial)
    {
        string T(string key) => services.Language.T(key);
        TextBox siteBox = new()
        {
            Text = string.IsNullOrWhiteSpace(initial.OnlineSourceSite) ? RepositoryEditorDialog.DefaultOnlineSourceSite : initial.OnlineSourceSite,
            PlaceholderText = T("Dialogs.OnlineSourceForExampleGameBanana")
        };

        TextBox gameBox = new()
        {
            Text = initial.OnlineGameName,
            PlaceholderText = T("Dialogs.TargetGameNameForExampleArknights")
        };

        TextBox categoryBox = new()
        {
            Text = initial.OnlineCategoryId,
            PlaceholderText = T("Dialogs.SkinCategoryIDForExample42770")
        };

        TextBox wikiBox = new()
        {
            Text = initial.WikiUrl,
            PlaceholderText = T("Dialogs.GameWikiURLOptional")
        };

        TextBox notesBox = new()
        {
            Text = initial.Notes,
            PlaceholderText = T("Dialogs.NotesOptional"),
            AcceptsReturn = true,
            MinHeight = 88,
            TextWrapping = TextWrapping.Wrap
        };

        StackPanel panel = new() { Spacing = 10, MaxWidth = 620 };
        panel.Children.Add(new TextBlock
        {
            Text = T("Dialogs.ThisConfigControlsTheOnlineMod"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(DialogLayout.LabeledEditor(T("Common.OnlineSource"), siteBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.TargetGame"), gameBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.SkinCategoryID"), categoryBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Common.Notes"), notesBox));

        panel.Children.Add(new TextBlock
        {
            Text = T("Dialogs.TheCurrentFetchLogicPassesThis"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        panel.Children.Insert(panel.Children.Count - 1, DialogLayout.LabeledEditor(T("Dialogs.GameWiki"), wikiBox));

        ContentDialog dialog = new()
        {
            Title = T("Dialogs.EditCurrentRepositoryOnlineCategory"),
            Content = panel,
            PrimaryButtonText = T("Common.Save"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = services.Dialogs.Root
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return new OnlineRepositoryConfigResult
        {
            OnlineSourceSite = siteBox.Text,
            OnlineGameName = gameBox.Text,
            OnlineCategoryId = categoryBox.Text,
            WikiUrl = wikiBox.Text,
            Notes = notesBox.Text
        };
    }
}
