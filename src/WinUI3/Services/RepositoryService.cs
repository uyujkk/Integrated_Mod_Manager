namespace ModFolderCopier.WinUI;

/// <summary>The configured repositories, which one is current, and adding, changing and removing them.</summary>
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

    /// <summary>A new repository from the editor's fields, trimmed (stage J finish, step 8a: moved from the window).</summary>
    public static WorkspaceRepository Create(RepositoryEditorResult result)
    {
        return new WorkspaceRepository
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = result.Name.Trim(),
            SourcePath = result.SourcePath.Trim(),
            TargetPath = result.TargetPath.Trim(),
            UseDirectoryLinks = result.UseDirectoryLinks,
            AllowMultipleModsPerCharacter = result.AllowMultipleModsPerCharacter,
            LauncherPath = result.LauncherPath.Trim(),
            XxmiImporter = result.XxmiImporter,
            OnlineSourceSite = result.OnlineSourceSite.Trim(),
            OnlineGameName = result.OnlineGameName.Trim(),
            OnlineCategoryId = result.OnlineCategoryId.Trim(),
            WikiUrl = result.WikiUrl.Trim(),
            Notes = result.Notes.Trim()
        };
    }

    /// <summary>The editor's fields into an existing repository, trimmed.</summary>
    public static void Apply(WorkspaceRepository repository, RepositoryEditorResult result)
    {
        repository.Name = result.Name.Trim();
        repository.SourcePath = result.SourcePath.Trim();
        repository.TargetPath = result.TargetPath.Trim();
        repository.UseDirectoryLinks = result.UseDirectoryLinks;
        repository.AllowMultipleModsPerCharacter = result.AllowMultipleModsPerCharacter;
        repository.LauncherPath = result.LauncherPath.Trim();
        repository.XxmiImporter = result.XxmiImporter;
        repository.OnlineSourceSite = result.OnlineSourceSite.Trim();
        repository.OnlineGameName = result.OnlineGameName.Trim();
        repository.OnlineCategoryId = result.OnlineCategoryId.Trim();
        repository.WikiUrl = result.WikiUrl.Trim();
        repository.Notes = result.Notes.Trim();
    }

    /// <summary>The online source dialog's fields into the repository, trimmed.</summary>
    public static void Apply(WorkspaceRepository repository, OnlineRepositoryConfigResult result)
    {
        repository.OnlineSourceSite = result.OnlineSourceSite.Trim();
        repository.OnlineGameName = result.OnlineGameName.Trim();
        repository.OnlineCategoryId = result.OnlineCategoryId.Trim();
        repository.WikiUrl = result.WikiUrl.Trim();
        repository.Notes = result.Notes.Trim();
    }

    /// <summary>
    /// Removes a repository. Deleting the current one selects the one now in its place (or the last); deleting another
    /// keeps the current one. Returns whether the current one was deleted.
    /// </summary>
    public bool Remove(WorkspaceRepository repository)
    {
        bool wasSelected = repository.Id == SelectedId;
        int currentIndex = Items.FindIndex(item => item.Id == repository.Id);
        Items.RemoveAll(item => item.Id == repository.Id);
        if (wasSelected)
        {
            SelectedId = Items.Count > 0 ? Items[Math.Clamp(currentIndex, 0, Items.Count - 1)].Id : null;
        }
        return wasSelected;
    }
}
