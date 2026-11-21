using IntegratedModManager.Core.Deployment;

namespace IntegratedModManager.Core.Tests;

// The storage limit for install backups (stage J finish, step 4: the rule moved out of the window unchanged).
public sealed class InstallBackupPruningTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "imm-backups");
    private static readonly DateTime Start = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static InstallBackupFolder Backup(string name, int day, long size) => new(Path.Combine(Root, name), Start.AddDays(day), size);

    private static List<string> Names(IEnumerable<InstallBackupFolder> backups) => [.. backups.Select(item => Path.GetFileName(item.Path))];

    [Fact]
    public void LimitBytesConvertsGigabytes()
    {
        Assert.Equal(1024L * 1024 * 1024, InstallBackupPruning.LimitBytes(1));
        Assert.Equal(512L * 1024 * 1024, InstallBackupPruning.LimitBytes(0.5));
    }

    [Fact]
    public void NothingIsRemovedUnderTheLimit()
    {
        var deleted = new List<string>();
        var removed = InstallBackupPruning.Prune([Backup("a", 1, 40), Backup("b", 2, 60)], 100, Root, null, path => { deleted.Add(path); return true; });
        Assert.Empty(removed);
        Assert.Empty(deleted);
    }

    [Fact]
    public void TheOldestGoFirstUntilTheRestFit()
    {
        // Listed newest first, as the folder listing may return them.
        InstallBackupFolder[] backups = [Backup("new", 3, 50), Backup("old", 1, 50), Backup("middle", 2, 50)];
        var removed = InstallBackupPruning.Prune(backups, 60, Root, null, _ => true);
        Assert.Equal(["old", "middle"], Names(removed));
    }

    [Fact]
    public void TheBackupJustMadeIsKept()
    {
        InstallBackupFolder[] backups = [Backup("just-made", 0, 80), Backup("older", 1, 80)];
        var removed = InstallBackupPruning.Prune(backups, 100, Root, Path.Combine(Root, "JUST-MADE"), _ => true);
        Assert.Equal(["older"], Names(removed));
    }

    [Fact]
    public void FoldersOutsideTheBackupRootAreNeverRemoved()
    {
        // Path.GetFullPath resolves "..", so this one is outside the root.
        var outside = new InstallBackupFolder(Path.Combine(Root, "..", "elsewhere"), Start, 500);
        var deleted = new List<string>();
        var removed = InstallBackupPruning.Prune([outside, Backup("inside", 1, 500)], 100, Root, null, path => { deleted.Add(path); return true; });
        Assert.Equal(["inside"], Names(removed));
        Assert.DoesNotContain(deleted, path => path.Contains("elsewhere", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedRemovalStillCountsSoTheNextOldestGoes()
    {
        InstallBackupFolder[] backups = [Backup("locked", 1, 50), Backup("second", 2, 50), Backup("newest", 3, 50)];
        var removed = InstallBackupPruning.Prune(backups, 100, Root, null, path => !path.EndsWith("locked", StringComparison.Ordinal));
        Assert.Equal(["second"], Names(removed));
    }
}
