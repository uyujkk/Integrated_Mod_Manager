using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace IntegratedModManager.UI.Tests;

// "What's new" and the release notes offer the Discord server (the button is found, never clicked: it opens a browser).
public sealed class WhatsNewTests
{
    [Fact]
    public void WhatsNew_OffersTheDiscordServer()
    {
        // A copy that last saw an older feature set gets the dialog once.
        using AppSession app = AppSession.Configured((shell, _) => shell["WhatsNewSeenVersion"] = "4.7.0").Start();
        app.FindByName(app.Text("WhatsNew.JoinDiscord"), TimeSpan.FromSeconds(20));
        app.FindByName(app.Text("WhatsNew.OpenDetails"));
        AppSession.Invoke(app.FindByName(app.Text("WhatsNew.Close")));
        app.AssertNoErrors();
    }

    [Fact]
    public void ReleaseNotes_OfferTheDiscordServer()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();
        AutomationElement notes = app.Find("ReleaseNotesExpander", TimeSpan.FromSeconds(10));
        // The settings expander has no expand pattern for automation: click its header like a user.
        System.Drawing.Rectangle header = notes.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(header.X + 120, header.Y + 30));
        AutomationElement button = app.Find("ReleaseNotesDiscordButton", TimeSpan.FromSeconds(5));
        Assert.Equal(app.Text("WhatsNew.JoinDiscord"), button.Name);
        app.AssertNoErrors();
    }
}
