namespace ModFolderCopier.WinUI;

public sealed class RepositoryEditorResult
{
    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public bool UseDirectoryLinks { get; set; }

    public string LauncherPath { get; set; } = string.Empty;

    public string OnlineSourceSite { get; set; } = string.Empty;

    public string OnlineGameName { get; set; } = string.Empty;

    public string OnlineCategoryId { get; set; } = string.Empty;

    public string WikiUrl { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}
