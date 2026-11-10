using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Presets;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Several Mods of the current list at once: add them to a combination preset, copy them to a folder, or delete them.
// Tags already have their own batch dialog (MainWindow.LocalTags.cs).
public sealed partial class WorkspacePage
{
    private Button? _batchModsButton;

    private async Task ShowBatchModActionsAsync()
    {
        WorkspaceRepository? repository = _services.Repositories.Selected;
        SecondLevelFolderItem[] items = _modList.VisibleMods.ToArray();
        if (repository is null) return;
        if (items.Length == 0) { await ShowMessageAsync(T("LocalTags.NoModsInTheCurrentFilter"), T("BatchMods.Title")); return; }

        if (await BatchModActionsDialog.ShowAsync(_services, items, _modList.SelectedMod, [.. _host.GetCurrentRepositoryProfiles()], _host.SelectedConfigurationProfileId, _host.Root.ActualHeight)
            is not { } choice || !ReferenceEquals(_services.Repositories.Selected, repository)) return;
        SecondLevelFolderItem[] chosen = [.. choice.Mods];
        switch (choice.Action)
        {
            case 0:
                AddModsToPreset(repository, chosen, choice.ProfileId!);
                break;
            case 1:
                await CopyModsToFolderAsync(chosen);
                break;
            case 2:
                await DeleteModsAsync(repository, chosen);
                break;
            default:
                // Files only: loader values belong to presets and are exported from there.
                await _host.ExportModsAsBundleAsync(repository.SourcePath, T("BatchMods.BundleName", chosen.Length), repository.OnlineGameName, false,
                    chosen.Select(mod => new CombinationPresetState(Path.GetRelativePath(repository.SourcePath, mod.Path), [])).ToArray(),
                    T("BatchMods.ExportBundleConfirm", chosen.Length));
                break;
        }
    }

    // The preset restores this set of Mods next time; loader values for them are not captured here.
    private void AddModsToPreset(WorkspaceRepository repository, IReadOnlyList<SecondLevelFolderItem> mods, string profileId)
    {
        if (_host.ConfigurationProfiles.FirstOrDefault(item => item.Id == profileId) is not { } profile) return;
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
            _host.SaveShellConfig();
            _host.RefreshConfigurationProfiles();
        }
        StatusTextBlock.Text = T("BatchMods.AddedToPreset", added, profile.Name);
    }

    private async Task CopyModsToFolderAsync(IReadOnlyList<SecondLevelFolderItem> mods)
    {
        if (await _services.Dialogs.PickFolderAsync() is not { } folder) return;
        int copied = await Task.Run(() =>
        {
            int count = 0;
            foreach (SecondLevelFolderItem mod in mods)
            {
                string target = Path.Combine(folder, mod.Name);
                for (int suffix = 2; Directory.Exists(target) || File.Exists(target); suffix++) target = Path.Combine(folder, $"{mod.Name} ({suffix})");
                _host.CopyDirectoryTree(mod.Path, target);
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
            _host.LogApplicationIssue("Find deployed links before delete", new IOException($"{scan.Status}: {scan.Detail}"));
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
        if (!await ShowDangerConfirmAsync(confirm, T("LocalLibrary.ConfirmDelete"), T("Dialogs.Delete"))) return;

        string selectedFirstLevelPath = _host.Characters.Selected?.Path ?? string.Empty;
        var failed = new List<string>();
        foreach (SecondLevelFolderItem mod in deletable)
        {
            try { RemoveModAndItsLinks(mod, links[mod]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                _host.LogApplicationIssue("Batch delete Mod", ex);
                failed.Add(mod.Name + ": " + ex.Message);
            }
        }
        _host.SaveConfig();
        await RefreshListsAsync();
        SelectFirstLevelByPath(selectedFirstLevelPath);
        StatusTextBlock.Text = T("BatchMods.Deleted", deletable.Length - failed.Count);
        if (failed.Count > 0) await ShowMessageAsync(string.Join("\n", failed), T("LocalLibrary.DeleteFailed"));
    }
}
