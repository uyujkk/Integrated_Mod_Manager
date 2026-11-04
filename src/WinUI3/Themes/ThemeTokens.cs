using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>The App*Brush tokens of Themes/Tokens.xaml, for code that sets colours itself.</summary>
internal static class ThemeTokens
{
    /// <summary>The token's brush in a theme: "Light", "Dark" or "HighContrast".</summary>
    public static Brush Get(string token, string theme)
    {
        // Each theme dictionary of Tokens.xaml holds its own values, so reading it gives that theme's brush.
        foreach (ResourceDictionary merged in Application.Current.Resources.MergedDictionaries)
        {
            if (merged.ThemeDictionaries.TryGetValue(theme, out object? themed) && themed is ResourceDictionary tokens
                && tokens.ContainsKey("AppCardBackgroundBrush"))
            {
                return tokens.TryGetValue(token, out object? brush) && brush is Brush found
                    ? found
                    : throw new KeyNotFoundException("No theme token " + token);
            }
        }

        throw new InvalidOperationException("Themes/Tokens.xaml is not merged into the application resources.");
    }
}
