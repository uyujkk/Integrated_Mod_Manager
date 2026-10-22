namespace IntegratedModManager.Core;

/// <summary>Local Mod-update test build; app self-updates remain disabled.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.3.1-test.1";
    public const string FileVersion = "4.3.1.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => true;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;
}
