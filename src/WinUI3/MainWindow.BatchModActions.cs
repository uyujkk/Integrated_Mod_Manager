using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Several Mods of the current list at once: add them to a combination preset, copy them to a folder, or delete them.
// Tags already have their own batch dialog (MainWindow.LocalTags.cs).
public sealed partial class MainWindow
{
    private Button? _batchModsButton;

    private async Task ShowBatchModActionsAsync()
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        SecondLevelFolderItem[] items = _modList.VisibleMods.ToArray();
        if (repository is null) return;
        if (items.Length == 0) { await ShowMessageAsync(T("LocalTags.NoModsInTheCurrentFilter"), T("BatchMods.Title")); return; }

        var action = new ComboBox { Header = T("LocalTags.Action"), HorizontalAlignment = HorizontalAlignment.Stretch };
        action.Items.Add(T("BatchMods.AddToPreset"));
        action.Items.Add(T("BatchMods.CopyToFolder"));
        action.Items.Add(T("BatchMods.Delete"));
        action.Items.Add(T("BatchMods.ExportBundle"));
        action.SelectedIndex = 0;
        ModConfigurationProfile[] profiles = [.. GetCurrentRepositoryProfiles()];
        var profile = new ComboBox { Header = T("BatchMods.Preset"), HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = T("ConfigurationProfiles.NoProfilesForThisRepository") };
        foreach (ModConfigurationProfile item in profiles) profile.Items.Add(new ComboBoxItem { Content = item.Name, Tag = item.Id });
        profile.SelectedItem = profile.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == _selectedConfigurationProfileId) ?? profile.Items.FirstOrDefault();
        action.SelectionChanged += (_, _) => profile.Visibility = action.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;

        var body = new StackPanel { Spacing = 10, MinWidth = 300, MaxWidth = 580 };
        body.Children.Add(action);
        body.Children.Add(profile);
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Add(validation);
        var selectAll = new CheckBox { Content = T("LocalTags.SelectAllListedMods") };
        body.Children.Add(selectAll);
        var checks = items.Select(mod => (Mod: mod, Check: new CheckBox { Content = mod.Name, IsChecked = ReferenceEquals(mod, _modList.SelectedMod) })).ToArray();
        foreach (var item in checks) body.Children.Add(item.Check);
        selectAll.Click += (_, _) => { foreach (var item in checks) item.Check.IsChecked = selectAll.IsChecked == true; };
        var dialog = new ContentDialog
        {
            Title = T("BatchMods.Title"), Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(180, RootGrid.ActualHeight - 280) },
            PrimaryButtonText = T("BatchMods.Run"), CloseButtonText = T("Common.Cancel"), XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!checks.Any(item => item.Check.IsChecked == true)) { validation.Text = T("LocalTags.SelectAtLeastOneMod"); args.Cancel = true; }
            else if (action.SelectedIndex == 0 && profile.SelectedItem is null) { validation.Text = T("BatchMods.ChooseAPreset"); args.Cancel = true; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || !ReferenceEquals(GetSelectedRepository(), repository)) return;
        SecondLevelFolderItem[] chosen = checks.Where(item => item.Check.IsChecked == true).Select(item => item.Mod).ToArray();
        switch (action.SelectedIndex)
        {
            case 0:
                AddModsToPreset(repository, chosen, (string)((ComboBoxItem)profile.SelectedItem!).Tag);
                break;
            case 1:
                await CopyModsToFolderAsync(chosen);
                break;
            case 2:
                await DeleteModsAsync(repository, chosen);
                break;
            default:
                // Files only: loader values belong to presets and are exported from there.
                await ExportModsAsBundleAsync(repository.SourcePath, T("BatchMods.BundleName", chosen.Length), repository.OnlineGameName, false,
                    chosen.Select(mod => new CombinationPresetState(Path.GetRelativePath(repository.SourcePath, mod.Path), [])).ToArray(),
                    T("BatchMods.ExportBundleConfirm", chosen.Length));
                break;
        }
    }

    // The preset restores this set of Mods next time; loader values for them are not captured here.
    private void AddModsToPreset(WorkspaceRepository repository, IReadOnlyList<SecondLevelFolderItem> mods, string profileId)
    {
        if (_configurationProfiles.FirstOrDefault(item => item.Id == profileId) is not { } profile) return;
        int before = profile.ModRelativePaths.Count;
        foreach (SecondLevelFolderItem mod in mods)
        {
            string relative = Path.GetRelativePath(repository.SourcePath, mod.Path);
            if (!profile.ModRelativePaths.Contains(relative, StringComparer.OrdinalIgnoreCase)) profile.ModRelativePaths.Add(relative);
        }
        int added = profile.ModRelativePaths.Count - before;
        if (added > 0)
        {
            profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
            SaveShellConfig();
            RefreshConfigurationProfiles();
        }
        StatusTextBlock.Text = T("BatchMods.AddedToPreset", added, profile.Name);
    }

    private async Task CopyModsToFolderAsync(IReadOnlyList<SecondLevelFolderItem> mods)
    {
        if (await PickFolderAsync() is not { } folder) return;
        int copied = await Task.Run(() =>
        {
            int count = 0;
            foreach (SecondLevelFolderItem mod in mods)
            {
                string target = Path.Combine(folder, mod.Name);
                for (int suffix = 2; Directory.Exists(target) || File.Exists(target); suffix++) target = Path.Combine(folder, $"{mod.Name} ({suffix})");
                CopyDirectoryTree(mod.Path, target);
                count++;
            }
            return count;
        });
        StatusTextBlock.Text = T("BatchMods.Copied", copied, folder);
    }

    private async Task DeleteModsAsync(WorkspaceRepository repository, IReadOnlyList<SecondLevelFolderItem> mods)
    {
        string loaderRoot = (repository.TargetPath ?? string.Empty).Trim();
        var links = new Dictionary<SecondLevelFolderItem, IReadOnlyList<string>>();
        // A Mod whose links could not be listed is kept: deleting it could leave links to a missing folder. That
        // includes a loader folder that is set but cannot be reached right now (issue #109).
        var unverified = new List<string>();
        foreach (SecondLevelFolderItem mod in mods)
        {
            LinkScanResult scan = await Task.Run(() => DirectoryLinkDeployment.ScanLinksTargeting(mod.Path, loaderRoot));
            if (scan.IsComplete)
            {
                links[mod] = scan.Links;
                continue;
            }
            LogApplicationIssue("Find deployed links before delete", new IOException($"{scan.Status}: {scan.Detail}"));
            unverified.Add(mod.Name + ": " + (scan.Status == LinkScanStatus.Unavailable ? T("BatchMods.LoaderUnavailable", scan.Detail) : scan.Detail));
        }
        SecondLevelFolderItem[] deletable = mods.Where(links.ContainsKey).ToArray();
        string unverifiedNote = unverified.Count == 0 ? string.Empty : T("BatchMods.LinksUnknown", string.Join("\n", unverified));
        if (deletable.Length == 0)
        {
            await ShowMessageAsync(unverifiedNote, T("LocalLibrary.DeleteFailed"));
            return;
        }
        int linkCount = deletable.Sum(mod => links[mod].Count);
        string names = string.Join("\n", deletable.Select(mod => mod.Name));
        string confirm = T("BatchMods.ConfirmDelete", deletable.Length, linkCount, names) + (unverified.Count == 0 ? string.Empty : "\n\n" + unverifiedNote);
        if (!await ShowConfirmAsync(confirm, T("LocalLibrary.ConfirmDelete"))) return;

        string selectedFirstLevelPath = _characters.Selected?.Path ?? string.Empty;
        var failed = new List<string>();
        foreach (SecondLevelFolderItem mod in deletable)
        {
            try { RemoveModAndItsLinks(mod, links[mod]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                LogApplicationIssue("Batch delete Mod", ex);
                failed.Add(mod.Name + ": " + ex.Message);
            }
        }
        SaveConfig();
        await RefreshListsAsync();
        SelectFirstLevelByPath(selectedFirstLevelPath);
        StatusTextBlock.Text = T("BatchMods.Deleted", deletable.Length - failed.Count);
        if (failed.Count > 0) await ShowMessageAsync(string.Join("\n", failed), T("LocalLibrary.DeleteFailed"));
    }
}
