namespace ModFolderCopier.WinUI;

public sealed class WorkspaceRepository
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "新建仓库";

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public bool UseDirectoryLinks { get; set; }

    public string LauncherPath { get; set; } = string.Empty;

    /// <summary>Use the integrated Endfield launch instead of running LauncherPath directly.</summary>
    public bool UseEndfieldBridge { get; set; }

    public bool EndfieldRiskAccepted { get; set; }

    public string EndfieldGamePath { get; set; } = string.Empty;

    /// <summary>Optional explicit official Games.exe; empty means discover and verify it.</summary>
    public string EndfieldParentPath { get; set; } = string.Empty;

    public string EndfieldGameArguments { get; set; } = "-force-d3d11";

    public bool EndfieldUseMods { get; set; }

    public string PersistentUserIniPath { get; set; } = string.Empty;

    public string OnlineSourceSite { get; set; } = string.Empty;

    public string OnlineGameName { get; set; } = string.Empty;

    public string OnlineCategoryId { get; set; } = string.Empty;

    public string WikiUrl { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public Dictionary<string, string> CharacterCategoryMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // By default each character folder has one Mod enabled as a link at a time; enabling
    // another replaces it. These settings let character folders keep several enabled.
    public bool AllowMultipleModsPerCharacter { get; set; }

    // Character folder name -> allows several Mods, where it differs from the default.
    public Dictionary<string, bool> CharacterSelectionOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Character folder name -> catalog character key, for folders whose name does not match a character.
    public Dictionary<string, string> CharacterAvatarBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
