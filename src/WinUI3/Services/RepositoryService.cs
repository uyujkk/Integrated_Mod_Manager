namespace ModFolderCopier.WinUI;

/// <summary>The configured repositories and which one is current.</summary>
internal sealed class RepositoryService
{
    private string? _selectedId;

    public List<WorkspaceRepository> Items { get; } = [];

    /// <summary>The current repository's id; null in the "all repositories" view.</summary>
    public string? SelectedId
    {
        get => _selectedId;
        set
        {
            if (string.Equals(_selectedId, value, StringComparison.Ordinal)) return;
            _selectedId = value;
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public WorkspaceRepository? Selected => Items.FirstOrDefault(repository => repository.Id == _selectedId);

    /// <summary>Raised when another repository becomes current.</summary>
    public event EventHandler? SelectedChanged;
}
