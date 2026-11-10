using IntegratedModManager.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Where an online download goes in the repository (moved from the window in stage J8).</summary>
internal static class DownloadLocationDialog
{
    /// <summary>
    /// Primary with the chosen option's tag; Secondary to choose another location; None when cancelled.
    /// </summary>
    public static async Task<(ContentDialogResult Result, object? Choice)> ShowAsync(AppServices services, string repositoryPath, string destinationHint,
        IReadOnlyList<(string Label, object Tag)> options, int suggestedIndex)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        DialogViewport viewport = services.Dialogs.Viewport;
        ComboBox folderComboBox = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 40
        };
        foreach ((string label, object tag) in options)
            folderComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        folderComboBox.SelectedIndex = suggestedIndex;

        ContentDialog dialog = new()
        {
            Title = T("OnlineDownloadFlow.ChooseDownloadLocation"),
            Content = new ScrollViewer
            {
                MaxHeight = viewport.ContentHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel
                {
                    Width = Math.Min(620, viewport.MaxWidth - 48),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = T("OnlineDownloadFlow.CurrentModRepositoryFolderASeparate", repositoryPath, destinationHint),
                            TextWrapping = TextWrapping.Wrap
                        },
                        folderComboBox
                    }
                }
            },
            PrimaryButtonText = T("OnlineDownloadFlow.UseSelectedFolder"),
            SecondaryButtonText = T("OnlineDownloadFlow.ChooseAnotherLocation"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Resources["ContentDialogMaxWidth"] = viewport.MaxWidth;

        ContentDialogResult result = await services.Dialogs.ShowAsync(dialog);
        return (result, (folderComboBox.SelectedItem as ComboBoxItem)?.Tag);
    }
}
