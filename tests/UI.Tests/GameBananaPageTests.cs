using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace IntegratedModManager.UI.Tests;

// Issue #129: the GameBanana page opened for likes, in sample mode (IMM_ONLINE_FIXTURES): the page is the offline
// sample, nothing goes to the network, and Settings clears the sign-in it keeps in the app's cache folder.
public sealed class GameBananaPageTests
{
    private const string FixtureModeLine = "测试样例模式";

    private static AppSession StartOnline()
    {
        AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "online";
            shell["Repositories"]![0]!["OnlineCategoryId"] = "90001";
            shell["Repositories"]![0]!["OnlineGameName"] = "Sample Game";
        });
        Environment.SetEnvironmentVariable("IMM_ONLINE_FIXTURES", Path.Combine(app.TestData, "OnlineFixtures"));
        try { app.Start(); }
        finally { Environment.SetEnvironmentVariable("IMM_ONLINE_FIXTURES", null); }
        app.Resize(1400, 900);
        return app;
    }

    private static string Profile(AppSession app) => Path.Combine(app.AppFolder, "cache", "gamebanana-webview2");

    private static AutomationElement Visible(AppSession app, string name, int seconds = 20)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            AutomationElement? found = app.MainWindow.FindAllDescendants(cf => cf.ByName(name)).FirstOrDefault(element => !element.IsOffscreen);
            if (found is not null) return found;
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new InvalidOperationException("Not visible: " + name);
            Thread.Sleep(200);
        }
    }

    private static void WaitUntil(Func<bool> condition, string what, int seconds = 20)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    // A clickable settings card has no invoke pattern; click it like a person would, after scrolling it into view
    // (where it lands depends on how the Settings columns balance, which changes when cards are added).
    private static void ClickCard(AutomationElement card)
    {
        AppSession.ScrollIntoView(card);
        card.Focus();
        Mouse.Click(card.GetClickablePoint());
        Thread.Sleep(600);
    }

    private static bool PageShown(AppSession app)
        => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("GameBananaPage")) is { IsOffscreen: false };

    // Sample mode logs that it is on and each request without a sample (the page's own extras); nothing else.
    private static void AssertNoErrorsBesidesSampleMode(AppSession app)
    {
        string[] errors = app.Errors().Where(line => !line.Contains(FixtureModeLine, StringComparison.Ordinal)).ToArray();
        Assert.True(errors.Length == 0, "The app reported:\n" + string.Join("\n", errors));
    }

    private static void OpenPage(AppSession app)
    {
        Mouse.Click(Visible(app, "Sample Mod 01").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineDetailPaneTitleTextBlock"))?.Name == "Sample Mod 01",
            "details for Sample Mod 01");
        AppSession.Invoke(Visible(app, app.Text("OnlineDetails.LikeOnGameBanana")));
        WaitUntil(() => PageShown(app), "the GameBanana page");
        // The notice says what the page is and where the sign-in goes; the sample page loads.
        Visible(app, app.Text("OnlineDetails.GameBananaPageNotice"));
        // The hint shows once the sample page has loaded; the browser's first start can take long on a CI machine.
        Visible(app, app.Text("OnlineDetails.GameBananaLikeHint"), seconds: 60);
    }

    [Fact]
    public void ThePageOpensTheSampleGoesBackToTheModAndCloses()
    {
        using AppSession app = StartOnline();
        OpenPage(app);
        Assert.True(Directory.Exists(Profile(app)), "The page keeps its data in the app's cache folder");

        AppSession.Invoke(Visible(app, app.Text("OnlineDetails.ReturnToGameBananaMod")));
        Visible(app, app.Text("OnlineDetails.GameBananaLikeHint"));

        AppSession.Invoke(Visible(app, app.Text("Common.Close")));
        WaitUntil(() => !PageShown(app), "the page closed");
        Visible(app, "Sample Mod 02");
        Assert.True(app.IsRunning);
        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void SettingsClearsTheSignInRightAfterThePageWasOpen()
    {
        using AppSession app = StartOnline();
        OpenPage(app);
        File.WriteAllText(Path.Combine(Profile(app), "sample-sign-in.txt"), "made up");

        // Leaving the Online page closes the page; Settings waits for its browser to let go of the files.
        AppSession.Invoke(app.Find("SettingsNavButton"));
        ClickCard(app.Find("ClearGameBananaSignInButton", TimeSpan.FromSeconds(15)));
        AppSession.Invoke(app.FindByName(app.Text("Settings.ClearGameBananaSignInClear"), TimeSpan.FromSeconds(10)));
        WaitUntil(() => !Directory.Exists(Profile(app)), "the sign-in deleted");
        Visible(app, app.Text("Settings.GameBananaSignInCleared"));
        Assert.True(Directory.Exists(Path.Combine(app.AppFolder, "cache")), "The rest of the cache stays");

        // Nothing saved any more: clearing again says so.
        ClickCard(app.Find("ClearGameBananaSignInButton"));
        AppSession.Invoke(app.FindByName(app.Text("Settings.ClearGameBananaSignInClear"), TimeSpan.FromSeconds(10)));
        Visible(app, app.Text("Settings.GameBananaSignInNothingToClear"));

        AppSession.Invoke(app.Find("OnlineNavButton"));
        Visible(app, "Sample Mod 01");
        Assert.False(PageShown(app));
        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void CancellingKeepsTheSignIn()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings");
        Directory.CreateDirectory(Profile(app));
        File.WriteAllText(Path.Combine(Profile(app), "sample-sign-in.txt"), "made up");
        app.Start();

        ClickCard(app.Find("ClearGameBananaSignInButton", TimeSpan.FromSeconds(15)));
        AppSession.Invoke(app.FindByName(app.Text("Common.Cancel"), TimeSpan.FromSeconds(10)));
        Thread.Sleep(1000);
        Assert.True(File.Exists(Path.Combine(Profile(app), "sample-sign-in.txt")));
        app.AssertNoErrors();
    }
}
