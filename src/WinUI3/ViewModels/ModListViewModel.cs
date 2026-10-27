using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mod workspace's middle column: which Mods are shown, which one is selected and how
/// (file list, covers or presets). The window still scans the repository, filters by search
/// and tags, and runs every deployment; it keeps this state here instead of in its own fields.
/// </summary>
public sealed partial class ModListViewModel : ObservableObject
{
    private SecondLevelFolderItem? _selectedMod;
    private RepositoryWorkspaceView _view;
    private ModListEmptyKind _emptyKind = ModListEmptyKind.NoCharacter;
    private ModStateFilter _stateFilter;

    /// <summary>The selected character's Mods that pass the search and tag filters.</summary>
    public BatchObservableCollection<SecondLevelFolderItem> VisibleMods { get; } = [];

    public SecondLevelFolderItem? SelectedMod { get => _selectedMod; set => SetProperty(ref _selectedMod, value); }

    public RepositoryWorkspaceView View { get => _view; set => SetProperty(ref _view, value); }

    /// <summary>The state chips above the list: all Mods, the deployed ones, or the rest.</summary>
    public ModStateFilter StateFilter { get => _stateFilter; set => SetProperty(ref _stateFilter, value); }

    public ModListEmptyKind EmptyKind { get => _emptyKind; private set => SetProperty(ref _emptyKind, value); }

    // The catalog changes in bursts while a repository is scanned; the view is rebuilt once per
    // burst, and skipped when a search already applied the latest catalog.
    public int CatalogRevision { get; private set; }

    public int ViewRevision { get; private set; } = -1;

    public bool IsCurrent => ViewRevision == CatalogRevision;

    public void MarkCatalogChanged() => CatalogRevision++;

    /// <summary>Shows these Mods and records which catalog they came from.</summary>
    public void ShowMods(IEnumerable<SecondLevelFolderItem> visible)
    {
        VisibleMods.ReplaceAll(visible);
        ViewRevision = CatalogRevision;
    }

    /// <summary>Call after the shown Mods, the catalog or the selected character change.</summary>
    public void UpdateEmptyKind(int totalMods, bool hasCharacter)
        => EmptyKind = RepositoryWorkspacePolicy.EmptyKind(totalMods, VisibleMods.Count, hasCharacter);

    public bool IsSelected(SecondLevelFolderItem mod) => ReferenceEquals(mod, _selectedMod);
}
