using IntegratedModManager.Core.Endfield;
using IntegratedModManager.Core.Online;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The online source a repository uses (stage J finish, step 3, moved from the Online page): its site, game name and
/// GameBanana category, or the defaults, and whether it is Arknights: Endfield. The rules are OnlineSourceRules.
/// </summary>
internal sealed class OnlineSourceService(LanguageService language)
{
    public string Source(WorkspaceRepository? repository)
        => repository is null ? language.T("Common.NotSelected") : OnlineSourceRules.Source(repository.OnlineSourceSite);

    public string GameName(WorkspaceRepository? repository)
        => repository is null ? string.Empty : OnlineSourceRules.Trimmed(repository.OnlineGameName);

    public string CategoryId(WorkspaceRepository? repository)
        => repository is null ? string.Empty : OnlineSourceRules.Trimmed(repository.OnlineCategoryId);

    public bool IsGameBanana(string? source) => OnlineSourceRules.IsGameBanana(source);

    public bool IsEndfield(WorkspaceRepository? repository)
        => repository is not null && OnlineSourceRules.IsEndfield(CategoryId(repository), GameName(repository));

    public bool IsEndfieldCategory(string? categoryId)
        => OnlineSourceRules.IsEndfieldCategory(categoryId, EndfieldCharacterCatalog.BuiltIn.Select(character => character.GameBananaCategoryId));
}
