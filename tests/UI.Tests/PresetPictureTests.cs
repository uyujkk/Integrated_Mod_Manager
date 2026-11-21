using System.Diagnostics;
using System.Text.Json.Nodes;

namespace IntegratedModManager.UI.Tests;

// Preset pictures (snapshots\preset-previews\<repository>\<preset>.jpg), written before stage J finish step 6 moves the
// preview code out of the window: pictures that a preset or state slot names are kept when the app starts, pictures
// nothing names are removed, and deleting a preset deletes its picture. Only AppSession's disposable copy is used.
public sealed class PresetPictureTests
{
    private static string Previews(AppSession app) => Path.Combine(app.AppFolder, "snapshots", "preset-previews", "ui-repository");

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 15000) throw new TimeoutException(what);
            Thread.Sleep(100);
        }
    }

    [Fact]
    public void Presets_NamedPicturesStayOrphansGoAndDeletingAPresetDeletesItsPicture()
    {
        using AppSession app = AppSession.Configured((shell, _) =>
        {
            shell["CurrentPrimarySection"] = "repository";
            shell["RepositoryWorkspaceView"] = "presets";
            shell["SelectedConfigurationProfileId"] = "with-picture";
            shell["ConfigurationProfiles"] = new JsonArray(new JsonObject
            {
                ["Id"] = "with-picture", ["RepositoryId"] = "ui-repository", ["Name"] = "With picture",
                ["ModRelativePaths"] = new JsonArray(@"TestCharacter\TestModB"),
                ["IncludesPersistentState"] = false, ["PersistentStates"] = new JsonArray(),
                ["PreviewFile"] = "ui-repository/with-picture.jpg"
            });
            shell["ModPersistentSlots"] = new JsonArray(new JsonObject
            {
                ["Id"] = "slot-with-picture", ["RepositoryId"] = "ui-repository", ["ModRelativePath"] = @"TestCharacter\TestModA", ["Name"] = "Pictured",
                ["Values"] = new JsonArray(), ["PreviewFile"] = "ui-repository/slot-with-picture.jpg"
            });
        });
        Directory.CreateDirectory(Previews(app));
        byte[] picture = File.ReadAllBytes(Path.Combine(app.TestData, "OnlineFixtures", "img", "mod-01.png"));
        foreach (string name in new[] { "with-picture.jpg", "slot-with-picture.jpg", "nobody-names-me.jpg" })
            File.WriteAllBytes(Path.Combine(Previews(app), name), picture);
        app.Start();

        // At startup: pictures a preset or slot names stay, the one nothing names is removed.
        WaitUntil(() => !File.Exists(Path.Combine(Previews(app), "nobody-names-me.jpg")), "the orphan picture removed");
        Assert.True(File.Exists(Path.Combine(Previews(app), "with-picture.jpg")));
        Assert.True(File.Exists(Path.Combine(Previews(app), "slot-with-picture.jpg")));

        AppSession.Invoke(app.Find("DeleteConfigurationProfileButton", TimeSpan.FromSeconds(15)));
        AppSession.Invoke(app.FindByName(app.Text("Dialogs.Delete"), TimeSpan.FromSeconds(10)));
        WaitUntil(() => app.TryShellConfig()?["ConfigurationProfiles"]?.AsArray().Count == 0, "the preset deleted");
        WaitUntil(() => !File.Exists(Path.Combine(Previews(app), "with-picture.jpg")), "its picture deleted");
        Assert.True(File.Exists(Path.Combine(Previews(app), "slot-with-picture.jpg")));
        app.AssertNoErrors();
    }
}
