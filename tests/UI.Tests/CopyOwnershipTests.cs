using System.Text.Json.Nodes;

namespace IntegratedModManager.UI.Tests;

// The reproduction of issue #107 under the issue's test name, now checking the fixed behaviour: restoring a copy-mode
// preset keeps a same-named folder the manager did not make. Only AppSession's disposable copy and data are used.
public sealed class CopyOwnershipTests
{
    [Fact]
    public void Reproduce_CopyPresetRemovesUnrelatedSameNamedFolder()
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["RepositoryWorkspaceView"] = "presets";
            shell["Repositories"]![0]!["UseDirectoryLinks"] = false;
            shell["SelectedConfigurationProfileId"] = "only-b";
            shell["ConfigurationProfiles"] = new JsonArray(new JsonObject
            {
                ["Id"] = "only-b", ["RepositoryId"] = "ui-repository", ["Name"] = "Only B",
                ["ModRelativePaths"] = new JsonArray(@"TestCharacter\TestModB"),
                ["IncludesPersistentState"] = false, ["PersistentStates"] = new JsonArray()
            });
        });
        // TestModA in the loader folder is the user's own folder: no copy of the repository Mod, no record.
        string foreign = Path.Combine(app.TestData, "Loader", "Mods", "TestModA");
        Directory.Delete(foreign, recursive: true);
        Directory.CreateDirectory(foreign);
        string sentinel = Path.Combine(foreign, "foreign-user-data.txt");
        File.WriteAllText(sentinel, "keep");
        // As in the issue, the preset's Mod is not deployed yet.
        Directory.Delete(Path.Combine(app.TestData, "Loader", "Mods", "TestModB"), recursive: true);
        app.Start();

        AppSession.Invoke(app.Find("ApplyConfigurationProfileButton", TimeSpan.FromSeconds(15)));
        // The preview names it an external folder and offers to take it over; the restore leaves it alone.
        app.FindByName(string.Format(app.Text("CombinationPresets.TakeOverExternalFolders"), 1), TimeSpan.FromSeconds(15));
        AppSession.Invoke(app.FindByName(app.Text("Common.IHaveFullyClosedTheGame")));
        AppSession.Invoke(app.FindByName(app.Text("CombinationPresets.BackUpAndRestore")));
        app.FindByName(app.Text("CombinationPresets.CombinationRestored"), TimeSpan.FromSeconds(30));

        Assert.True(File.Exists(sentinel), "The foreign folder must stay");
        Assert.True(File.Exists(Path.Combine(app.TestData, "Loader", "Mods", "TestModB", "mod.ini")), "The preset's Mod is copied");
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }
}
