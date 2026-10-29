using IntegratedModManager.Core.Presets;

namespace ModFolderCopier.WinUI;

public sealed class ModPersistentSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string ModRelativePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    // Set when the slot is saved, updated, imported as active or restored. The slot of a Mod with
    // the latest time is shown as its current variant.
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    // Created automatically from the Mod's declared defaults; its name follows the UI language until renamed.
    public bool IsModDefault { get; set; }
    public List<ModPersistentValue> Values { get; set; } = [];
}
