using System.Diagnostics;
using System.Text.Json.Nodes;

namespace IntegratedModManager.UI.Tests;

// The reproduction of issue #108 under the issue's test name, now checking the fixed behaviour: renaming a character
// in the real menu moves its deployed junction and the preset path with it, also after a restart. Only AppSession's
// disposable copy and data are used.
public sealed class CharacterRenameTests
{
    [Fact]
    public void Reproduce_RenameCharacterLeavesDeployedJunctionAndProfileAtOldPath()
    {
        using AppSession app = AppSession.Configured((shell, session) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["Repositories"]![0]!["UseDirectoryLinks"] = true;
            shell["ConfigurationProfiles"] = new JsonArray(new JsonObject
            {
                ["Id"] = "with-a", ["RepositoryId"] = "ui-repository", ["Name"] = "With A",
                ["ModRelativePaths"] = new JsonArray(@"TestCharacter\TestModA"),
                ["IncludesPersistentState"] = false, ["PersistentStates"] = new JsonArray()
            });
            // TestModB was deployed as a copy (#107): its record names the source by the character's name.
            shell["CopyDeployments"] = new JsonArray(new JsonObject
            {
                ["RepositoryId"] = "ui-repository", ["SourceRelativePath"] = @"TestCharacter\TestModB",
                ["DeployedPath"] = Path.Combine(session.TestData, "Loader", "Mods", "TestModB"), ["Origin"] = "copied"
            });
        });
        string oldSource = Path.Combine(app.TestData, "ModRepository", "TestCharacter", "TestModA");
        string newSource = Path.Combine(app.TestData, "ModRepository", "RenamedCharacter", "TestModA");
        string junction = Path.Combine(app.TestData, "Loader", "Mods", "TestModA");
        Directory.Delete(junction, recursive: true);
        using (Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{oldSource}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            mklink.WaitForExit();
        Assert.Equal(oldSource, Target(junction), StringComparer.OrdinalIgnoreCase);
        app.Start();

        AppSession.Invoke(app.FindByName("TestCharacter"));
        AppSession.Invoke(app.Find("CharacterMoreButton"));
        AppSession.Invoke(app.FindByName(app.Text("LocalLibrary.Rename"), TimeSpan.FromSeconds(10)));
        app.Find("PromptTextBox", TimeSpan.FromSeconds(10)).Patterns.Value.Pattern.SetValue("RenamedCharacter");
        AppSession.Invoke(app.FindByName(app.Text("Dialogs.Rename")));
        Wait(() => Directory.Exists(newSource) && Target(junction).Equals(newSource, StringComparison.OrdinalIgnoreCase)
            && (string?)app.TryShellConfig()?["ConfigurationProfiles"]?[0]?["ModRelativePaths"]?[0] == @"RenamedCharacter\TestModA");

        Assert.True(Directory.Exists(newSource));
        Assert.False(Directory.Exists(oldSource));
        Assert.Equal(newSource, Target(junction), StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(junction, "mod.ini")));
        Assert.Equal(@"RenamedCharacter\TestModA", (string?)app.ReadSaved()["ConfigurationProfiles"]![0]!["ModRelativePaths"]![0]);
        Assert.Equal(@"RenamedCharacter\TestModB", CopySource(app));

        // After a restart and a rescan the junction still works and the preset still finds the same Mod.
        app.Restart();
        AppSession.Invoke(app.FindByName("RenamedCharacter", TimeSpan.FromSeconds(15)));
        app.FindByName("TestModA");
        Assert.Equal(newSource, Target(junction), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"RenamedCharacter\TestModA", (string?)app.ReadSaved()["ConfigurationProfiles"]![0]!["ModRelativePaths"]![0]);
        Assert.Equal(@"RenamedCharacter\TestModB", CopySource(app));
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }

    private static string? CopySource(AppSession app) => (string?)app.ReadSaved()["CopyDeployments"]![0]!["SourceRelativePath"];

    private static string Target(string link) => (new DirectoryInfo(link).LinkTarget ?? string.Empty).Replace(@"\??\", string.Empty);

    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "The rename did not finish");
            Thread.Sleep(200);
        }
    }
}
