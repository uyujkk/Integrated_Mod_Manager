namespace ModFolderCopier.WinUI;

public sealed record WikiCatalogDescriptor(
    string AppSn,
    int ChannelId,
    string ApiUrl,
    string ContentUrlTemplate,
    string HoYoWikiApp,
    string HoYoWikiCharacterMenuId);
