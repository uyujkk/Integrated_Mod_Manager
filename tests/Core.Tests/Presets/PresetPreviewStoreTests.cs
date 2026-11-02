using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class PresetPreviewStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-preview-" + Guid.NewGuid().ToString("N"));
    private PresetPreviewStore Store => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Write_StoresTheImageUnderARelativePathAndReplacesIt()
    {
        string relative = Store.Write("repo-1", "slot_1", [1, 2]);
        Store.Write("repo-1", "slot_1", [3]);

        Assert.Equal("repo-1/slot_1.jpg", relative);
        Assert.Equal([3], File.ReadAllBytes(Store.Resolve(relative)!));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "repo-1")));
    }

    [Theory]
    [InlineData("..", "slot")]
    [InlineData("repo", "../slot")]
    [InlineData("repo", "a\\b")]
    [InlineData("", "slot")]
    public void Write_RejectsIdsThatAreNotPlainNames(string repository, string preset)
    {
        Assert.Throws<ArgumentException>(() => Store.Write(repository, preset, [1]));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../secret.jpg")]
    [InlineData("repo/../../secret.jpg")]
    [InlineData("C:/secret.jpg")]
    [InlineData("repo/slot.png")]
    [InlineData("slot.jpg")]
    public void Resolve_RefusesValuesOutsideThePreviewFolder(string? value) => Assert.Null(Store.Resolve(value));

    [Fact]
    public void Delete_RemovesThePreviewAndIgnoresAMissingOrForeignOne()
    {
        string outside = _root + "-outside.jpg";
        File.WriteAllBytes(outside, [1]);
        try
        {
            string relative = Store.Write("repo", "slot", [1]);

            Store.Delete(relative);
            Store.Delete(relative);
            Store.Delete(null);
            Store.Delete("../" + Path.GetFileName(outside));

            Assert.False(File.Exists(Store.Resolve(relative)));
            Assert.True(File.Exists(outside));
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public void RemoveOrphans_KeepsReferencedPreviewsAndClearsTheRest()
    {
        string kept = Store.Write("repo", "kept", [1]);
        string orphan = Store.Write("repo", "orphan", [1]);
        string otherRepository = Store.Write("gone", "orphan", [1]);
        File.WriteAllBytes(Store.Resolve(kept) + ".tmp", [1]);

        int removed = Store.RemoveOrphans([kept, null, "../ignored.jpg"]);

        Assert.Equal(3, removed);
        Assert.True(File.Exists(Store.Resolve(kept)));
        Assert.False(File.Exists(Store.Resolve(orphan)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Store.Resolve(otherRepository))));
    }

    [Fact]
    public void RemoveOrphans_WithoutAFolderDoesNothing() => Assert.Equal(0, Store.RemoveOrphans([]));

    [Theory]
    [InlineData(1920, 1080, 640, 360)]
    [InlineData(1080, 1920, 360, 640)]
    [InlineData(320, 200, 320, 200)]
    [InlineData(6000, 1, 640, 1)]
    public void ThumbnailSize_LimitsTheLongEdgeAndNeverEnlarges(int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), PresetPreviewStore.ThumbnailSize(width, height));

    [Fact]
    public void IsNearlyBlack_DetectsBlackAndNoiseButNotARealFrame()
    {
        byte[] black = new byte[4 * 1024], dim = new byte[4 * 1024], frame = new byte[4 * 1024];
        Array.Fill(dim, (byte)10);
        for (int i = 0; i < frame.Length / 2; i++) frame[i] = 200;

        Assert.True(PresetPreviewStore.IsNearlyBlack(black));
        Assert.True(PresetPreviewStore.IsNearlyBlack(dim));
        Assert.False(PresetPreviewStore.IsNearlyBlack(frame));
    }
}
