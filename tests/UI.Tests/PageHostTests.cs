using System.Diagnostics;
using FlaUI.Core.AutomationElements;

namespace IntegratedModManager.UI.Tests;

// Stage J finish, step 1: the Settings, dashboard, Updates and Shortcuts pages are made the first time they are shown.
// Whichever page the app starts on, a page made after a language or theme change shows the new language and theme.
// Only AppSession's disposable copy and data are used.
public sealed class PageHostTests
{
    private static readonly (string Nav, string TitleKey)[] Pages =
    [
        ("DashboardNavButton", "Localization.RepositoryDashboard"),
        ("RepositoryNavButton", "Common.ModWorkspace"),
        ("OnlineNavButton", "Common.OnlineModBrowser"),
        ("UpdatesNavButton", "Updates.ModUpdates"),
        ("ShortcutsNavButton", "Common.ModShortcuts"),
        ("SettingsNavButton", "Common.ApplicationSettings")
    ];

    private static void Wait(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), what);
            Thread.Sleep(100);
        }
    }

    // The mean brightness (0-255) of the window, sampled on a grid: dark pages are well under the middle.
    private static double Brightness(AppSession app)
    {
        using var image = FlaUI.Core.Capturing.Capture.Element(app.MainWindow);
        System.Drawing.Bitmap bitmap = image.Bitmap;
        double sum = 0;
        int count = 0;
        for (int x = bitmap.Width / 10; x < bitmap.Width; x += bitmap.Width / 10)
            for (int y = bitmap.Height / 10; y < bitmap.Height; y += bitmap.Height / 10)
            {
                System.Drawing.Color color = bitmap.GetPixel(x, y);
                sum += (color.R + color.G + color.B) / 3.0;
                count++;
            }
        return sum / count;
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("repository")]
    [InlineData("online")]
    [InlineData("updates")]
    [InlineData("shortcuts")]
    [InlineData("settings")]
    public void Pages_MadeLaterShowTheCurrentLanguageAndTheme(string startSection)
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = startSection;
            shell["ThemePreference"] = "light";
        }).Start();
        app.Resize(1400, 900);
        Thread.Sleep(1000);

        // Switch to English and the dark theme on the Settings page.
        AppSession.Invoke(app.Find("SettingsNavButton", TimeSpan.FromSeconds(15)));
        ComboBox languages = app.Find("LanguageComboBox", TimeSpan.FromSeconds(10)).AsComboBox();
        languages.Select(1);
        string englishTitle = AppSession.TextIn("en-US", "Common.IntegratedModManager").Split('{')[0].Trim();
        Wait(() => app.MainWindow.Title.StartsWith(englishTitle, StringComparison.Ordinal), "The window should switch to English");
        app.Find("ThemeComboBox").AsComboBox().Select(2);
        Wait(() => (string?)app.ShellConfig()["ThemePreference"] == "dark", "The dark theme should be saved");

        // Every page, made before or after the change, is in English and dark.
        foreach ((string nav, string titleKey) in Pages)
        {
            AppSession.Invoke(app.Find(nav));
            string title = AppSession.TextIn("en-US", titleKey);
            Wait(() => app.MainWindow.FindAllDescendants(cf => cf.ByName(title)).Any(element => !element.IsOffscreen), $"{nav}: '{title}' should show");
            Thread.Sleep(600);
            double brightness = Brightness(app);
            Assert.True(brightness < 100, $"{nav} should be dark (mean brightness {brightness:F0})");
        }
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }
}
