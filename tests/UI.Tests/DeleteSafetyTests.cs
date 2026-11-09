using System.Diagnostics;

namespace IntegratedModManager.UI.Tests;

// The reproductions of issues #105 and #106, under the issues' test names, now checking the fixed behaviour.
// Only the disposable copy and synthetic data of AppSession are used.
public sealed class DeleteSafetyTests
{
    // #105: the loader folder set to the Mod's character folder made the Mod's deployed folder its own source, and a
    // double-click removed the source. Now the toggle stops before any backup, copy or delete and names both paths.
    [Fact]
    public void Reproduce_SingleToggleCanRemoveItsOwnSource()
    {
        string? repository = null, character = null;
        using AppSession app = AppSession.Configured((shell, session) =>
        {
            repository = Path.Combine(session.TestData, "ModRepository");
            character = Path.Combine(repository, "TestCharacter");
            var entry = shell["Repositories"]![0]!;
            entry["TargetPath"] = character;
            entry["UseDirectoryLinks"] = false;
            shell["CurrentPrimarySection"] = "repository";
        });
        // The window reads the loader folder from config.ini too.
        static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
        string config = Path.Combine(app.AppFolder, "config.ini");
        File.WriteAllLines(config, File.ReadAllLines(config).Select(line => line.StartsWith("target_dir=", StringComparison.Ordinal) ? "target_dir=" + Encode(character!) : line));
        string source = Path.Combine(character!, "TestModA");
        string sentinel = Path.Combine(source, "sentinel.txt");
        File.WriteAllText(sentinel, "keep");
        app.Start();

        AppSession.Invoke(app.FindByName("TestCharacter"));
        app.FindByName("TestModA").DoubleClick();

        app.FindByName(string.Format(app.Text("LocalLibrary.PathsOverlap"), repository, character), TimeSpan.FromSeconds(10));
        Assert.True(Directory.Exists(source), "The source Mod must stay");
        Assert.True(File.Exists(sentinel));
        Assert.True(File.Exists(Path.Combine(source, "mod.ini")));
        Assert.True(app.IsRunning);
        app.AssertNoErrors();
    }

    // #106: a junction under the preview folder led the startup cleanup into an outside folder, which it emptied.
    // Now the cleanup leaves everything behind a link in place and logs why.
    [Fact]
    public void Reproduce_StartupPreviewCleanupDeletesThroughAJunction()
    {
        using AppSession app = AppSession.Configured();
        string outside = Path.Combine(app.TestData, "ExternalSamples");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "unrelated.txt"), "keep");
        File.WriteAllBytes(Path.Combine(outside, "unrelated.jpg"), [0xFF, 0xD8, 0xFF]);
        string previews = Path.Combine(app.AppFolder, "snapshots", "preset-previews");
        Directory.CreateDirectory(previews);
        string junction = Path.Combine(previews, "linked");
        using (Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            mklink.WaitForExit();
        Assert.True(Directory.Exists(junction), "The test needs the junction");

        app.Start();
        Thread.Sleep(4000);

        Assert.True(File.Exists(Path.Combine(outside, "unrelated.txt")));
        Assert.True(File.Exists(Path.Combine(outside, "unrelated.jpg")));
        Assert.True(app.IsRunning);
        // The only issue logged is the reason the cleanup skipped the link.
        IReadOnlyList<string> errors = app.Errors();
        Assert.Contains(errors, line => line.Contains("Preset preview cleanup", StringComparison.Ordinal) && line.Contains("LinkedFolder", StringComparison.Ordinal));
        Assert.All(errors, line => Assert.Contains("Preset preview cleanup", line));
        // The disposable session deletes its folder afterwards; the junction goes first so nothing is followed.
        Directory.Delete(junction);
    }
}
