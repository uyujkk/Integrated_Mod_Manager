namespace IntegratedModManager.Core.Updates;

/// <summary>Stable release; application update checks honor the saved user preference.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.6.0";
    public const string FileVersion = "4.6.0.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => false;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;

    /// <summary>
    /// The update setting to write back. A build without self-update keeps the loaded value instead of
    /// its forced-off one, so moving back to a stable build restores the user's choice.
    /// </summary>
    public static T UpdateSettingToSave<T>(T current, T loaded) => UpdateSettingToSave(current, loaded, ApplicationSelfUpdateEnabled);

    public static T UpdateSettingToSave<T>(T current, T loaded, bool selfUpdateEnabled) => selfUpdateEnabled ? current : loaded;
}
