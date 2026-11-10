namespace IntegratedModManager.UI.Tests;

// A setting changed just before the app closes is kept: the switch and the Updates view are saved when they change.
// Only AppSession's disposable copy and data are used.
public sealed class SettingsPersistenceTests
{
    [Fact]
    public void Settings_AChangeSurvivesClosingRightAway()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "settings").Start();

        // Turn the title bar search off and the Updates page to its recovery view, and close at once.
        AppSession.Invoke(app.Find("TitleBarSearchToggleSwitch", TimeSpan.FromSeconds(10)));
        app.Restart();

        Assert.False(app.ShellConfig()["ShowTitleBarSearch"]!.GetValue<bool>());
        Thread.Sleep(2000);
        Assert.False(app.Shows("TitleBarSearchBox"), "The search box should stay off after closing right after the change");

        AppSession.Invoke(app.Find("UpdatesNavButton", TimeSpan.FromSeconds(10)));
        AppSession.Invoke(app.Find("UpdatesViewRecovery", TimeSpan.FromSeconds(10)));
        app.Restart();
        Assert.Equal("recovery", (string?)app.ShellConfig()["UpdatesView"]);
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }
}
