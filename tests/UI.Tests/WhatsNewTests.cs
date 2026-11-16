using FlaUI.Core.AutomationElements;

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
        app.Find("ReleaseNotesExpander", TimeSpan.FromSeconds(10));
        // Opened through the Expander inside the card (a mouse click at a fixed offset did not open it on CI),
        // then scrolled into view: the card's items are only made while they are on screen.
        app.Expand("ReleaseNotesExpander");
        AppSession.ScrollIntoView(app.Find("ReleaseNotesExpander"));
        AutomationElement button = app.Find("ReleaseNotesDiscordButton");
        Assert.Equal(app.Text("WhatsNew.JoinDiscord"), button.Name);
        app.AssertNoErrors();
    }
}
