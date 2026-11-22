namespace ModFolderCopier.WinUI;

// Choosing and opening the repository's Mod folder, loader Mods folder and launcher (stage J finish, step 7b: moved from
// the workspace page unchanged). The dashboard's path cards and the workspace's setup card both use them; choosing a
// folder rescans the lists through the window, which redraws the workspace page when it exists.
internal sealed partial class WorkspaceService
{
    private Func<Task> _refreshLists = () => Task.CompletedTask;

    /// <summary>Called once by the window: rescanning the lists, with the workspace page when it exists.</summary>
    public void AttachRefresh(Func<Task> refreshLists) => _refreshLists = refreshLists;

    /// <summary>Rescans the repository, and redraws the workspace page when it exists.</summary>
    public Task RefreshListsAsync() => _refreshLists();

    public async Task PickSourceAsync()
    {
        string? picked = await _services.Dialogs.PickFolderAsync();
        if (!string.IsNullOrEmpty(picked))
        {
            Paths.Source = picked;
            _saveConfig();
            await RefreshListsAsync();
        }
    }

    public async Task PickTargetAsync()
    {
        string? picked = await _services.Dialogs.PickFolderAsync();
        if (!string.IsNullOrEmpty(picked))
        {
            Paths.Target = picked;
            _saveConfig();
            await RefreshListsAsync();
        }
    }

    public async Task PickLauncherAsync()
    {
        string? picked = await _services.Dialogs.PickFileAsync([".exe", ".bat", ".cmd"]);
        if (!string.IsNullOrEmpty(picked))
        {
            Paths.Launcher = picked;
            _saveConfig();
        }
    }

    public void OpenSource() => _services.Dialogs.OpenDirectory(Paths.Source, T("LocalLibrary.SourceFolder"));

    public void OpenTarget() => _services.Dialogs.OpenDirectory(Paths.Target, T("LocalLibrary.TargetFolder"));

    public void OpenLauncher() => _services.Dialogs.OpenFileLocation(Paths.Launcher, T("LocalLibrary.Launcher"));
}
