namespace IntegratedModManager.Core;

/// <summary>Development preview; application self-update stays off so the stable channel is never replaced.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.3.0-preview.1";
    public const string FileVersion = "4.3.0.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => true;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;
}
