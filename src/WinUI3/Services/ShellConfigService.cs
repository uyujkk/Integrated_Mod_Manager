using System.Text.Json;
using IntegratedModManager.Core.Configuration;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Where beta-shell.json and config.ini live and how they are read and written, with the backup
/// copies that restore a damaged file. The window still decides what goes into them.
/// </summary>
internal sealed class ShellConfigService(string baseDirectory)
{
    private readonly ConfigurationFileStore _files = new(Path.Combine(baseDirectory, "backups", "config"));

    public string ConfigPath { get; } = Path.Combine(baseDirectory, "config.ini");
    public string ShellConfigPath { get; } = Path.Combine(baseDirectory, "beta-shell.json");

    /// <summary>Unknown keys of beta-shell.json, written back unchanged so newer settings survive.</summary>
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    // Neither file exists only on a first install; upgrades keep them, and with them the user's language.
    public bool IsFirstInstall => !File.Exists(ConfigPath) && !File.Exists(ShellConfigPath);

    /// <summary>The file's text, or its latest valid backup when the file is damaged.</summary>
    public string Read(string filePath, Func<string, bool> validator, out bool restoredFromBackup)
    {
        ConfigurationReadResult result = _files.Read(filePath, validator);
        restoredFromBackup = result.RestoredFromBackup;
        return result.Text;
    }

    public void Write(string filePath, string content) => _files.Write(filePath, content);
}
