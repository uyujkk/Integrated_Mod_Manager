using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// Users could not find features that sat in second-level or right-click menus, and a step-by-step popup at first start
// was not wanted. The search lists every feature and where it is and takes the user there; the dashboard's "Get
// started" card ticks off the first tasks; each page shows one hint the first time; the title bar has help.
public sealed class DiscoverabilityTests
{
    private static void WaitUntil(Func<bool> condition, string what, int seconds = 15)
        => Assert.True(FlaUI.Core.Tools.Retry.WhileFalse(condition, TimeSpan.FromSeconds(seconds)).Result, what);

    private static AutomationElement OpenSearch(AppSession app)
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
        return app.FindByName(app.Text("CommandPalette.Placeholder"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Search_DetailedListsEveryFeatureAreaByArea()
    {
        using AppSession app = AppSession.Configured().Start();
        OpenSearch(app);
        // Before anything is typed: the first area's heading and its first feature, with where it is.
        app.FindByName(app.Text("Features.Area.General"), TimeSpan.FromSeconds(5));
        AutomationElement first = app.Find("FeatureGetStarted", TimeSpan.FromSeconds(5));
        Assert.Contains(app.Text("Features.GetStarted.Where"), first.Name);
        app.FindByName(app.Text("CommandPalette.TileTitle"));
        // Settings has its own page: it is not a group of tiles, but typing finds its features.
        Assert.False(app.Shows("FeatureLanguage"), "The start screen should have no Settings group");
        // Another word for a feature finds it ("标签" is in the tags feature's other words and title).
        app.FindByName(app.Text("CommandPalette.Placeholder")).AsTextBox().Text = "d3dx_user";
        AutomationElement slots = app.Find("FeatureSaveSlot", TimeSpan.FromSeconds(5));
        Assert.Contains(app.Text("Features.SaveSlot.Where"), slots.Name);
        app.FindByName(app.Text("CommandPalette.Placeholder")).AsTextBox().Text = app.Text("Features.Language");
        app.Find("FeatureLanguage", TimeSpan.FromSeconds(5));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        app.WaitForSaved(saved => (bool?)saved["SearchUsed"] == true, "Opening the search should count as a first-steps task");
        app.AssertNoErrors();
    }

    // The detailed search opens like Windows 8's start screen: a tile per feature; moving to one says where it is, Enter
    // takes you there, and typing on a tile goes on in the search box.
    [Fact]
    public void Search_DetailedIsAStartScreenOfTiles()
    {
        using AppSession app = AppSession.Configured().Start();
        app.Resize(1400, 900);
        OpenSearch(app);
        app.Find("FeatureTiles", TimeSpan.FromSeconds(5));
        Assert.Equal(app.Text("CommandPalette.TileHint"), app.Find("FeatureTileWhere").Name);
        Keyboard.Type(VirtualKeyShort.DOWN);
        WaitUntil(() => app.Find("FeatureTileWhere").Name.Contains(app.Text("Features.GetStarted.Where")),
            "Moving to the first tile should say where its feature is");
        Keyboard.Type(VirtualKeyShort.RIGHT);
        WaitUntil(() => !app.Find("FeatureTileWhere").Name.Contains(app.Text("Features.GetStarted.Where")),
            "The arrow keys should move between tiles");
        // Typing on a tile searches.
        Keyboard.Type("d3dx");
        app.Find("FeatureSaveSlot", TimeSpan.FromSeconds(5));
        WaitUntil(() => !app.Shows("FeatureTiles") || app.Find("FeatureTiles").IsOffscreen, "Typing should replace the tiles with matches");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        app.AssertNoErrors();
    }

    [Fact]
    public void Search_BriefListsFeaturesOnlyAsMatches()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["SearchStyle"] = "brief").Start();
        AutomationElement search = OpenSearch(app);
        app.FindByName(app.Text("CommandPalette.OpenDeveloperTools"), TimeSpan.FromSeconds(5));
        Assert.False(app.Shows("FeatureGetStarted"), "The brief search should not list features before anything is typed");
        search.AsTextBox().Text = app.Text("Features.Tags");
        app.Find("FeatureTags", TimeSpan.FromSeconds(5));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        app.AssertNoErrors();
    }

    // Picking a feature goes to its page and points at the place: the control gets the keyboard focus and a ring.
    [Theory]
    [InlineData("OrganizeImport", "ExistingModImportButton")]
    [InlineData("Rollback", "RollbackLastInstallButton")]
    [InlineData("SearchStyle", "SearchStyleComboBox")]
    [InlineData("DeployMode", "LinkDeploymentSettingsCard")]
    public void Search_PickingAFeatureTakesYouThere(string feature, string place)
    {
        using AppSession app = AppSession.Configured().Start();
        app.Resize(1400, 900);
        AutomationElement search = OpenSearch(app);
        search.AsTextBox().Text = app.Text("Features." + feature);
        app.Find("Feature" + feature, TimeSpan.FromSeconds(5));
        Keyboard.Type(VirtualKeyShort.ENTER);
        app.Find(Spotlight_RingId, TimeSpan.FromSeconds(10));
        AutomationElement target = app.Find(place, TimeSpan.FromSeconds(5));
        Assert.False(target.IsOffscreen, place + " should be scrolled into view");
        app.AssertNoErrors();
    }

    private const string Spotlight_RingId = "FeatureSpotlightRing";

    [Fact]
    public void Settings_SearchStyleIsSaved()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        app.Resize(1400, 900);
        AutomationElement style = app.Find("SearchStyleComboBox", TimeSpan.FromSeconds(10));
        AppSession.ScrollIntoView(style);
        Assert.Equal(app.Text("Settings.SearchStyle"), style.Name);
        style.AsComboBox().Select(app.Text("Settings.SearchStyleBrief"));
        app.WaitForSaved(saved => (string?)saved["SearchStyle"] == "brief", "The brief style should be saved");
        app.AssertNoErrors();
    }

    // The card replaces the step-by-step tips: it is part of the dashboard, ticks tasks off and hides for good.
    [Fact]
    public void GettingStarted_CardTicksTasksOffAndHides()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["GettingStartedTipsPending"] = true).Start();
        app.Resize(1400, 900);
        app.Find("GettingStartedCloseButton", TimeSpan.FromSeconds(10));
        // The test repository has its paths and Mods: those tasks are done and have no button.
        Assert.False(app.Shows("GettingStartedPathsButton"));
        Assert.False(app.Shows("GettingStartedFirstModButton"));

        // The search task opens the search, and is done once it was opened.
        AppSession.Invoke(app.Find("GettingStartedSearchButton"));
        app.FindByName(app.Text("CommandPalette.Placeholder"), TimeSpan.FromSeconds(5));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        WaitUntil(() => !app.Shows("GettingStartedSearchButton"), "The search task should tick itself off");

        // A task's button takes the user to its place.
        AppSession.Invoke(app.Find("GettingStartedShortcutsButton"));
        app.Find(Spotlight_RingId, TimeSpan.FromSeconds(10));
        app.Find("ShortcutsOpenOverlayButton", TimeSpan.FromSeconds(5));
        AppSession.Invoke(app.Find("DashboardNavButton"));
        WaitUntil(() => !app.Shows("GettingStartedShortcutsButton"), "Opening the Shortcuts page should tick its task off");

        AppSession.Invoke(app.Find("GettingStartedCloseButton"));
        WaitUntil(() => !app.Shows("GettingStartedCloseButton"), "Hide should take the card away");
        app.WaitForSaved(saved => (bool?)saved["GettingStartedTipsPending"] == false, "Hiding the card should be saved");
        app.AssertNoErrors();
    }

    [Fact]
    public void GettingStarted_ComesBackFromTheSearch()
    {
        using AppSession app = AppSession.Configured().Start();
        Assert.False(app.Shows("GettingStartedCloseButton"));
        AutomationElement search = OpenSearch(app);
        search.AsTextBox().Text = app.Text("Features.GetStarted");
        app.Find("FeatureGetStarted", TimeSpan.FromSeconds(5));
        Keyboard.Type(VirtualKeyShort.ENTER);
        app.Find("GettingStartedCloseButton", TimeSpan.FromSeconds(10));
        app.WaitForSaved(saved => (bool?)saved["GettingStartedTipsPending"] == true, "Showing the card again should be saved");
        app.AssertNoErrors();
    }

    // Each page's hint shows the first time and stays closed once its X is clicked, also after a restart.
    [Fact]
    public void PageHints_ShowOnceAndStayClosed()
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["ClosedPageHints"] = new JsonArray();
        }).Start();
        app.Resize(1400, 900);
        AutomationElement hint = app.Find("PageHintInfoBar", TimeSpan.FromSeconds(10));
        Assert.Contains(app.Text("PageHints.Repository"), hint.FindAllDescendants().Select(element => element.Name));
        app.Find("PageHintAllFeaturesButton");

        // Another page has its own hint; the dashboard has none.
        AppSession.Invoke(app.Find("UpdatesNavButton"));
        WaitUntil(() => app.Find("PageHintInfoBar").FindAllDescendants().Any(element => element.Name == app.Text("PageHints.Updates")),
            "The Updates page should show its hint");
        AppSession.Invoke(app.Find("DashboardNavButton"));
        WaitUntil(() => !app.Shows("PageHintAllFeaturesButton"), "The dashboard has no hint");

        AppSession.Invoke(app.Find("RepositoryNavButton"));
        AutomationElement close = app.Find("PageHintInfoBar", TimeSpan.FromSeconds(10))
            .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
            .First(button => button.AutomationId != "PageHintAllFeaturesButton");
        AppSession.Invoke(close);
        app.WaitForSaved(saved => saved["ClosedPageHints"]?.AsArray().Any(id => (string?)id == "repository") == true,
            "Closing the hint should be saved");

        app.Restart();
        AppSession.Invoke(app.Find("RepositoryNavButton", TimeSpan.FromSeconds(20)));
        Thread.Sleep(1500);
        Assert.False(app.Shows("PageHintAllFeaturesButton"), "A closed hint should not come back");
        app.AssertNoErrors();
    }

    [Fact]
    public void TitleBar_HasHelpForThePage()
    {
        using AppSession app = AppSession.Configured().Start();
        AutomationElement help = app.Find("TitleBarHelpButton", TimeSpan.FromSeconds(10));
        Assert.Equal(app.Text("Help.ThisPage"), help.Name);
        Assert.False(help.IsOffscreen);
        app.AssertNoErrors();
    }
}
