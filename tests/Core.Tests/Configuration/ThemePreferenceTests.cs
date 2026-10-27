using IntegratedModManager.Core.Configuration;

namespace IntegratedModManager.Core.Tests;

public sealed class ThemePreferenceTests
{
    [Theory]
    [InlineData("system", true, ThemePreference.System)]
    [InlineData("Dark", false, ThemePreference.Dark)]
    [InlineData("light", true, ThemePreference.Light)]
    public void Parse_StoredPreferenceWinsOverLegacyFlag(string value, bool legacyDark, ThemePreference expected)
        => Assert.Equal(expected, ThemePreferences.Parse(value, legacyDark));

    [Theory]
    [InlineData(true, ThemePreference.Dark)]
    [InlineData(false, ThemePreference.Light)]
    [InlineData(null, ThemePreference.System)]
    public void Parse_WithoutPreferenceKeepsTheLegacyChoiceOrFollowsWindows(bool? legacyDark, ThemePreference expected)
    {
        Assert.Equal(expected, ThemePreferences.Parse(null, legacyDark));
        Assert.Equal(expected, ThemePreferences.Parse("unknown", legacyDark));
        Assert.Equal(expected, ThemePreferences.Parse("7", legacyDark));
    }

    [Theory]
    [InlineData(ThemePreference.System, true, true)]
    [InlineData(ThemePreference.System, false, false)]
    [InlineData(ThemePreference.Dark, false, true)]
    [InlineData(ThemePreference.Light, true, false)]
    public void IsDark_OnlySystemFollowsWindows(ThemePreference preference, bool systemDark, bool expected)
        => Assert.Equal(expected, ThemePreferences.IsDark(preference, systemDark));

    [Fact]
    public void Format_RoundTrips()
    {
        foreach (ThemePreference preference in Enum.GetValues<ThemePreference>())
        {
            Assert.Equal(preference, ThemePreferences.Parse(ThemePreferences.Format(preference), null));
        }
    }
}
