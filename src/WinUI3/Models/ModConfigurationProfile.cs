namespace ModFolderCopier.WinUI;

public sealed class ModConfigurationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string RepositoryId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> ModRelativePaths { get; set; } = [];

    public bool IncludesPersistentState { get; set; }

    public List<ModConfigurationPersistentState> PersistentStates { get; set; } = [];

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
