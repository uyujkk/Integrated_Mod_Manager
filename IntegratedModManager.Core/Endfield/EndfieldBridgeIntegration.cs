namespace IntegratedModManager.Core.Endfield;

/// <summary>
/// Repository-level decisions for the integrated Endfield launch. The launch itself lives in
/// the AGPL-3.0-only IntegratedModManager.EndfieldLaunch component; this class only decides
/// eligibility, the one-time offer and where a former standalone bridge may be installed.
/// </summary>
public static class EndfieldBridgeIntegration
{
    /// <summary>Revision 2 announces the integrated launch, including to users of the standalone bridge.</summary>
    public const int OfferRevision = 2;

    /// <summary>Folder used by manager 4.1.1–4.2.0 for the separately downloaded bridge.</summary>
    public const string LegacyFolderName = "EndfieldBridge-0.3.2-win-x64";

    public static bool IsEndfield(string? categoryId, string? gameName) =>
        categoryId?.Trim() == "42770"
        || string.Equals(gameName?.Trim(), "Arknights: Endfield", StringComparison.OrdinalIgnoreCase)
        || string.Equals(gameName?.Trim(), "明日方舟：终末地", StringComparison.OrdinalIgnoreCase)
        || string.Equals(gameName?.Trim(), "终末地", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldOffer(bool eligible, int revision) => eligible && revision < OfferRevision;

    public static string ResolveLoader(string? configured, IEnumerable<string> defaults)
    {
        // Never replace a user's explicit path, even when temporarily unavailable.
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return defaults.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    public static string LegacyInstallDirectory(string toolsRoot) => Path.Combine(toolsRoot, LegacyFolderName);

    /// <summary>A standalone bridge was installed and may hold settings or an EFMI restore point to migrate.</summary>
    public static bool HasLegacyInstallation(string toolsRoot)
    {
        string root = LegacyInstallDirectory(toolsRoot);
        return File.Exists(Path.Combine(root, "EndfieldBridge.exe"))
            && (File.Exists(Path.Combine(root, "data", "profile.json")) || File.Exists(Path.Combine(root, "profile.json")));
    }
}
