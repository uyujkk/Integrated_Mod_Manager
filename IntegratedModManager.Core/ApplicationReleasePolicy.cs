namespace IntegratedModManager.Core;

/// <summary>Local integration preview; keep application updates separate from the stable channel.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.2.0-preview.3";
    public const string FileVersion = "4.2.0.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => true;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;
}
