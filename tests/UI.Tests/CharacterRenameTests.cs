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

    // Written before stage J finish step 6 moves the slot code (and with it part of the rename) out of the window: a
    // rename also moves the character's state slots, Default slot seeds, shortcut bindings, links and tracked Mods.
    [Fact]
    public void Rename_MovesSlotsSeedsBindingsLinksAndTrackedMods()
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["ModPersistentSlots"] = new JsonArray(new JsonObject
            {
                ["Id"] = "pinned-slot", ["RepositoryId"] = "ui-repository", ["ModRelativePath"] = @"TestCharacter\TestModA", ["Name"] = "Pinned",
                ["Values"] = new JsonArray(new JsonObject
                {
                    ["RelativeIniPath"] = "mod.ini", ["Variable"] = "$hair", ["RuntimeKey"] = @"$\Mods\TestModA\mod.ini\hair", ["Value"] = "2"
                })
            });
            shell["DefaultSlotSeeds"] = new JsonObject { ["ui-repository"] = new JsonArray(@"TestCharacter\TestModA") };
        });
        string oldSource = Path.Combine(app.TestData, "ModRepository", "TestCharacter", "TestModA");
        string newSource = Path.Combine(app.TestData, "ModRepository", "RenamedCharacter", "TestModA");
        // config.ini keeps bindings, links and tracked Mods by the Mod's full path (fields Base64, separated by tabs).
        static string Fields(params string[] values) => string.Join('\t', values.Select(value => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))));
        File.AppendAllLines(Path.Combine(app.AppFolder, "config.ini"),
        [
            "binding=" + Fields(oldSource, "Ctrl+H", "Pinned hair"),
            "mod_link=" + Fields(oldSource, "https://example.invalid/mods/pinned"),
            "mod_origin=" + Fields(oldSource, "GameBanana", "990100", "Pinned Mod", "https://gamebanana.com/mods/990100", "", "", "770000", "sample.zip")
        ]);
        app.Start();

        AppSession.Invoke(app.FindByName("TestCharacter"));
        AppSession.Invoke(app.Find("CharacterMoreButton"));
        AppSession.Invoke(app.FindByName(app.Text("LocalLibrary.Rename"), TimeSpan.FromSeconds(10)));
        app.Find("PromptTextBox", TimeSpan.FromSeconds(10)).Patterns.Value.Pattern.SetValue("RenamedCharacter");
        AppSession.Invoke(app.FindByName(app.Text("Dialogs.Rename")));
        Wait(() => Directory.Exists(newSource) && (string?)app.TryShellConfig()?["ModPersistentSlots"]?.AsArray()
            .FirstOrDefault(slot => (string?)slot!["Id"] == "pinned-slot")?["ModRelativePath"] == @"RenamedCharacter\TestModA");

        JsonNode saved = app.ReadSaved();
        Assert.All(saved["ModPersistentSlots"]!.AsArray().Where(slot => (string?)slot!["ModRelativePath"] is { } path && path.EndsWith(@"\TestModA", StringComparison.Ordinal)),
            slot => Assert.Equal(@"RenamedCharacter\TestModA", (string?)slot!["ModRelativePath"]));
        Assert.DoesNotContain(saved["ModPersistentSlots"]!.AsArray(), slot => ((string?)slot!["ModRelativePath"])?.StartsWith(@"TestCharacter\", StringComparison.Ordinal) == true);
        Assert.Contains(@"RenamedCharacter\TestModA", saved["DefaultSlotSeeds"]!["ui-repository"]!.AsArray().Select(path => (string?)path));
        Assert.DoesNotContain(@"TestCharacter\TestModA", saved["DefaultSlotSeeds"]!["ui-repository"]!.AsArray().Select(path => (string?)path));

        string[] config = File.ReadAllLines(Path.Combine(app.AppFolder, "config.ini"));
        string[] Decoded(string key) => [.. config.Where(line => line.StartsWith(key, StringComparison.Ordinal))
            .Select(line => string.Join("|", line[key.Length..].Split('\t').Select(field => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(field)))))];
        Assert.Equal([newSource + "|Ctrl+H|Pinned hair"], Decoded("binding="));
        Assert.Equal([newSource + "|https://example.invalid/mods/pinned"], Decoded("mod_link="));
        string origin = Assert.Single(Decoded("mod_origin="));
        Assert.StartsWith(newSource + "|GameBanana|990100|Pinned Mod|", origin);
        Assert.DoesNotContain(config, line => line.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(oldSource)), StringComparison.Ordinal));
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
