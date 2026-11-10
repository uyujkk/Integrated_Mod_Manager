using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>Downloading an update and replacing the whole local Mod folder with it (moved from the window in stage J8).</summary>
internal static class ModUpdateReplaceDialog
{
    /// <summary>The file to download, or null when cancelled.</summary>
    public static async Task<OnlineDownloadCandidate?> ChooseAsync(AppServices services, string modPath, IReadOnlyList<OnlineDownloadCandidate> candidates,
        string? currentFileId, Func<long, string> formatSize)
    {
        string T(string key) => services.Language.T(key);
        var picker = new ComboBox { Header = T("ModUpdates.ChooseUpdateFileVariant"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var file in candidates)
            picker.Items.Add(new ComboBoxItem { Content = $"{file.FileName} ({formatSize(file.FileSizeBytes)})", Tag = file });
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is OnlineDownloadCandidate file
            && !string.IsNullOrWhiteSpace(currentFileId) && file.FileId == currentFileId);
        if (picker.SelectedItem is null && picker.Items.Count == 1) picker.SelectedIndex = 0;
        var body = new StackPanel { Spacing = 12, MaxWidth = 560 };
        body.Children.Add(new TextBlock
        {
            Text = modPath + T("ModUpdates.TheEntireLocalModFolderWill"),
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(picker);
        var dialog = new ContentDialog
        {
            Title = T("ModUpdates.DownloadAndReplaceMod"), Content = body,
            PrimaryButtonText = T("Common.DownloadAndReplace"), CloseButtonText = T("Common.Cancel"),
            IsPrimaryButtonEnabled = picker.SelectedItem is not null
        };
        DialogService.MarkDangerous(dialog);
        picker.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = picker.SelectedItem is not null;
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return picker.SelectedItem is ComboBoxItem { Tag: OnlineDownloadCandidate selected } ? selected : null;
    }
}
