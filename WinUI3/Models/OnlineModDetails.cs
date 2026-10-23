namespace ModFolderCopier.WinUI;

public sealed class OnlineModDetails
{
    public string Summary { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> ImageUrls { get; set; } = [];

    public string TranslationNote { get; set; } = string.Empty;

    public string AccessRequirementSummary { get; set; } = string.Empty;

    public List<ShortcutBinding> ShortcutBindings { get; set; } = [];
}
