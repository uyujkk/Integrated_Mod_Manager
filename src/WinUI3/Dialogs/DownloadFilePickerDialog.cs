using System.Globalization;
using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Choosing which file of an online entry to download (moved from the window in stage J8).</summary>
internal static class DownloadFilePickerDialog
{
    public static async Task<OnlineDownloadCandidate?> ChooseAsync(AppServices services, OnlineModCard mod, IReadOnlyList<OnlineDownloadCandidate> candidates)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        DialogViewport viewport = services.Dialogs.Viewport;
        var fileList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        OnlineDownloadCandidate? defaultFile = OnlineDownloadSelectionPolicy.SelectDefault(candidates);
        ListViewItem? defaultItem = null;
        foreach (OnlineDownloadCandidate candidate in candidates)
        {
            string fileName = string.IsNullOrWhiteSpace(candidate.FileName)
                ? T("OnlineDetails.File", candidate.FileId)
                : candidate.FileName;

            var title = new TextBlock
            {
                Text = fileName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2
            };

            var metadata = new List<string>();
            if (!string.IsNullOrWhiteSpace(candidate.Version))
            {
                metadata.Add(T("OnlineDetails.Version", candidate.Version));
            }
            if (candidate.FileSizeBytes > 0)
            {
                metadata.Add(MainWindow.FormatFileSize(candidate.FileSizeBytes));
            }
            if (candidate.AddedAt > DateTimeOffset.MinValue)
            {
                metadata.Add(candidate.AddedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
            }
            metadata.Add(candidate.IsArchived ? T("OnlineDetails.Archived") : T("OnlineDetails.Current"));
            if (!candidate.IsSupportedArchive)
            {
                metadata.Add(T("OnlineDetails.CannotAutoExtract"));
            }

            var details = new StackPanel { Spacing = 3 };
            details.Children.Add(title);
            details.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", metadata),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(candidate.Description))
            {
                details.Children.Add(new TextBlock
                {
                    Text = candidate.Description,
                    Style = (Style)Application.Current.Resources["MutedTextStyle"],
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 2
                });
            }

            var item = new ListViewItem
            {
                Content = details,
                Tag = candidate,
                Padding = new Thickness(10, 8, 10, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsEnabled = candidate.IsSupportedArchive
            };
            fileList.Items.Add(item);
            if (ReferenceEquals(candidate, defaultFile))
            {
                defaultItem = item;
            }
        }

        fileList.SelectedItem = defaultItem
            ?? fileList.Items.OfType<ListViewItem>().FirstOrDefault(item => item.IsEnabled);

        var content = new Grid
        {
            Width = Math.Min(620, viewport.MaxWidth - 48),
            Height = Math.Min(480, viewport.ContentHeight),
            RowSpacing = 10
        };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new TextBlock
        {
            Text = T("OnlineDetails.TheNewestCurrentArchiveIsSelected"),
            Style = (Style)Application.Current.Resources["MutedTextStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(fileList, 1);
        content.Children.Add(fileList);

        var dialog = new ContentDialog
        {
            Title = T("OnlineDetails.SelectDownloadFile", mod.Title),
            Content = content,
            PrimaryButtonText = T("OnlineDetails.DownloadSelected"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = fileList.SelectedItem is ListViewItem
        };
        fileList.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = fileList.SelectedItem is ListViewItem { IsEnabled: true };

        ContentDialogResult result = await services.Dialogs.ShowAsync(dialog);
        return result == ContentDialogResult.Primary
            && fileList.SelectedItem is ListViewItem { Tag: OnlineDownloadCandidate selected }
                ? selected
                : null;
    }
}
