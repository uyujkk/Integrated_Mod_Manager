using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>Batch downloads of saved Mods: reading their file lists, then the review (moved from the window in stage J8).</summary>
internal static class BatchDownloadDialogs
{
    /// <summary>One saved Mod in the review, prepared by the window.</summary>
    public sealed record Row(object Key, string Title, string IncludeLabel, string? Error, bool Enabled, bool Checked,
        IReadOnlyList<(string Label, OnlineDownloadCandidate File)> Files,
        IReadOnlyList<(string Label, object Tag)> Folders, int FolderIndex, string? FolderNote);

    /// <summary>The approved rows with their file and folder choice.</summary>
    public sealed record Approved(object Key, OnlineDownloadCandidate File, object Folder);

    /// <summary>"Reading file lists": shown while the window reads them; closing it cancels unless they are read.</summary>
    public static (ContentDialog Dialog, TextBlock Progress) Preparing(AppServices services)
    {
        string T(string key) => services.Language.T(key);
        var progress = new TextBlock { Text = T("BatchDownloads.ReadingFileLists"), TextWrapping = TextWrapping.Wrap };
        var progressDialog = new ContentDialog { Title = T("BatchDownloads.PrepareBatchDownloads"), Content = progress, CloseButtonText = T("Common.Cancel") };
        return (progressDialog, progress);
    }

    /// <summary>The review; null when cancelled. <paramref name="duplicateKind"/> classifies a row's chosen file.</summary>
    public static async Task<(IReadOnlyList<Approved> Rows, bool AllowDuplicates)?> ReviewAsync(AppServices services, string repositoryName, string repositoryPath,
        IReadOnlyList<Row> rows, Func<object, OnlineDownloadCandidate?, OnlineDuplicateKind> duplicateKind, Brush rowBorder, double windowWidth, double windowHeight)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var body = new StackPanel { Spacing = 14, MinWidth = 320, MaxWidth = 620 };
        body.Children.Add(new TextBlock { Text = T("BatchDownloads.RepositoryReviewFilesAndDestinationsBefore", repositoryName, repositoryPath), TextWrapping = TextWrapping.Wrap });
        var allowDuplicates = new CheckBox { Content = T("BatchDownloads.AllowSeparateCopiesOfInstalledEntries") };
        body.Children.Add(allowDuplicates);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap }; body.Children.Add(summary);
        var plans = new List<(Row Data, CheckBox Include, ComboBox File, ComboBox Folder, TextBlock Status)>();
        var dialog = new ContentDialog { Title = T("BatchDownloads.ReviewBatchDownloads"), PrimaryButtonText = T("BatchDownloads.AddToDownloadQueue"), CloseButtonText = T("Common.Cancel"),
            Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(180, windowHeight - 280) } };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Min(760, Math.Max(400, windowWidth - 80));
        void Validate()
        {
            int count = 0, duplicateCount = 0;
            foreach (var plan in plans)
            {
                var file = (plan.File.SelectedItem as ComboBoxItem)?.Tag as OnlineDownloadCandidate;
                var kind = duplicateKind(plan.Data.Key, file);
                plan.Status.Text = plan.Data.Error ?? (file is null ? T("BatchDownloads.NoSupportedArchiveSkipped") : kind switch
                {
                    OnlineDuplicateKind.SameFile => T("BatchDownloads.SameFileInstalledASeparateCopy"),
                    OnlineDuplicateKind.OtherFile => T("BatchDownloads.AnotherFileOrVariantIsInstalled"),
                    OnlineDuplicateKind.UnknownFile => T("BatchDownloads.EntryInstalledTheOlderRecordCannot"),
                    _ => T("BatchDownloads.Ready")
                });
                if (plan.Include.IsChecked != true || !plan.Include.IsEnabled) continue;
                count++;
                if (kind != OnlineDuplicateKind.None) duplicateCount++;
            }
            summary.Text = T("BatchDownloads.SelectedHaveExistingInstallations", count, duplicateCount);
            dialog.IsPrimaryButtonEnabled = count > 0 && (duplicateCount == 0 || allowDuplicates.IsChecked == true);
        }
        foreach (var data in rows)
        {
            var panel = new StackPanel { Spacing = 6 };
            var file = new ComboBox { Header = T("BatchDownloads.DownloadFile"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var candidate in data.Files) file.Items.Add(new ComboBoxItem { Content = candidate.Label, Tag = candidate.File });
            if (file.Items.Count > 0) file.SelectedIndex = 0;
            var include = new CheckBox { Content = data.IncludeLabel, IsEnabled = data.Enabled, IsChecked = data.Checked };
            panel.Children.Add(include); panel.Children.Add(file);
            var folder = new ComboBox { Header = T("BatchDownloads.DownloadAndExtractInto"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in data.Folders) folder.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Tag });
            folder.SelectedIndex = data.FolderIndex;
            panel.Children.Add(folder);
            if (data.FolderNote is not null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = data.FolderNote,
                    TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75
                });
            }
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
            AutomationProperties.SetName(file, T("BatchDownloads.DownloadFile2") + data.Title);
            AutomationProperties.SetName(folder, T("BatchDownloads.DownloadDestination") + data.Title);
            plans.Add((data, include, file, folder, status));
            include.Click += (_, _) => Validate(); file.SelectionChanged += (_, _) => Validate();
            body.Children.Add(new Border { Child = panel, Padding = new Thickness(12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), BorderBrush = rowBorder });
        }
        allowDuplicates.Click += (_, _) => Validate();
        Validate();
        if (await services.Dialogs.ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        var chosen = plans.Where(plan => plan.Include.IsChecked == true && plan.Include.IsEnabled).Select(plan => new Approved(
            plan.Data.Key, (OnlineDownloadCandidate)((ComboBoxItem)plan.File.SelectedItem).Tag,
            ((ComboBoxItem)plan.Folder.SelectedItem).Tag)).ToArray();
        return (chosen, allowDuplicates.IsChecked == true);
    }
}
