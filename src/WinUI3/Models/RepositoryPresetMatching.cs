namespace ModFolderCopier.WinUI;

/// <summary>Which repository a built-in game preset stands for (stage J4 moved this out of the window).</summary>
public static class RepositoryPresetMatching
{
    /// <summary>
    /// The same category, or the same game name in either language. Genshin's older category (17510)
    /// counts as Genshin too.
    /// </summary>
    public static bool IsForPreset(WorkspaceRepository repository, RepositoryPresetInfo preset)
    {
        string categoryId = repository.OnlineCategoryId?.Trim() ?? string.Empty;
        string gameName = repository.OnlineGameName?.Trim() ?? string.Empty;
        return string.Equals(categoryId, preset.CategoryId, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(preset.Id, "genshin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(categoryId, "17510", StringComparison.OrdinalIgnoreCase))
            || string.Equals(gameName, preset.DisplayNameEn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(gameName, preset.DisplayNameZh, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The repository already made from the preset on the given online site, if there is one.</summary>
    public static WorkspaceRepository? FindExisting(IEnumerable<WorkspaceRepository> repositories, RepositoryPresetInfo preset, string site)
        => repositories.FirstOrDefault(repository =>
            string.Equals(repository.OnlineSourceSite, site, StringComparison.OrdinalIgnoreCase)
            && IsForPreset(repository, preset));
}
