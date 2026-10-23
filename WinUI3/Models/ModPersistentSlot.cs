using IntegratedModManager.Core.Presets;

namespace ModFolderCopier.WinUI;

public sealed class ModPersistentSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string ModRelativePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ModPersistentValue> Values { get; set; } = [];
}
