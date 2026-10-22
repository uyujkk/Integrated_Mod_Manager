using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ModDeploymentLinksTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "imm-links-" + Guid.NewGuid().ToString("N"));

    public ModDeploymentLinksTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "repo", "Mod"));
        Directory.CreateDirectory(Path.Combine(root, "repo", "Other"));
        Directory.CreateDirectory(Path.Combine(root, "deploy"));
    }

    [Fact]
    public void LinkToItsOwnSourceIsRecognized()
    {
        string link = Path.Combine(root, "deploy", "Mod");
        CreateJunction(link, Path.Combine(root, "repo", "Mod"));
        Assert.True(ModDeploymentLinks.IsLinkTo(link, Path.Combine(root, "repo", "Mod") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void LinkToAnotherFolderIsNotTreatedAsSource()
    {
        string link = Path.Combine(root, "deploy", "Mod");
        CreateJunction(link, Path.Combine(root, "repo", "Other"));
        Assert.False(ModDeploymentLinks.IsLinkTo(link, Path.Combine(root, "repo", "Mod")));
    }

    [Fact]
    public void RealCopyAndMissingFolderAreNotLinks()
    {
        string copy = Path.Combine(root, "deploy", "Mod");
        Directory.CreateDirectory(copy);
        Assert.False(ModDeploymentLinks.IsLinkTo(copy, Path.Combine(root, "repo", "Mod")));
        Assert.False(ModDeploymentLinks.IsLinkTo(Path.Combine(root, "deploy", "Missing"), Path.Combine(root, "repo", "Mod")));
    }

    // Junctions are what the manager deploys and need no symlink privilege.
    private static void CreateJunction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    public void Dispose()
    {
        // Remove links first so deleting the tree never follows them.
        foreach (string entry in Directory.EnumerateDirectories(Path.Combine(root, "deploy")))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry);
        Directory.Delete(root, true);
    }
}
