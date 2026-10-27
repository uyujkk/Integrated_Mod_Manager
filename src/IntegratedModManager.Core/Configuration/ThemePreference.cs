namespace IntegratedModManager.Core.Configuration;

public enum ThemePreference { System, Light, Dark }

public static class ThemePreferences
{
    // Older configs only stored DarkTheme=true/false; they keep that explicit choice.
    // A config with neither value is a new install and follows Windows.
    public static ThemePreference Parse(string? value, bool? legacyDarkTheme)
    {
        if (Enum.TryParse(value, ignoreCase: true, out ThemePreference preference)
            && Enum.IsDefined(preference))
        {
            return preference;
        }

        return legacyDarkTheme switch
        {
            true => ThemePreference.Dark,
            false => ThemePreference.Light,
            null => ThemePreference.System
        };
    }

    public static string Format(ThemePreference preference) => preference.ToString().ToLowerInvariant();

    public static bool IsDark(ThemePreference preference, bool systemIsDark) => preference switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => systemIsDark
    };
}
