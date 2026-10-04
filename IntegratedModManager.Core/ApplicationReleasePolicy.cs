namespace IntegratedModManager.Core;

/// <summary>Explicit channel boundary for this independently distributed beta.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.0-beta";
    public const string FileVersion = "4.0.0.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/tag/v4.0-beta";
    public static bool IsPrerelease => true;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;
}
