namespace ModFolderCopier.WinUI;

public sealed class EndfieldCharacterInfo
{
    public EndfieldCharacterInfo(
        string englishName,
        string chineseName,
        string gameBananaCategoryId,
        string avatarUrl,
        params string[] aliases)
    {
        EnglishName = englishName;
        ChineseName = chineseName;
        GameBananaCategoryId = gameBananaCategoryId;
        AvatarUrl = avatarUrl;
        AllNames = [englishName, chineseName, .. aliases];
    }

    public string EnglishName { get; }

    public string ChineseName { get; }

    public string GameBananaCategoryId { get; }

    public string AvatarUrl { get; }

    public string[] AllNames { get; }
}
