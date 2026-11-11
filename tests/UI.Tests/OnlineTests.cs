using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// The Online page on the synthetic samples in tests/TestData/OnlineFixtures (IMM_ONLINE_FIXTURES): nothing goes
// to the network, and a request without a sample fails as a network error would.
public sealed class OnlineTests
{
    private const string FixtureModeLine = "测试样例模式";

    private static AppSession StartOnline(string category = "90001", string language = "zh")
    {
        AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "online";
            shell["Repositories"]![0]!["OnlineCategoryId"] = category;
            shell["Repositories"]![0]!["OnlineGameName"] = "Sample Game";
        });
        if (language != "zh")
        {
            static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
            string config = Path.Combine(app.AppFolder, "config.ini");
            File.WriteAllLines(config, File.ReadAllLines(config).Select(line => line.StartsWith("language=") ? "language=" + Encode(language) : line));
        }
        Environment.SetEnvironmentVariable("IMM_ONLINE_FIXTURES", Path.Combine(app.TestData, "OnlineFixtures"));
        try { app.Start(); }
        finally { Environment.SetEnvironmentVariable("IMM_ONLINE_FIXTURES", null); }
        return app;
    }

    // Sample mode logs that it is on, and each request it has no sample for; anything else is a real error.
    private static void AssertNoErrorsBesidesSampleMode(AppSession app, bool allowMissingSamples = false)
    {
        string[] errors = app.Errors()
            .Where(line => !line.Contains(FixtureModeLine, StringComparison.Ordinal) || (!allowMissingSamples && line.Contains("no sample", StringComparison.Ordinal)))
            .ToArray();
        Assert.True(errors.Length == 0, "The app reported:\n" + string.Join("\n", errors));
    }

    // The page keeps hidden copies of some names (the other view's template); this is the one on screen.
    private static AutomationElement Visible(AppSession app, string name)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            AutomationElement? found = app.MainWindow.FindAllDescendants(cf => cf.ByName(name)).FirstOrDefault(element => !element.IsOffscreen);
            if (found is not null) return found;
            if (watch.ElapsedMilliseconds > 15000) throw new InvalidOperationException("Not visible: " + name);
            Thread.Sleep(200);
        }
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 15000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    [Fact]
    public void Online_SamplesListOpenDetailsAndFilterByCharacter()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");

        // Details open beside the list and close again with Esc.
        Mouse.Click(Visible(app, "Sample Mod 03").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineDetailPaneTitleTextBlock"))?.Name == "Sample Mod 03",
            "details for Sample Mod 03");
        AutomationElement close = app.Find("CloseOnlineDetailButton");
        Assert.Equal(app.Text("Common.Close"), close.Name);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CloseOnlineDetailButton")) is not { IsOffscreen: false },
            "details closed");

        // A character shows only its Mods.
        Mouse.Click(Visible(app, "Sample Hero Briar").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByName("Sample Mod 02")) is not null
            && app.MainWindow.FindFirstDescendant(cf => cf.ByName("Sample Mod 01")) is null, "Briar's Mods only");

        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void Online_CategorySelectionKeepsThePageResponsive()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");

        AppSession.Invoke(Visible(app, "UI"));
        Visible(app, "Sample Menu");
        AppSession.Invoke(Visible(app, "HUD"));
        Visible(app, "Sample HUD");
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByName("Sample Menu")) is null, "HUD Mods only");

        AppSession.Invoke(Visible(app, "Skins"));
        Visible(app, "Sample Mod 01");
        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void Online_NarrowWindowFoldsTheRailAndDetailsCoverTheResults()
    {
        using AppSession app = StartOnline();
        app.Resize(670, 530);
        Visible(app, "Sample Mod 01");

        // No rail: the character list sits in the toolbar, and the search box keeps its own row.
        Assert.False(app.Find("OnlineCharacterComboBox").IsOffscreen);
        Assert.True(app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineCharacterRailHost")) is null or { IsOffscreen: true });

        Mouse.Click(Visible(app, "Sample Mod 01").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineDetailPaneTitleTextBlock"))?.Name == "Sample Mod 01",
            "details for Sample Mod 01");
        // Covering the results, the drawer's close button takes the reader back to them.
        AutomationElement back = app.Find("CloseOnlineDetailButton");
        Assert.Equal(app.Text("OnlineDetails.BackToResults"), back.Name);
        AppSession.Invoke(back);
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CloseOnlineDetailButton")) is not { IsOffscreen: false },
            "details closed");

        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void Online_AFailedLoadShowsAnInlineErrorWithRetry()
    {
        // No samples exist for this category, so its list request fails.
        using AppSession app = StartOnline("90009");

        AutomationElement retry = app.FindByName(app.Text("DownloadTasks.Retry"), TimeSpan.FromSeconds(20));
        Assert.Null(app.MainWindow.FindFirstDescendant(cf => cf.ByClassName("ContentDialog")));
        AppSession.Invoke(retry);
        // Retrying fails the same way and keeps the error in place, without a dialog.
        Thread.Sleep(1500);
        Assert.NotNull(app.MainWindow.FindFirstDescendant(cf => cf.ByName(app.Text("DownloadTasks.Retry"))));
        Assert.Null(app.MainWindow.FindFirstDescendant(cf => cf.ByClassName("ContentDialog")));

        AssertNoErrorsBesidesSampleMode(app, allowMissingSamples: true);
    }

    // Stage J finish, step 3: the character catalog moves out of the page. Its result must stay the same: the rail lists
    // the same characters in the same order with their avatars, the same in each language, and the catalog is cached.
    [Theory]
    [InlineData("zh")]
    [InlineData("en")]
    public void Online_CharacterCatalogResultIsUnchanged(string language)
    {
        using AppSession app = StartOnline(language: language);
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");
        Thread.Sleep(1500);

        string[] heroes = app.MainWindow.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
            .Where(element => (element.Properties.Name.ValueOrDefault ?? string.Empty).StartsWith("Sample Hero", StringComparison.Ordinal)
                && !element.Properties.IsOffscreen.ValueOrDefault)
            .OrderBy(element => element.BoundingRectangle.Top).ThenBy(element => element.BoundingRectangle.Left)
            .Select(element => element.Name).ToArray();
        Assert.Equal(["Sample Hero Aster", "Sample Hero Briar", "Sample Hero Cobalt"], heroes);

        // Picking a character filters by its category, as before.
        Mouse.Click(Visible(app, "Sample Hero Cobalt").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByName("Sample Mod 03")) is not null
            && app.MainWindow.FindFirstDescendant(cf => cf.ByName("Sample Mod 01")) is null, "Cobalt's Mods only");

        // The catalog is kept in the local index for the next start.
        WaitUntil(() => File.Exists(Path.Combine(app.AppFolder, "cache", "app-index.db")), "the local index");
        AssertNoErrorsBesidesSampleMode(app);
    }
}
