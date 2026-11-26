using FlaUI.Core.AutomationElements;
using static IntegratedModManager.UI.Tests.OnlineTests;

namespace IntegratedModManager.UI.Tests;

// Users could not find actions that were only in right-click menus. These are now visible buttons: the selected
// character's avatar, rename, folder and several-Mods toggle in the Mod card's heading; "save for later" on every
// Online card and in the details drawer; "save author" and the author's works in the details; and each Updates row's
// folder. The menus keep them as shortcuts.
public sealed class VisibleEntryPointTests
{
    private static AutomationElement ShownButton(AppSession app, string automationId)
    {
        AutomationElement element = app.Find(automationId, TimeSpan.FromSeconds(10));
        Assert.False(element.IsOffscreen, automationId + " is on screen");
        Assert.True(element.IsEnabled, automationId + " is enabled");
        return element;
    }

    [Fact]
    public void Workspace_TheSelectedCharactersActionsAreButtonsInTheModCardHeading()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();
        app.Resize(1400, 900);
        AppSession.Invoke(app.FindByName("TestCharacter", TimeSpan.FromSeconds(15)));

        ShownButton(app, "OpenCharacterFolderButton");
        Assert.Equal(string.Format(app.Text("CharacterSelection.OpenCharacterFolder"), "TestCharacter"), app.Find("OpenCharacterFolderButton").Name);

        // The avatar opens the avatar actions.
        AppSession.Invoke(ShownButton(app, "CharacterAvatarButton"));
        app.FindByName(app.Text("CharacterAvatars.ChooseImage"), TimeSpan.FromSeconds(10));
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        Thread.Sleep(400);

        // Rename asks for the new name; cancelling changes nothing.
        AppSession.Invoke(ShownButton(app, "RenameFirstLevelButton"));
        Assert.Equal("TestCharacter", app.Find("PromptTextBox", TimeSpan.FromSeconds(10)).Patterns.Value.Pattern.Value.Value);
        AppSession.Invoke(app.FindByName(app.Text("Common.Cancel")));
        Thread.Sleep(400);
        Assert.True(Directory.Exists(Path.Combine(app.TestData, "ModRepository", "TestCharacter")));

        // The several-Mods toggle is saved for this character.
        AutomationElement toggle = ShownButton(app, "CharacterMultipleModsToggle");
        Assert.Equal(FlaUI.Core.Definitions.ToggleState.Off, toggle.Patterns.Toggle.Pattern.ToggleState.Value);
        // A real click: the page acts on the toggle's Click.
        toggle.Click();
        app.WaitForSaved(saved => (bool?)saved["Repositories"]?[0]?["CharacterSelectionOverrides"]?["TestCharacter"] == true,
            "TestCharacter allows several Mods");
        app.AssertNoErrors();
    }

    [Fact]
    public void Online_SaveForLaterIsAButtonOnEachCardAndInTheDetails()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        Visible(app, "Sample Mod 01");
        string save = app.Text("OnlineLibrary.SaveForLater");
        string remove = app.Text("OnlineLibrary.RemoveFromSaved");

        // The first card is Sample Mod 01 (sorted by hotness).
        AutomationElement CardButton() => app.MainWindow.FindAllDescendants(cf => cf.ByAutomationId("OnlineSaveForLaterButton"))
            .First(element => !element.IsOffscreen);
        Assert.Equal(save, CardButton().Name);
        AppSession.Invoke(CardButton());
        WaitUntil(() => CardButton().Name == remove, "the card's button says it is saved");

        // The details drawer shows the same state and can undo it, which the card follows.
        FlaUI.Core.Input.Mouse.Click(Visible(app, "Sample Mod 01").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineDetailPaneTitleTextBlock"))?.Name == "Sample Mod 01",
            "details for Sample Mod 01");
        AutomationElement drawerButton = app.Find("SaveOnlineDetailButton", TimeSpan.FromSeconds(10));
        WaitUntil(() => app.Find("SaveOnlineDetailButton").Name == remove, "the drawer says it is saved");
        AppSession.Invoke(drawerButton);
        WaitUntil(() => app.Find("SaveOnlineDetailButton").Name == save, "the drawer says it is not saved");
        WaitUntil(() => CardButton().Name == save, "the card follows the drawer");
        AssertNoErrorsBesidesSampleMode(app);
    }

    // "Save author" was only in a card's right-click menu; the details show it under the author, beside all their works.
    [Fact]
    public void Online_SaveAuthorIsAButtonInTheDetails()
    {
        using AppSession app = StartOnline();
        app.Resize(1400, 900);
        FlaUI.Core.Input.Mouse.Click(Visible(app, "Sample Mod 01").GetClickablePoint());
        WaitUntil(() => app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OnlineDetailPaneTitleTextBlock"))?.Name == "Sample Mod 01",
            "details for Sample Mod 01");
        AutomationElement works = ShownButton(app, "OnlineDetailAuthorWorksButton");
        Assert.Equal(string.Format(app.Text("OnlineLibrary.AuthorWorks"), "Sample Author 1"), works.Name);
        AutomationElement save = ShownButton(app, "OnlineDetailSaveAuthorButton");
        Assert.Equal(app.Text("OnlineLibrary.SaveAuthor"), save.Name);
        AppSession.Invoke(save);
        WaitUntil(() => app.Find("OnlineDetailSaveAuthorButton").Name == app.Text("OnlineLibrary.UnsaveAuthor"), "the button says the author is saved");
        app.WaitForSaved(saved => saved["OnlinePreferences"]?["Authors"]?.AsArray().Any(author => (int?)author?["Id"] == 880000) == true,
            "the saved author is written to the settings");
        AssertNoErrorsBesidesSampleMode(app);
    }

    [Fact]
    public void Updates_EachRowHasAnOpenFolderButton()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "updates");
        // config.ini keeps tracked Mods by the Mod's full path (fields Base64, separated by tabs).
        string mod = Path.Combine(app.TestData, "ModRepository", "TestCharacter", "TestModA");
        static string Fields(params string[] values) => string.Join('\t', values.Select(value => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))));
        File.AppendAllLines(Path.Combine(app.AppFolder, "config.ini"),
            ["mod_origin=" + Fields(mod, "GameBanana", "990100", "Tracked Mod", "https://gamebanana.com/mods/990100", "", "", "770000", "sample.zip")]);
        app.Start();
        app.Resize(1400, 900);

        AutomationElement folder = ShownButton(app, "UpdatesOpenFolderButton");
        Assert.Equal(app.Text("Updates.OpenFolder"), folder.Name);
        app.AssertNoErrors();
    }
}
