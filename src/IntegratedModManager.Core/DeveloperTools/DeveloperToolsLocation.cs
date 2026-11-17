namespace IntegratedModManager.Core.DeveloperTools;

/// <summary>
/// Where the separately released Developer Tools live in an install: a <c>DeveloperTools</c> folder beside
/// <c>IntegratedModManager.exe</c> (the install root, the parent of <c>WinUI3</c>). The tools' package unpacks into
/// that folder, and the app's Settings card and command palette open the executable from there.
/// </summary>
public static class DeveloperToolsLocation
{
    public const string FolderName = "DeveloperTools";
    public const string ExecutableName = "IntegratedModManager.DeveloperTools.exe";

    /// <summary>The release page to open when the tools are not installed.</summary>
    public const string ReleasesUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";

    /// <summary>The documented place of the executable for an install root.</summary>
    public static string ExpectedPath(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        return Path.Combine(installRoot, FolderName, ExecutableName);
    }

    /// <summary>The executable when it is installed in its documented place, otherwise null.</summary>
    public static string? Find(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return null;
        string path = ExpectedPath(installRoot);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The app's data folder (the one with <c>beta-shell.json</c>, normally <c>WinUI3</c>) for an install root the tools
    /// run in: <c>&lt;root&gt;\WinUI3</c> when it exists.
    /// </summary>
    public static string? AppFolderForInstallRoot(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return null;
        string folder = Path.Combine(installRoot, "WinUI3");
        return Directory.Exists(folder) ? folder : null;
    }
}
