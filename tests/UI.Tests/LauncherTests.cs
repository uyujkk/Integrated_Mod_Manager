namespace IntegratedModManager.UI.Tests;

// Running the repository's launcher, written before stage J finish step 8a moves the launch code out of the window. The
// test repository has no launcher, is not an Endfield repository and names no XXMI importer, so nothing is started.
public sealed class LauncherTests
{
    // The workspace's toolbar button runs the launch, which only says that no launcher is set.
    [Fact]
    public void RunLauncher_WithoutALauncherSaysSoAndStartsNothing()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "repository").Start();
        AppSession.Invoke(app.Find("RunLauncherButton", TimeSpan.FromSeconds(10)));
        app.FindByName(app.Text("EndfieldBridge.LauncherNotSet"), TimeSpan.FromSeconds(10));
        app.FindByName(app.Text("EndfieldBridge.NoValidDefaultXXMIPathWas"));
        AppSession.Invoke(app.Find("CloseButton"));
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }

    // The dashboard's quick action waits for a launcher.
    [Fact]
    public void Dashboard_RunLauncherIsOffWithoutALauncher()
    {
        using AppSession app = AppSession.Configured().Start();
        Assert.False(app.Find("DashboardRunLauncherButton", TimeSpan.FromSeconds(10)).IsEnabled);
        app.AssertNoErrors();
    }
}
