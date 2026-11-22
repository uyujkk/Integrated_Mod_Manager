using System.Diagnostics;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace IntegratedModManager.UI.Tests;

// What loading and saving the two settings files (beta-shell.json and config.ini) and managing repositories do, written
// before stage J finish step 8a moves the configuration and repository code out of the window. Only the test's own
// copy changes.
public sealed class ConfigurationRoundTripTests
{
    private static void WaitUntil(Func<bool> condition, string what, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > seconds * 1000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    // Window size and place follow the test machine's screen; everything else must come back as it was saved.
    private static readonly string[] WindowKeys = ["WindowX", "WindowY", "WindowWidth", "WindowHeight", "WindowMaximized"];

    private static JsonObject WithoutWindow(JsonNode saved)
    {
        var copy = (JsonObject)JsonNode.Parse(saved.ToJsonString())!;
        foreach (string key in WindowKeys) copy.Remove(key);
        return copy;
    }

    private static string ConfigIni(AppSession app) => File.ReadAllText(Path.Combine(app.AppFolder, "config.ini"));

    // Non-default settings of every kind the window loads, an unknown key a newer version may write, and config.ini's
    // bindings, links and tracked Mods: a start and a close save them unchanged, and a second round changes nothing.
    [Fact]
    public void Settings_EveryLoadedValueIsSavedAgainUnchanged()
    {
        using AppSession app = AppSession.Configured((shell, session) =>
        {
            OverlaySamples.Apply(shell, session);
            shell["CurrentPrimarySection"] = "updates";
            shell["ThemePreference"] = "light";
            shell["InterfaceDensity"] = "compact";
            shell["ReduceMotion"] = true;
            shell["ShowTitleBarSearch"] = false;
            shell["NavigationCollapsed"] = true;
            shell["OnlineCardLayout"] = "grid";
            shell["EnableConflictDetection"] = false;
            shell["InstallBackupLimitGb"] = 7.5;
            shell["RepositoryWorkspaceView"] = "covers";
            shell["WorkspaceCharacterWidth"] = 300.0;
            shell["WorkspaceDetailWidth"] = 420.0;
            shell["OverlayCompactView"] = true;
            shell["OverlayCompactFontSize"] = 16;
            shell["OverlayAutoContrast"] = true;
            shell["KeepMenuStateVariables"] = true;
            shell["PresetPreviewFrameLocked"] = true;
            shell["LatestReleaseTag"] = "v0.0.1-test";
            shell["LatestReleaseTitle"] = "Synthetic release";
            shell["FutureSetting"] = new JsonObject { ["Kept"] = 42 };
            shell["Repositories"]![0]!["Notes"] = "synthetic notes";
            shell["Repositories"]![0]!["CharacterAvatarBindings"] = new JsonObject { ["TestCharacter"] = "SampleKey" };
        });
        string source = Path.Combine(app.TestData, "ModRepository", "TestCharacter", "TestModA");
        static string Fields(params string[] values) => string.Join('\t', values.Select(value => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))));
        File.AppendAllLines(Path.Combine(app.AppFolder, "config.ini"),
        [
            "binding=" + Fields(source, "Ctrl+H", "Hair"),
            "mod_link=" + Fields(source, "https://example.invalid/mods/a"),
            "mod_origin=" + Fields(source, "GameBanana", "990100", "Sample Mod", "https://gamebanana.com/mods/990100", "", "", "770000", "sample.zip")
        ]);
        app.Start();
        app.Restart();
        JsonNode first = app.ReadSaved();
        string firstIni = ConfigIni(app);

        Assert.Equal("updates", (string?)first["CurrentPrimarySection"]);
        Assert.Equal("light", (string?)first["ThemePreference"]);
        Assert.Equal("compact", (string?)first["InterfaceDensity"]);
        Assert.True((bool)first["ReduceMotion"]!);
        Assert.False((bool)first["ShowTitleBarSearch"]!);
        Assert.Equal("grid", (string?)first["OnlineCardLayout"]);
        Assert.False((bool)first["EnableConflictDetection"]!);
        Assert.Equal(7.5, (double)first["InstallBackupLimitGb"]!);
        Assert.Equal("covers", (string?)first["RepositoryWorkspaceView"]);
        Assert.Equal(300.0, (double)first["WorkspaceCharacterWidth"]!);
        Assert.Equal(420.0, (double)first["WorkspaceDetailWidth"]!);
        Assert.True((bool)first["OverlayCompactView"]!);
        Assert.Equal(16, (int)first["OverlayCompactFontSize"]!);
        Assert.True((bool)first["OverlayAutoContrast"]!);
        Assert.True((bool)first["KeepMenuStateVariables"]!);
        Assert.True((bool)first["PresetPreviewFrameLocked"]!);
        Assert.Equal("v0.0.1-test", (string?)first["LatestReleaseTag"]);
        Assert.Equal("Synthetic release", (string?)first["LatestReleaseTitle"]);
        Assert.Equal(42, (int)first["FutureSetting"]!["Kept"]!);
        Assert.Equal("synthetic notes", (string?)first["Repositories"]![0]!["Notes"]);
        Assert.Equal("SampleKey", (string?)first["Repositories"]![0]!["CharacterAvatarBindings"]!["TestCharacter"]);
        Assert.Contains("binding=" + Fields(source, "Ctrl+H", "Hair"), firstIni);
        Assert.Contains("mod_link=" + Fields(source, "https://example.invalid/mods/a"), firstIni);
        Assert.Contains("mod_origin=" + Fields(source, "GameBanana", "990100", "Sample Mod"), firstIni);

        app.Restart();
        Assert.Equal(WithoutWindow(first).ToJsonString(), WithoutWindow(app.ReadSaved()).ToJsonString());
        Assert.Equal(firstIni, ConfigIni(app));
        app.AssertNoErrors();
    }

    private static JsonArray Repositories(AppSession app) => app.ReadSaved()["Repositories"]!.AsArray();

    // A button of a repository card on the dashboard. The cards are ItemsRepeater items, which UI Automation lists flat
    // after the "All repositories" title: a card's buttons follow its name, before the next card's name.
    private static AutomationElement CardButton(AppSession app, string repository, Func<AutomationElement, bool> match)
    {
        AutomationElement? found = null;
        WaitUntil(() =>
        {
            AutomationElement[] all = app.MainWindow.FindAllDescendants();
            int title = Array.FindIndex(all, element => element.Properties.AutomationId.ValueOrDefault == "DashboardRepositoriesTitleTextBlock");
            if (title < 0) return false;
            string[] names = Repositories(app).Select(item => (string)item!["Name"]!).ToArray();
            bool inCard = false;
            for (int index = title + 1; index < all.Length; index++)
            {
                AutomationElement element = all[index];
                if (element.ControlType == ControlType.Text && names.Contains(element.Properties.Name.ValueOrDefault))
                {
                    if (inCard) return false;
                    inCard = element.Properties.Name.ValueOrDefault == repository;
                    continue;
                }
                if (inCard && element.ControlType == ControlType.Button && match(element))
                {
                    found = element;
                    return true;
                }
            }
            return false;
        }, "a button of the card of " + repository);
        return found!;
    }

    // A blank repository from the dashboard's menu: named in the editor, selected, saved; the other one can be switched
    // back to, and the new one deleted from its card.
    [Fact]
    public void Repositories_CreateSwitchAndDeleteFromTheDashboard()
    {
        using AppSession app = AppSession.Configured().Start();
        AppSession.Invoke(app.Find("AddRepositoryButton"));
        AppSession.Invoke(app.FindByName(app.Text("Dashboard.BlankRepository"), TimeSpan.FromSeconds(5)));
        AutomationElement name = app.FindByName(app.Text("Dialogs.EnterRepositoryName"), TimeSpan.FromSeconds(10));
        name.Patterns.Value.Pattern.SetValue("Second repository");
        AppSession.Invoke(app.Find("PrimaryButton"));

        WaitUntil(() => Repositories(app).Count == 2, "the new repository saved");
        JsonNode created = Repositories(app).Single(repository => (string?)repository!["Name"] == "Second repository")!;
        WaitUntil(() => (string?)app.ReadSaved()["SelectedRepositoryId"] == (string?)created["Id"], "the new repository selected");

        AppSession.Invoke(app.Find("DashboardNavButton"));
        AppSession.Invoke(CardButton(app, "UI repository", _ => true));
        WaitUntil(() => (string?)app.ReadSaved()["SelectedRepositoryId"] == "ui-repository", "switched back to the first repository");

        AutomationElement more = CardButton(app, "Second repository", button => button.Properties.Name.ValueOrDefault == app.Text("Dashboard.MoreActions"));
        more.Patterns.Invoke.Pattern.Invoke();
        AppSession.Invoke(app.FindByName(app.Text("Dashboard.DeleteRepository"), TimeSpan.FromSeconds(5)));
        // Deleting asks twice.
        app.FindByName(app.Text("Repositories.FirstConfirmation"), TimeSpan.FromSeconds(10));
        AppSession.Invoke(app.Find("PrimaryButton"));
        app.FindByName(app.Text("Repositories.SecondConfirmation"), TimeSpan.FromSeconds(10));
        AppSession.Invoke(app.Find("PrimaryButton"));
        WaitUntil(() => Repositories(app).Count == 1, "the second repository deleted");
        Assert.Equal("ui-repository", (string?)Repositories(app)[0]!["Id"]);
        Assert.True(Directory.Exists(Path.Combine(app.TestData, "ModRepository", "TestCharacter")), "deleting a repository keeps its folders");
        app.AssertNoErrors();
    }
}
