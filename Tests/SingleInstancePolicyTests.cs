using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class SingleInstancePolicyTests
{
    [Fact]
    public void SameDirectoryWithDifferentSpellingProducesSameKey()
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-single-instance", "App");
        string withSeparator = root + Path.DirectorySeparatorChar;
        string upper = root.ToUpperInvariant();

        string key = SingleInstancePolicy.CreateInstanceKey(root);

        Assert.Equal(key, SingleInstancePolicy.CreateInstanceKey(withSeparator));
        Assert.Equal(key, SingleInstancePolicy.CreateInstanceKey("  " + root + "  "));
        Assert.Equal(SingleInstancePolicy.NormalizeDirectory(root), SingleInstancePolicy.NormalizeDirectory(upper));
    }

    [Fact]
    public void DifferentInstallDirectoriesProduceDifferentKeys()
    {
        string stable = Path.Combine(Path.GetTempPath(), "imm-single-instance", "Stable");
        string preview = Path.Combine(Path.GetTempPath(), "imm-single-instance", "Preview");

        Assert.NotEqual(
            SingleInstancePolicy.CreateInstanceKey(stable),
            SingleInstancePolicy.CreateInstanceKey(preview));
    }

    [Fact]
    public void KeyIsStableAndBounded()
    {
        string directory = Path.Combine(Path.GetTempPath(), "imm-single-instance", "目录 with spaces");

        string key = SingleInstancePolicy.CreateInstanceKey(directory);

        Assert.Equal(key, SingleInstancePolicy.CreateInstanceKey(directory));
        Assert.StartsWith("IntegratedModManager.Main.", key, StringComparison.Ordinal);
        Assert.Equal("IntegratedModManager.Main.".Length + 24, key.Length);
        Assert.Matches("^[A-Za-z0-9.]+$", key);
    }

    [Fact]
    public void BareRootKeepsItsSeparator()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        string normalized = SingleInstancePolicy.NormalizeDirectory(root);

        Assert.Equal(Path.GetFullPath(root).ToUpperInvariant(), normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankDirectoryIsRejected(string directory)
    {
        Assert.Throws<ArgumentException>(() => SingleInstancePolicy.CreateInstanceKey(directory));
    }
}
