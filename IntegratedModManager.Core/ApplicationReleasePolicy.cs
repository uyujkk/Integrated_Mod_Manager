namespace IntegratedModManager.Core;

/// <summary>Stable release; application update checks honor the saved user preference.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.3.1";
    public const string FileVersion = "4.3.1.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => false;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;
}
