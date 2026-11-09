namespace IntegratedModManager.Core.Configuration;

/// <summary>
/// The "what's new" dialog, shown once per feature set. A first install has nothing to compare with, so it
/// counts the current set as seen and goes straight to the getting-started tips (stage J4-b); upgrades
/// keep what they saved and see the dialog once.
/// </summary>
public static class WhatsNewPolicy
{
    /// <summary>The feature set to treat as seen after loading the settings.</summary>
    public static string? SeenAfterLoad(string? savedSeen, string featureSet, bool firstInstall) => firstInstall ? featureSet : savedSeen;

    public static bool ShouldShow(string? seen, string featureSet) => !string.Equals(seen, featureSet, StringComparison.Ordinal);
}
