using IntegratedModManager.Core.Deployment;
using IntegratedModManager.Core.Online;
using IntegratedModManager.Core.Presets;

namespace IntegratedModManager.Core.Tests;

public sealed class PresetPreviewStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imm-preview-" + Guid.NewGuid().ToString("N"));
    private PresetPreviewStore Store => new(_root);

    public void Dispose() => DeleteTree(_root);

    // Links first, without following them: deleting a junction's folder recursively is refused.
    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root)) return;
        if (DirectoryLinkDeployment.IsDirectoryLink(root)) { DirectoryLinkDeployment.RemoveLink(root); return; }
        foreach (string folder in Directory.GetDirectories(root)) DeleteTree(folder);
        Directory.Delete(root, recursive: true);
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

        int removed = Store.RemoveOrphans([kept, null, "../ignored.jpg"]).Removed;

        Assert.Equal(3, removed);
        Assert.True(File.Exists(Store.Resolve(kept)));
        Assert.False(File.Exists(Store.Resolve(orphan)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Store.Resolve(otherRepository))));
    }

    [Fact]
    public void RemoveOrphans_WithoutAFolderDoesNothing() => Assert.Equal(0, Store.RemoveOrphans([]).Removed);

    // Issue #106: a junction under the preview folder leads outside it; nothing there may be deleted.
    [Fact]
    public void RemoveOrphans_DoesNotDeleteThroughASubfolderJunction()
    {
        string outside = Path.Combine(Path.GetTempPath(), "imm-preview-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "unrelated.txt"), "keep");
            File.WriteAllBytes(Path.Combine(outside, "unrelated.jpg"), [1]);
            Directory.CreateDirectory(_root);
            MakeJunction(Path.Combine(_root, "linked"), outside);

            PreviewCleanupResult result = Store.RemoveOrphans([]);

            Assert.Equal(0, result.Removed);
            Assert.True(File.Exists(Path.Combine(outside, "unrelated.txt")));
            Assert.True(File.Exists(Path.Combine(outside, "unrelated.jpg")));
            Assert.Contains(result.Skipped, skip => skip.Reason == PreviewCleanupSkipReason.LinkedFolder);
        }
        finally
        {
            DeleteTree(_root);
            DeleteTree(outside);
        }
    }

    // Issue #106: the preview folder itself redirected elsewhere: the cleanup stops before touching anything.
    [Fact]
    public void RemoveOrphans_StopsWhenThePreviewFolderIsALink()
    {
        string real = Path.Combine(Path.GetTempPath(), "imm-preview-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(real, "repo"));
        try
        {
            File.WriteAllBytes(Path.Combine(real, "repo", "orphan.jpg"), [1]);
            MakeJunction(_root, real);

            PreviewCleanupResult result = Store.RemoveOrphans([]);

            Assert.Equal(0, result.Removed);
            Assert.True(File.Exists(Path.Combine(real, "repo", "orphan.jpg")));
            Assert.Equal(PreviewCleanupSkipReason.LinkedFolder, Assert.Single(result.Skipped).Reason);
        }
        finally
        {
            DirectoryLinkDeployment.RemoveLink(_root);
            DeleteTree(real);
        }
    }

    // Issue #106: only files this store writes are removed; anything else in its folders stays.
    [Fact]
    public void RemoveOrphans_KeepsFilesItDidNotWrite()
    {
        string orphan = Store.Write("repo", "orphan", [1]);
        string referenced = Store.Write("repo", "kept", [1]);
        string folder = Path.GetDirectoryName(Store.Resolve(orphan))!;
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "keep");
        File.WriteAllBytes(Path.Combine(folder, "holiday photo.jpg"), [1]);
        Directory.CreateDirectory(Path.Combine(_root, "not a repository"));
        File.WriteAllText(Path.Combine(_root, "not a repository", "x.jpg"), "keep");

        PreviewCleanupResult result = Store.RemoveOrphans([referenced]);

        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(Store.Resolve(orphan)));
        Assert.True(File.Exists(Store.Resolve(referenced)));
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "holiday photo.jpg")));
        Assert.True(File.Exists(Path.Combine(_root, "not a repository", "x.jpg")));
        Assert.All(result.Skipped, skip => Assert.Equal(PreviewCleanupSkipReason.NotAPreview, skip.Reason));
    }

    private static void MakeJunction(string junction, string target) => DirectoryLinkDeployment.CreateJunction(junction, target);

    [Theory]
    [InlineData(1920, 1080, 640, 360)]
    [InlineData(1080, 1920, 360, 640)]
    [InlineData(320, 200, 320, 200)]
    [InlineData(6000, 1, 640, 1)]
    public void ThumbnailSize_LimitsTheLongEdgeAndNeverEnlarges(int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), PresetPreviewStore.ThumbnailSize(width, height));

    [Fact]
    public void ClipToGame_ReturnsTheSelectionRelativeToTheGame()
        => Assert.Equal(new PixelRect(100, 50, 200, 300),
            PresetPreviewStore.ClipToGame(new PixelRect(1000, 500, 1920, 1080), new PixelRect(1100, 550, 200, 300)));

    [Fact]
    public void ClipToGame_CutsWhatLiesOutsideTheGame()
        => Assert.Equal(new PixelRect(0, 0, 100, 80),
            PresetPreviewStore.ClipToGame(new PixelRect(100, 100, 800, 600), new PixelRect(50, 60, 150, 120)));

    [Theory]
    [InlineData(2000, 0, 200, 200)]
    [InlineData(100, 100, 31, 200)]
    [InlineData(-200, 100, 220, 200)]
    public void ClipToGame_RefusesSelectionsWithTooLittleOfTheGame(int x, int y, int width, int height)
        => Assert.Null(PresetPreviewStore.ClipToGame(new PixelRect(0, 0, 800, 600), new PixelRect(x, y, width, height)));

    [Fact]
    public void Region_RoundTripsAndFollowsTheWindowSize()
    {
        CharacterScreenRegion region = PresetPreviewStore.RegionOf(new PixelRect(480, 270, 960, 540), 1920, 1080);

        Assert.Equal(new CharacterScreenRegion(.25, .25, .5, .5), region);
        Assert.Equal(new PixelRect(480, 270, 960, 540), PresetPreviewStore.RegionToPixels(region, 1920, 1080));
        Assert.Equal(new PixelRect(640, 360, 1280, 720), PresetPreviewStore.RegionToPixels(region, 2560, 1440));
    }

    [Theory]
    [InlineData(-.1, 0, .5, .5)]
    [InlineData(.8, 0, .5, .5)]
    [InlineData(0, 0, .01, .5)]
    [InlineData(double.NaN, 0, .5, .5)]
    public void RegionToPixels_RefusesDamagedOrTinyRegions(double x, double y, double width, double height)
        => Assert.Null(PresetPreviewStore.RegionToPixels(new CharacterScreenRegion(x, y, width, height), 1920, 1080));

    [Fact]
    public void CropBgra_CopiesTheChosenPixels()
    {
        byte[] frame = new byte[3 * 3 * 4];
        for (int i = 0; i < frame.Length; i++) frame[i] = (byte)i;

        byte[] cropped = PresetPreviewStore.CropBgra(frame, 3, new PixelRect(1, 1, 2, 2));

        Assert.Equal([16, 17, 18, 19, 20, 21, 22, 23, 28, 29, 30, 31, 32, 33, 34, 35], cropped);
    }

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
