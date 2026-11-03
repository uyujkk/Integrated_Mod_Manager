namespace ModFolderCopier.WinUI;

/// <summary>
/// An empty game or category used to mean Arknights: Endfield. It now means no game, so older
/// configs get the value they were already using written out, once, before the meaning changes.
/// </summary>
public static class RepositoryGameMigration
{
    public static bool Apply(BetaShellConfig config, string formerGame, string formerCategory)
    {
        if (config.RepositoryGamesExplicit) return false;
        foreach (WorkspaceRepository repository in config.Repositories)
        {
            if (string.IsNullOrWhiteSpace(repository.OnlineGameName)) repository.OnlineGameName = formerGame;
            if (string.IsNullOrWhiteSpace(repository.OnlineCategoryId)) repository.OnlineCategoryId = formerCategory;
        }
        config.RepositoryGamesExplicit = true;
        return true;
    }
}
