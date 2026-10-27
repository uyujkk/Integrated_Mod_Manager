using CommunityToolkit.Mvvm.ComponentModel;
using IntegratedModManager.Core.Repository;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The Mod workspace's character column: every character folder of the repository, the ones
/// the search shows, and the selected one. The list view's selection is the source; the window
/// copies it here when it changes, so the rest of the window reads the selection from here.
/// </summary>
public sealed partial class CharacterListViewModel : ObservableObject
{
    private FirstLevelFolderItem? _selected;

    /// <summary>Every character folder from the last repository scan.</summary>
    public List<FirstLevelFolderItem> All { get; } = [];

    /// <summary>The characters that match the search, in folder order.</summary>
    public BatchObservableCollection<FirstLevelFolderItem> Visible { get; } = [];

    public FirstLevelFolderItem? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public void Load(IEnumerable<FirstLevelFolderItem> characters)
    {
        All.Clear();
        All.AddRange(characters);
    }

    public void Clear()
    {
        All.Clear();
        Visible.Clear();
    }

    public void ApplyFilter(string? keyword)
        => Visible.ReplaceAll(RepositoryWorkspacePolicy.FilterByName(All, character => character.Name, keyword));

    /// <summary>A shown character by folder path, or null when the search hides it.</summary>
    public FirstLevelFolderItem? FindVisible(string? path)
        => string.IsNullOrWhiteSpace(path) ? null
            : Visible.FirstOrDefault(character => string.Equals(character.Path, path, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Any loaded character by folder path, shown or not.</summary>
    public FirstLevelFolderItem? Find(string? path)
        => string.IsNullOrWhiteSpace(path) ? null
            : All.FirstOrDefault(character => string.Equals(character.Path, path, StringComparison.OrdinalIgnoreCase));
}
