using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Creates or edits a repository: name, paths, deployment, launcher and online category (stage J4; the
/// dashboard and the Mod workspace open it through the window).
/// </summary>
internal static class RepositoryEditorDialog
{
    public const string DefaultOnlineSourceSite = "GameBanana";

    public static async Task<RepositoryEditorResult?> ShowAsync(AppServices services, string content, string title, WorkspaceRepository initial)
    {
        string T(string key) => services.Language.T(key);
        TextBox nameBox = new()
        {
            Text = initial.Name,
            PlaceholderText = T("Dialogs.EnterRepositoryName")
        };

        TextBox sourceBox = new()
        {
            Text = initial.SourcePath,
            PlaceholderText = T("Dialogs.FirstLevelModStoragePath")
        };

        TextBox targetBox = new()
        {
            Text = initial.TargetPath,
            PlaceholderText = T("Dialogs.TargetGameModFolderPath")
        };

        CheckBox linkDeploymentBox = new()
        {
            IsChecked = initial.UseDirectoryLinks,
            Content = T("Dialogs.DeployWithDirectoryJunctionsRepositoryAnd")
        };

        CheckBox multipleModsBox = new()
        {
            IsChecked = initial.AllowMultipleModsPerCharacter,
            Content = T("Dialogs.AllowSeveralModsPerCharacterByDefault")
        };

        TextBox launcherBox = new()
        {
            Text = initial.LauncherPath,
            PlaceholderText = T("Dialogs.LauncherPathOptional")
        };

        var importerBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        importerBox.Items.Add(new ComboBoxItem { Content = T("XxmiLaunch.ImporterAutomatic"), Tag = null });
        foreach (string importer in IntegratedModManager.Core.Repository.XxmiLaunchPolicy.Importers)
            importerBox.Items.Add(new ComboBoxItem { Content = importer, Tag = importer });
        importerBox.SelectedItem = importerBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, initial.XxmiImporter, StringComparison.OrdinalIgnoreCase)) ?? importerBox.Items[0];

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
            PlaceholderText = T("Dialogs.CategoryIDForExample21842")
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
            MinHeight = 72,
            TextWrapping = TextWrapping.Wrap
        };

        StackPanel panel = new() { Spacing = 10, MaxWidth = 620 };
        panel.Children.Add(new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.RepositoryName"), nameBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.ModStoragePath"), sourceBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.TargetPath"), targetBox));
        panel.Children.Add(linkDeploymentBox);
        panel.Children.Add(multipleModsBox);
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.LauncherPath"), launcherBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("XxmiLaunch.Importer"), importerBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Common.OnlineSource"), siteBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.TargetGame"), gameBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Dialogs.CategoryID"), categoryBox));
        panel.Children.Add(DialogLayout.LabeledEditor(T("Common.Notes"), notesBox));

        panel.Children.Insert(panel.Children.Count - 1, DialogLayout.LabeledEditor(T("Dialogs.GameWiki"), wikiBox));

        ContentDialog dialog = new()
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = T("Common.Save"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = services.Dialogs.Root
        };

        ContentDialogResult result = await services.Dialogs.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return new RepositoryEditorResult
        {
            Name = nameBox.Text,
            SourcePath = sourceBox.Text,
            TargetPath = targetBox.Text,
            UseDirectoryLinks = linkDeploymentBox.IsChecked == true,
            AllowMultipleModsPerCharacter = multipleModsBox.IsChecked == true,
            LauncherPath = launcherBox.Text,
            XxmiImporter = (importerBox.SelectedItem as ComboBoxItem)?.Tag as string,
            OnlineSourceSite = siteBox.Text,
            OnlineGameName = gameBox.Text,
            OnlineCategoryId = categoryBox.Text,
            WikiUrl = wikiBox.Text,
            Notes = notesBox.Text
        };
    }
}
