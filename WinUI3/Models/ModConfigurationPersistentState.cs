using IntegratedModManager.Core;

namespace ModFolderCopier.WinUI;

public sealed class ModConfigurationPersistentState
{
    public string ModRelativePath { get; set; } = string.Empty;
    public List<ModPersistentValue> Values { get; set; } = [];
}
