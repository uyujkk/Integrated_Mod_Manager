namespace IntegratedModManager.Core.Updates;

/// <summary>Local preview; application updates stay disabled until a stable release.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.6.0-preview.1";
    public const string FileVersion = "4.6.0.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => true;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;
}
