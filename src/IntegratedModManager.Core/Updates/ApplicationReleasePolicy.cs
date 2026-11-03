namespace IntegratedModManager.Core.Updates;

/// <summary>Development preview: app self-update is off; the saved update settings are kept for the next stable build.</summary>
public static class ApplicationReleasePolicy
{
    public const string VersionLabel = "v4.7.0-preview.1";
    public const string FileVersion = "4.7.0.0";
    public const string ReleasePageUrl = "https://github.com/uyujkk/Integrated_Mod_Manager/releases/latest";
    public static bool IsPrerelease => true;
    public static bool ApplicationSelfUpdateEnabled => !IsPrerelease;
    public static bool CanCheckApplicationUpdates(bool requested) => requested && ApplicationSelfUpdateEnabled;

    /// <summary>
    /// The update setting to write back. A build without self-update keeps the loaded value instead of
    /// its forced-off one, so moving back to a stable build restores the user's choice.
    /// </summary>
    public static T UpdateSettingToSave<T>(T current, T loaded) => UpdateSettingToSave(current, loaded, ApplicationSelfUpdateEnabled);

    public static T UpdateSettingToSave<T>(T current, T loaded, bool selfUpdateEnabled) => selfUpdateEnabled ? current : loaded;
}
