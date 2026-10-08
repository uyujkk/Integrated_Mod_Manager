namespace IntegratedModManager.Core;

/// <summary>The stable channel; application checks remain controlled by the user's saved preferences.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.1.0";
    public const string FileVersion = "4.1.0.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => false;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;
}
