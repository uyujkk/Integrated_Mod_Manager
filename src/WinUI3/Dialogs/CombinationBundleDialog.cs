using IntegratedModManager.Core.Layout;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>The preview before a combination bundle is installed (moved from the window in stage J8).</summary>
internal static class CombinationBundleDialog
{
    public static async Task<(bool Install, bool Restore)> ConfirmInstallAsync(AppServices services, CombinationBundleImport import, string currentGame)
    {
        string T(string key, params object?[] values) => services.Language.T(key, values);
        var manifest = import.Manifest;
        DialogViewport viewport = services.Dialogs.Viewport;
        var panel = new Grid { RowSpacing = 10, MaxWidth = viewport.MaxWidth, Height = viewport.ContentHeight };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        string summary = T("CombinationBundles.ModsValues", manifest.Name, manifest.Mods.Count, manifest.Mods.Sum(mod => mod.Values.Count));
        summary += T("CombinationBundles.InstallIntoTheCurrentRepositoryAnd");
        if (manifest.Game.Length > 0) summary += "\n" + T("CombinationBundles.BundleGame") + manifest.Game;
        if (manifest.Game.Length > 0 && currentGame.Length > 0 && !manifest.Game.Equals(currentGame, StringComparison.OrdinalIgnoreCase))
            summary += T("CombinationBundles.WarningGameLabelDiffersFromThis");
        if (manifest.Mods.Count == 0) summary += T("CombinationBundles.EmptySetRestoringItRemovesRecognized");
        panel.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
        // Grouped by what installing does with each file (J8): added, kept (identical files reused), skipped (conflicts).
        PreviewGroup Group(Func<int, string> header, string action, CombinationBundleDisposition? disposition)
        {
            var rows = import.Preview.Where(item => disposition is null
                    ? item.Disposition is not (CombinationBundleDisposition.Install or CombinationBundleDisposition.Reuse)
                    : item.Disposition == disposition)
                .Select(item => new CombinationPreviewRow(item.RelativePath, "", action)).ToArray();
            return new PreviewGroup(header(rows.Length), rows);
        }
        ListView list = DialogLayout.GroupedPreview(
        [
            Group(n => T("Dialogs.PreviewAdded", n), T("CombinationBundles.NewInstall"), CombinationBundleDisposition.Install),
            Group(n => T("Dialogs.PreviewKept", n), T("CombinationBundles.IdenticalFilesReuse"), CombinationBundleDisposition.Reuse),
            Group(n => T("Dialogs.PreviewSkipped", n), T("CombinationBundles.PathContentConflictBlocked"), null)
        ], DialogLayout.PreviewRowTemplate());
        Grid.SetRow(list, 1); panel.Children.Add(list);
        var restore = new CheckBox { Content = T("CombinationBundles.ReviewDeploymentAndStateRestoreAfter"),
            IsChecked = true, IsEnabled = import.CanInstall };
        Grid.SetRow(restore, 2); panel.Children.Add(restore);
        var dialog = new ContentDialog
        {
            Title = T("CombinationBundles.BundleInstallationPreview"), Content = panel,
            PrimaryButtonText = T("CombinationBundles.InstallAndSavePreset"), CloseButtonText = T("Common.Cancel"),
            IsPrimaryButtonEnabled = import.CanInstall, DefaultButton = ContentDialogButton.Close
        };
        dialog.Resources["ContentDialogMaxWidth"] = viewport.MaxWidth;
        return (await services.Dialogs.ShowAsync(dialog) == ContentDialogResult.Primary, restore.IsChecked == true);
    }
}
