using IntegratedModManager.Core.Deployment;
using Microsoft.UI.Xaml;
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
        toggle.Click += (_, _) => SetCharacterAllowsMultiple(repository, item, toggle.IsChecked);
        menu.Items.Add(toggle);
    }

    private void SetCharacterAllowsMultiple(WorkspaceRepository repository, FirstLevelFolderItem item, bool allowsMultiple)
    {
        repository.CharacterSelectionOverrides ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in repository.CharacterSelectionOverrides.Keys.Where(name => string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            repository.CharacterSelectionOverrides.Remove(name);
        }
        // Store only differences from the repository default, so changing the default applies to the rest.
        if (allowsMultiple != repository.AllowMultipleModsPerCharacter)
        {
            repository.CharacterSelectionOverrides[item.Name] = allowsMultiple;
        }
        SaveShellConfig();
        StatusTextBlock.Text = allowsMultiple
            ? T("CharacterSelection.NowAllowsSeveral", item.Name)
            : T("CharacterSelection.NowSingle", item.Name);
        UpdateCharacterHeaderActions();
    }

    // The header toggle and avatar button follow the selected character.
    private void UpdateCharacterHeaderActions()
    {
        if (CharacterMultipleModsToggle is null || CharacterAvatarButton is null) return;
        WorkspaceRepository? repository = GetSelectedRepository();
        var item = FirstLevelListView.SelectedItem as FirstLevelFolderItem;
        bool allows = repository is not null && item is not null
            && CharacterSelectionPolicy.AllowsMultiple(item.Name, repository.AllowMultipleModsPerCharacter, repository.CharacterSelectionOverrides);
        CharacterMultipleModsToggle.IsEnabled = item is not null && repository is not null;
        CharacterMultipleModsToggle.IsChecked = allows;
        string toggleTip = item is null
            ? T("CharacterSelection.SelectACharacterToSet")
            : allows ? T("CharacterSelection.ToggleTipOn", item.Name) : T("CharacterSelection.ToggleTipOff", item.Name);
        ToolTipService.SetToolTip(CharacterMultipleModsToggle, toggleTip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CharacterMultipleModsToggle, toggleTip);
        string avatarTip = item is null ? T("CharacterAvatars.SelectACharacterFirst") : T("CharacterAvatars.HeaderTip", item.Name);
        CharacterAvatarButton.IsEnabled = item is not null;
        ToolTipService.SetToolTip(CharacterAvatarButton, avatarTip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CharacterAvatarButton, avatarTip);
    }

    private void OnCharacterMultipleModsToggleClicked(object sender, RoutedEventArgs e)
    {
        if (GetSelectedRepository() is { } repository && FirstLevelListView.SelectedItem is FirstLevelFolderItem item)
        {
            SetCharacterAllowsMultiple(repository, item, CharacterMultipleModsToggle.IsChecked == true);
        }
        else
        {
            UpdateCharacterHeaderActions();
        }
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
