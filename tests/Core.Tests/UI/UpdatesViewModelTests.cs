using IntegratedModManager.Core.Updates;
using ModFolderCopier.WinUI;

namespace IntegratedModManager.Core.Tests;

public sealed class UpdatesViewModelTests
{
    private static TrackedModRow Row(string title, TrackedModStatus status, string path = "", bool? hasUpdate = null)
        => new(title, path.Length > 0 ? path : @"D:\Mods\" + title, "https://gamebanana.com/mods/1", null, null,
            null, null, hasUpdate ?? status == TrackedModStatus.Update, status, status == TrackedModStatus.Failed ? "Timed out" : null);

    private static UpdatesViewModel WithRows(params TrackedModRow[] rows)
    {
        var updates = new UpdatesViewModel();
        updates.SetTrackedMods(rows);
        return updates;
    }

    [Fact]
    public void VisibleTrackedMods_PutUpdatesAndFailuresFirst()
    {
        UpdatesViewModel updates = WithRows(
            Row("Zeta", TrackedModStatus.UpToDate), Row("Beta", TrackedModStatus.Update),
            Row("Alpha", TrackedModStatus.Failed), Row("Gamma", TrackedModStatus.Update));

        Assert.Equal(["Beta", "Gamma", "Alpha", "Zeta"], updates.VisibleTrackedMods.Select(row => row.Title));
    }

    [Fact]
    public void FilterAndSearch_NarrowTheList()
    {
        UpdatesViewModel updates = WithRows(
            Row("Nahida outfit", TrackedModStatus.Update), Row("Furina hair", TrackedModStatus.Update),
            Row("Nahida eyes", TrackedModStatus.UpToDate, @"D:\Mods\Nahida\eyes"));
        List<string> changed = [];
        updates.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        updates.TrackedModFilter = TrackedModFilter.Updates;
        Assert.Equal(["Furina hair", "Nahida outfit"], updates.VisibleTrackedMods.Select(row => row.Title));

        updates.Search = "nahida";
        Assert.Equal(["Nahida outfit"], updates.VisibleTrackedMods.Select(row => row.Title));

        updates.TrackedModFilter = TrackedModFilter.All;
        updates.Search = @"Nahida\eyes";
        Assert.Equal(["Nahida eyes"], updates.VisibleTrackedMods.Select(row => row.Title));
        Assert.Contains(nameof(UpdatesViewModel.VisibleTrackedMods), changed);
    }

    [Fact]
    public void CountFor_CountsEachFilter()
    {
        UpdatesViewModel updates = WithRows(
            Row("a", TrackedModStatus.Update), Row("b", TrackedModStatus.Failed),
            Row("c", TrackedModStatus.UpToDate), Row("d", TrackedModStatus.UpToDate), Row("e", TrackedModStatus.NotChecked));

        Assert.Equal((5, 1, 1, 2), (updates.CountFor(TrackedModFilter.All), updates.CountFor(TrackedModFilter.Updates),
            updates.CountFor(TrackedModFilter.Failed), updates.CountFor(TrackedModFilter.UpToDate)));
    }

    [Fact]
    public void TrackedModFilter_IgnoresUndefinedValues()
    {
        var updates = new UpdatesViewModel { TrackedModFilter = TrackedModFilter.Failed };
        updates.TrackedModFilter = (TrackedModFilter)42;
        Assert.Equal(TrackedModFilter.Failed, updates.TrackedModFilter);

        updates.HistoryFilter = DownloadHistoryFilter.Canceled;
        updates.HistoryFilter = (DownloadHistoryFilter)42;
        Assert.Equal(DownloadHistoryFilter.Canceled, updates.HistoryFilter);
    }

    [Fact]
    public void AvailableUpdateCount_IsAnnouncedOnlyWhenItChanges()
    {
        var updates = new UpdatesViewModel();
        int announced = 0;
        updates.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(UpdatesViewModel.AvailableUpdateCount)) announced++; };

        updates.SetTrackedMods([Row("a", TrackedModStatus.Update), Row("b", TrackedModStatus.UpToDate)]);
        updates.SetTrackedMods([Row("a", TrackedModStatus.Update), Row("c", TrackedModStatus.NotChecked)]);
        Assert.Equal((1, 1), (updates.AvailableUpdateCount, announced));

        updates.SetTrackedMods([]);
        Assert.Equal((0, 2), (updates.AvailableUpdateCount, announced));
    }

    [Fact]
    public void Replacing_WaitsOnlyForTheModsBeingReplaced()
    {
        var updates = new UpdatesViewModel();
        updates.SetReplacing([@"D:\Mods\A"]);
        Assert.True(updates.IsReplacing(@"d:\mods\a"));
        Assert.False(updates.IsReplacing(@"D:\Mods\B"));

        updates.SetReplacing([]);
        Assert.False(updates.IsReplacing(@"D:\Mods\A"));
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(12.5, 12.5)]
    [InlineData(500, 100)]
    public void BackupLimit_StaysInRange(double value, double expected)
        => Assert.Equal(expected, new UpdatesViewModel { BackupLimitGb = value }.BackupLimitGb);

    [Fact]
    public void BackupLimit_KeepsItsValueForNotANumber()
    {
        var updates = new UpdatesViewModel { BackupLimitGb = 7 };
        updates.BackupLimitGb = double.NaN;
        Assert.Equal(7, updates.BackupLimitGb);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 0)]
    public void ModUpdateIntervalIndex_IgnoresValuesOutsideTheList(int value, int expected)
        => Assert.Equal(expected, new UpdatesViewModel { ModUpdateIntervalIndex = value }.ModUpdateIntervalIndex);

    [Fact]
    public void SetRollbackAndBackups_AreAnnounced()
    {
        var updates = new UpdatesViewModel();
        List<string> changed = [];
        updates.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        updates.SetRollback(true, "Undo covers deployment");
        updates.SetBackups("2 backups", [new InstallBackupRow(@"D:\b1", "Install A", "today")]);

        Assert.True(updates.CanUndoLastInstall);
        Assert.Equal("Undo covers deployment", updates.RollbackStatus);
        Assert.Equal("2 backups", updates.BackupSummary);
        Assert.Single(updates.Backups);
        Assert.Contains(nameof(UpdatesViewModel.CanUndoLastInstall), changed);
        Assert.Contains(nameof(UpdatesViewModel.Backups), changed);
    }

    [Fact]
    public void RequestAndRefresh_ReachTheWindowAndPage()
    {
        var updates = new UpdatesViewModel();
        var requests = new List<UpdatesActionRequest>();
        int refreshed = 0;
        updates.ActionRequested += (_, request) => requests.Add(request);
        updates.Refreshed += (_, _) => refreshed++;

        updates.Request(UpdatesAction.RestoreBackup, @"D:\b1");
        updates.Request(UpdatesAction.CheckModUpdates);
        updates.NotifyRefreshed();

        Assert.Equal([new UpdatesActionRequest(UpdatesAction.RestoreBackup, @"D:\b1"), new UpdatesActionRequest(UpdatesAction.CheckModUpdates)], requests);
        Assert.Equal(1, refreshed);
    }
}
