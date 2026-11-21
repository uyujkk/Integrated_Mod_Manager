using System.Text.Json.Nodes;

namespace IntegratedModManager.UI.Tests;

// The Updates page's list of install backups, written before stage J finish step 8a moves the backup code out of the
// window. The backup is a synthetic record in the test's own copy; nothing is restored.
public sealed class InstallBackupTests
{
    [Fact]
    public void Backups_TheUpdatesPageListsARecordedBackup()
    {
        using AppSession app = AppSession.Configured((shell, _) => shell["CurrentPrimarySection"] = "updates");
        string backup = Path.Combine(app.AppFolder, "backups", "mod-installs", "synthetic-backup");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "transaction.json"), new JsonObject
        {
            ["Id"] = "synthetic-backup",
            ["Description"] = "Synthetic install backup",
            ["RepositoryId"] = "ui-repository",
            ["CreatedAtUtc"] = "2026-11-01T10:00:00+00:00",
            ["Entries"] = new JsonArray(new JsonObject
            {
                ["TargetPath"] = Path.Combine(app.TestData, "Loader", "Mods", "TestModA"),
                ["ExistedBefore"] = true,
                ["BackupRelativePath"] = "TestModA"
            })
        }.ToJsonString());
        File.WriteAllText(Path.Combine(backup, "payload.txt"), "synthetic");
        app.Start();
        AppSession.Invoke(app.Find("UpdatesViewRecovery", TimeSpan.FromSeconds(10)));
        app.Expand("BackupHistoryExpander");
        app.FindByName("Synthetic install backup", TimeSpan.FromSeconds(15));
        // The summary counts it: one backup.
        string prefix = app.Text("ProductivityFeatures.BackupsGB").Split("{1}")[0].Replace("{0}", "1");
        Assert.Contains(app.MainWindow.FindAllDescendants(), element => (element.Properties.Name.ValueOrDefault ?? "").StartsWith(prefix, StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(app.TestData, "Loader", "Mods", "TestModA")), "listing restores nothing");
        app.AssertNoErrors();
    }
}
