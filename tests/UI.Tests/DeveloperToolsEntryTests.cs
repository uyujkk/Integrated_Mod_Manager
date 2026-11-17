using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace IntegratedModManager.UI.Tests;

// The ways into the separately released Developer Tools: a card in Settings → Diagnostics and a command in the palette.
// The test copy has no DeveloperTools folder, so neither is invoked (that would open the release page in a browser).
public sealed class DeveloperToolsEntryTests
{
    [Fact]
    public void Settings_HasTheDeveloperToolsCard()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        AutomationElement card = app.Find("DeveloperToolsSettingsCard", TimeSpan.FromSeconds(10));
        Assert.Contains(app.Text("Settings.DeveloperTools"), card.Name);
        app.AssertNoErrors();
    }

    [Fact]
    public void CommandPalette_OffersTheDeveloperTools()
    {
        using AppSession app = AppSession.Configured().Start();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
        AutomationElement search = app.FindByName(app.Text("CommandPalette.Placeholder"), TimeSpan.FromSeconds(5));
        // An empty query lists the commands; the entry is among them, and a search finds it too.
        app.FindByName(app.Text("CommandPalette.OpenDeveloperTools"), TimeSpan.FromSeconds(5));
        search.AsTextBox().Text = app.Text("Settings.DeveloperTools");
        Thread.Sleep(500);
        app.FindByName(app.Text("CommandPalette.OpenDeveloperTools"), TimeSpan.FromSeconds(5));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        app.AssertNoErrors();
    }
}
