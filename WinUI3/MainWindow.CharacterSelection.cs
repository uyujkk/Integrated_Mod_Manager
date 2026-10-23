using IntegratedModManager.Core.Deployment;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

// Single or multiple selection per character folder (CharacterSelectionPolicy). With single
// selection, enabling a Mod as a directory link replaces the links of its sibling Mods; copy
// mode never removes sibling copies.
public sealed partial class MainWindow
{
    private static bool AllowsMultipleMods(WorkspaceRepository repository, string modPath)
    {
        string? characterFolder = ModDeploymentLayout.CharacterFolder(repository.SourcePath, modPath);
        return characterFolder is not null && CharacterSelectionPolicy.AllowsMultiple(
            Path.GetFileName(characterFolder), repository.AllowMultipleModsPerCharacter, repository.CharacterSelectionOverrides);
    }

    private void AddCharacterSelectionMenuItem(MenuFlyout menu, FirstLevelFolderItem item)
    {
        WorkspaceRepository? repository = GetSelectedRepository();
        if (repository is null)
        {
            return;
        }

        var toggle = new ToggleMenuFlyoutItem
        {
            Text = T("CharacterSelection.AllowSeveralMods"),
            IsChecked = CharacterSelectionPolicy.AllowsMultiple(item.Name, repository.AllowMultipleModsPerCharacter, repository.CharacterSelectionOverrides)
        };
        toggle.Click += (_, _) =>
        {
            repository.CharacterSelectionOverrides ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in repository.CharacterSelectionOverrides.Keys.Where(name => string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                repository.CharacterSelectionOverrides.Remove(name);
            }
            // Store only differences from the repository default, so changing the default applies to the rest.
            if (toggle.IsChecked != repository.AllowMultipleModsPerCharacter)
            {
                repository.CharacterSelectionOverrides[item.Name] = toggle.IsChecked;
            }
            SaveShellConfig();
            StatusTextBlock.Text = toggle.IsChecked
                ? T("CharacterSelection.NowAllowsSeveral", item.Name)
                : T("CharacterSelection.NowSingle", item.Name);
        };
        menu.Items.Add(toggle);
    }

    // Asks before restoring a combination that enables several Mods of a single-selection character.
    private async Task<bool> ConfirmSingleSelectionViolationsAsync(WorkspaceRepository repository, IEnumerable<string> modSourcePaths)
    {
        IReadOnlyList<string> characters = CharacterSelectionPolicy.SingleSelectionViolations(
            repository.SourcePath, modSourcePaths, repository.AllowMultipleModsPerCharacter, repository.CharacterSelectionOverrides);
        return characters.Count == 0 || await ShowConfirmAsync(
            T("CombinationPresets.SingleSelectionCharactersHaveSeveralMods", string.Join("\n", characters)),
            T("CombinationPresets.SeveralModsForOneCharacter"));
    }
}
