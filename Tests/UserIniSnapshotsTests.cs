using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class UserIniSnapshotsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-user-snapshots-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Noon = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private string UserIni => Path.Combine(_root, "Loader", "d3dx_user.ini");
    private string Snapshots => Path.Combine(_root, "snapshots");

    public UserIniSnapshotsTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserIni)!);
        File.WriteAllText(UserIni, "[Constants]\n$\\mods\\a\\x = 1\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void SaveAndList_KeepNamedCopiesNewestFirst()
    {
        UserIniSnapshot first = UserIniSnapshots.Save(UserIni, Snapshots, "Before update", Noon);
        UserIniSnapshot same = UserIniSnapshots.Save(UserIni, Snapshots, "Before update", Noon);
        File.WriteAllText(UserIni, "[Constants]\n$\\mods\\a\\x = 2\n");
        UserIniSnapshot later = UserIniSnapshots.Save(UserIni, Snapshots, "a/b:c", Noon.AddHours(1));

        Assert.Equal("Before update", first.Name);
        Assert.Equal("Before update-2", same.Name);
        Assert.Equal("a_b_c", later.Name);
        Assert.Equal([later.Path, first.Path, same.Path], UserIniSnapshots.List(Snapshots).Select(snapshot => snapshot.Path));
        Assert.Equal("[Constants]\n$\\mods\\a\\x = 1\n", File.ReadAllText(first.Path));
        Assert.Equal(Noon.ToLocalTime().DateTime, first.SavedAt.DateTime);
    }

    [Fact]
    public void Restore_ReplacesTheFileAndKeepsTheCurrentOneAsBackup()
    {
        UserIniSnapshot snapshot = UserIniSnapshots.Save(UserIni, Snapshots, "Good", Noon);
        File.WriteAllText(UserIni, "[Constants]\n$\\mods\\a\\x = 9\n");

        string backup = UserIniSnapshots.Restore(snapshot, UserIni, Noon);
        string second = UserIniSnapshots.Restore(snapshot, UserIni, Noon);

        Assert.Equal("[Constants]\n$\\mods\\a\\x = 1\n", File.ReadAllText(UserIni));
        Assert.Equal("[Constants]\n$\\mods\\a\\x = 9\n", File.ReadAllText(backup));
        Assert.NotEqual(backup, second);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(UserIni)!, "*.tmp"));
    }

    [Fact]
    public void ListIgnoresOtherFilesAndDeleteRemovesASnapshot()
    {
        Assert.Empty(UserIniSnapshots.List(Snapshots));
        UserIniSnapshot snapshot = UserIniSnapshots.Save(UserIni, Snapshots, "One", Noon);
        File.WriteAllText(Path.Combine(Snapshots, "notes.ini"), "x");

        Assert.Single(UserIniSnapshots.List(Snapshots));
        UserIniSnapshots.Delete(snapshot);
        Assert.Empty(UserIniSnapshots.List(Snapshots));
    }

    [Fact]
    public void RejectsOtherFilesEmptyNamesAndMissingSnapshots()
    {
        string other = Path.Combine(_root, "Loader", "d3dx.ini");
        File.WriteAllText(other, "x");

        Assert.Throws<InvalidDataException>(() => UserIniSnapshots.Save(other, Snapshots, "x", Noon));
        Assert.Throws<ArgumentException>(() => UserIniSnapshots.Save(UserIni, Snapshots, " .. ", Noon));
        Assert.Throws<InvalidDataException>(() => UserIniSnapshots.Restore(
            new UserIniSnapshot(Path.Combine(Snapshots, "missing.ini"), "missing", Noon, 0), UserIni, Noon));
    }
}
