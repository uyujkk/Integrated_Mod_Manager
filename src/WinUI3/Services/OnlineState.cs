namespace ModFolderCopier.WinUI;

/// <summary>
/// The Online page's state that the window also reads and writes (stage J finish, step 3): the page's view model and
/// the workspace preferences saved with the settings. It lives outside the page so it exists before the page does.
/// </summary>
internal sealed class OnlineState
{
    public OnlinePageViewModel Page { get; } = new();

    public OnlineWorkspacePreferences Preferences { get; set; } = new();

    /// <summary>The catalog the Online page shows (its repository's); favourites and workspace avatars are keyed by it.</summary>
    public string? CharacterCatalogKey { get; set; }
}
