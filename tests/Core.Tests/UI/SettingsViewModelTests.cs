using IntegratedModManager.Core.Configuration;
using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

public sealed class SettingsViewModelTests
{
    private static List<string> ChangesOf(SettingsViewModel settings)
    {
        var changed = new List<string>();
        settings.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);
        return changed;
    }

    [Fact]
    public void Request_PassesTheActionAndAddressToTheWindow()
    {
        var settings = new SettingsViewModel();
        var requests = new List<SettingsActionRequest>();
        settings.ActionRequested += (_, request) => requests.Add(request);

        settings.Request(SettingsAction.CheckUpdates);
        settings.Request(SettingsAction.OpenLink, "https://www.7-zip.org/");

        Assert.Equal([new SettingsActionRequest(SettingsAction.CheckUpdates), new SettingsActionRequest(SettingsAction.OpenLink, "https://www.7-zip.org/")], requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Request_DropsALinkWithoutAnAddress(string? address)
    {
        var settings = new SettingsViewModel();
        int requests = 0;
        settings.ActionRequested += (_, _) => requests++;

        settings.Request(SettingsAction.OpenLink, address);

        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(true, "1.2.0", false, false, AppUpdateInstallAction.ViewPublicReleases)]
    [InlineData(false, "4.8.0", true, true, AppUpdateInstallAction.InstallLocalPackage)]
    [InlineData(false, null, false, false, AppUpdateInstallAction.CheckAndGet)]
    [InlineData(false, null, true, true, AppUpdateInstallAction.DownloadAndInstall)]
    [InlineData(false, null, true, false, AppUpdateInstallAction.OpenReleasePage)]
    public void InstallAction_FollowsWhatTheUpdateButtonCanDo(bool noSelfUpdate, string? localPackage, bool hasReleasePage, bool canDownload, AppUpdateInstallAction expected)
    {
        var settings = new SettingsViewModel { SelfUpdateEnabled = !noSelfUpdate };

        settings.SetInstallState(localPackage, hasReleasePage, canDownload);

        Assert.Equal(expected, settings.InstallAction);
    }

    [Fact]
    public void SetInstallState_AnnouncesTheNewAction()
    {
        var settings = new SettingsViewModel();
        List<string> changed = ChangesOf(settings);

        settings.SetInstallState("4.8.0", hasReleasePage: false, canDownloadRelease: false);

        Assert.Contains(nameof(SettingsViewModel.InstallAction), changed);
        Assert.Equal("4.8.0", settings.LocalPackageVersion);
    }

    [Fact]
    public void CheckUpdates_IsOffWhileCheckingAndWithoutSelfUpdate()
    {
        var settings = new SettingsViewModel();
        List<string> changed = ChangesOf(settings);
        Assert.True(settings.CanCheckUpdates);

        settings.IsCheckingUpdates = true;
        Assert.False(settings.CanCheckUpdates);
        Assert.Contains(nameof(SettingsViewModel.CanCheckUpdates), changed);

        settings.IsCheckingUpdates = false;
        settings.SelfUpdateEnabled = false;
        Assert.False(settings.CanCheckUpdates);
    }

    [Fact]
    public void CheckOnStartup_StaysOffWithoutSelfUpdate()
    {
        var settings = new SettingsViewModel { CheckOnStartup = true };
        Assert.True(settings.CheckOnStartup);

        settings.SelfUpdateEnabled = false;
        Assert.False(settings.CheckOnStartup);

        settings.CheckOnStartup = true;
        Assert.False(settings.CheckOnStartup);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 0)]
    public void UpdateIntervalIndex_IgnoresValuesOutsideTheList(int value, int expected)
        => Assert.Equal(expected, new SettingsViewModel { UpdateIntervalIndex = value }.UpdateIntervalIndex);

    [Fact]
    public void StatusLines_TakeNoRoomWhenEmpty()
    {
        var settings = new SettingsViewModel();
        Assert.False(settings.HasUpdateStatus);
        Assert.False(settings.HasDiagnosticsStatus);

        settings.UpdateStatus = "Current version: 4.7.3";
        settings.DiagnosticsStatus = "Exported diagnostics.zip";
        Assert.True(settings.HasUpdateStatus);
        Assert.True(settings.HasDiagnosticsStatus);

        settings.UpdateStatus = " ";
        settings.DiagnosticsStatus = null!;
        Assert.False(settings.HasUpdateStatus);
        Assert.Equal(string.Empty, settings.DiagnosticsStatus);
        Assert.False(settings.HasDiagnosticsStatus);
    }

    [Fact]
    public void SetOnlineInfo_SplitsTheTableWithTheOddItemOnTheLeft()
    {
        var settings = new SettingsViewModel();
        SettingInfoItem[] items = Enumerable.Range(1, 5).Select(index => new SettingInfoItem("Label" + index, "Value" + index)).ToArray();

        settings.SetOnlineInfo("Read by the Online page", items);

        Assert.Equal("Read by the Online page", settings.OnlineSummary);
        Assert.Equal(items.Take(3), settings.OnlineInfoFirstHalf);
        Assert.Equal(items.Skip(3), settings.OnlineInfoSecondHalf);
    }

    [Fact]
    public void SetOnlineInfo_WithoutARepositoryLeavesTheTableEmpty()
    {
        var settings = new SettingsViewModel();
        settings.SetOnlineInfo("x", [new SettingInfoItem("a", "b")]);

        settings.SetOnlineInfo("Select a repository", []);

        Assert.Empty(settings.OnlineInfoFirstHalf);
        Assert.Empty(settings.OnlineInfoSecondHalf);
    }

    [Fact]
    public void Download_ShowsProgressAndHidesTheBarAfterAFailure()
    {
        var settings = new SettingsViewModel();

        settings.BeginDownload("Preparing");
        Assert.True(settings.IsDownloadingUpdate);
        Assert.True(settings.IsDownloadProgressIndeterminate);
        Assert.Equal(0, settings.DownloadProgress);

        settings.ReportDownload(42.5, "42%");
        Assert.False(settings.IsDownloadProgressIndeterminate);
        Assert.Equal((42.5, "42%"), (settings.DownloadProgress, settings.DownloadText));

        // Without a size the bar runs, keeps its value and the text stays.
        settings.ReportDownload(null, null);
        Assert.True(settings.IsDownloadProgressIndeterminate);
        Assert.Equal((42.5, "42%"), (settings.DownloadProgress, settings.DownloadText));

        settings.ReportDownload(250, "done");
        Assert.Equal(100, settings.DownloadProgress);
        settings.ReportDownloadText("Verifying");
        Assert.Equal("Verifying", settings.DownloadText);

        settings.EndDownload();
        Assert.False(settings.IsDownloadingUpdate);
        Assert.False(settings.IsDownloadProgressIndeterminate);
    }

    [Fact]
    public void Busy_LocksTheAppearanceChoices()
    {
        var settings = new SettingsViewModel();
        List<string> changed = ChangesOf(settings);

        settings.IsBusy = true;

        Assert.False(settings.CanChangeAppearance);
        Assert.Contains(nameof(SettingsViewModel.CanChangeAppearance), changed);
    }

    [Fact]
    public void Appearance_KeepsTheThemeAndItsIndexTogether()
    {
        var settings = new SettingsViewModel();

        settings.Appearance.ThemeIndex = (int)ThemePreference.Dark;
        Assert.Equal(ThemePreference.Dark, settings.Appearance.Theme);

        settings.Appearance.ThemeIndex = 9;
        Assert.Equal(ThemePreference.Dark, settings.Appearance.Theme);
    }
}
