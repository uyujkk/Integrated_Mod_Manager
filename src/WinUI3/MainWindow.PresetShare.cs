using IntegratedModManager.Core.Presets;

namespace ModFolderCopier.WinUI;

// Sharing a combination preset as a small file without Mod files. The receiver applies it to the Mods they
// already have; missing Mods are listed, never downloaded. Combination bundles stay the way to send files.
public sealed partial class MainWindow
{
    private async Task ExportPresetShareAsync()
    {
        if (GetSelectedRepository() is not { } repository || GetSelectedConfigurationProfile() is not { } profile) return;
        var states = profile.PersistentStates.ToDictionary(state => state.ModRelativePath, StringComparer.OrdinalIgnoreCase);
        CombinationPresetState[] mods = profile.ModRelativePaths.Select(path => new CombinationPresetState(path,
            profile.IncludesPersistentState && states.TryGetValue(path, out var state) ? state.Values.ToArray() : [])).ToArray();
        string json;
        try { json = PresetShareService.Serialize(profile.Name, repository.OnlineGameName, profile.IncludesPersistentState, mods); }
        catch (PresetShareTooLargeException)
        {
            await ShowMessageAsync(T("PresetShare.ExportTooLarge", PresetShareService.MaxBytes / 1024 / 1024), T("PresetShare.Export"));
            return;
        }
        catch (InvalidDataException ex)
        {
            await ShowMessageAsync(ex.Message, T("PresetShare.Export"));
            return;
        }
        if (await PickFolderAsync() is not { } folder) return;
        string safeName = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        string path = Path.Combine(folder, (safeName.Length == 0 ? "preset" : safeName) + PresetShareService.FileExtension);
        for (int suffix = 2; File.Exists(path); suffix++) path = Path.Combine(folder, $"{safeName} ({suffix}){PresetShareService.FileExtension}");
        await File.WriteAllTextAsync(path, json, new System.Text.UTF8Encoding(false));
        _workspaceView.StatusTextBlock.Text = T("PresetShare.Exported", profile.Name, path);
    }

    private async Task ImportPresetShareAsync()
    {
        if (_applyingCombinationProfile || _restoringPersistentRuntime || GetSelectedRepository() is not { } repository) return;
        if (await PickFileAsync([".json"]) is not { } path) return;
        PresetShareFile file;
        try
        {
            if (new FileInfo(path).Length > PresetShareService.MaxBytes) throw new InvalidDataException(T("PresetShare.TooLarge"));
            file = PresetShareService.Parse(await File.ReadAllTextAsync(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync(ex.Message, T("PresetShare.Import"));
            return;
        }
        string root = repository.SourcePath;
        string[] available = await Task.Run(() => RepositoryModRelativePaths(root));
        PresetSharePreview preview = PresetShareService.Preview(file, available, repository.OnlineGameName);

        string? chosenName = await PresetShareImportDialog.ShowAsync(_services, file, preview, repository.OnlineGameName, UniqueBundleProfileName(file.Name), RootGrid.ActualHeight);
        if (chosenName is null || !ReferenceEquals(GetSelectedRepository(), repository)) return;

        string name = UniqueBundleProfileName(chosenName);
        var profile = new ModConfigurationProfile
        {
            RepositoryId = repository.Id, Name = name,
            ModRelativePaths = preview.Matched.Select(mod => mod.RelativePath.Replace('/', Path.DirectorySeparatorChar)).ToList(),
            IncludesPersistentState = file.IncludesPersistentState,
            PersistentStates = file.IncludesPersistentState ? preview.Matched.Select(mod => new ModConfigurationPersistentState
            {
                ModRelativePath = mod.RelativePath.Replace('/', Path.DirectorySeparatorChar), Values = [.. mod.Values]
            }).ToList() : []
        };
        _configurationProfiles.Add(profile);
        _selectedConfigurationProfileId = profile.Id;
        // A state slot per Mod under the same name, so each look can also be restored on its own. Never replaces one.
        int slots = 0;
        foreach (PresetShareMod mod in preview.Matched.Where(mod => mod.Values.Count > 0))
        {
            string relative = mod.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            if (_modPersistentSlots.Any(slot => slot.RepositoryId == repository.Id && slot.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && slot.ModRelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase))) continue;
            _modPersistentSlots.Add(new ModPersistentSlot { RepositoryId = repository.Id, ModRelativePath = relative, Name = name, Values = [.. mod.Values] });
            slots++;
        }
        SaveShellConfig();
        RefreshConfigurationProfiles();
        RefreshPersistentSlotControls();
        _workspaceView.StatusTextBlock.Text = T("PresetShare.Imported", name, preview.Matched.Count, slots);
    }

    // Mods sit two levels deep: character folder, then Mod folder.
    private static string[] RepositoryModRelativePaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateDirectories(root, "*", options)
            .SelectMany(character => Directory.EnumerateDirectories(character, "*", options))
            .Select(mod => Path.GetRelativePath(root, mod)).ToArray();
    }
}
