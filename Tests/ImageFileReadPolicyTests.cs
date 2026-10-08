using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class ImageFileReadPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReadsExactBytesWithUnicodeLongAndMixedSeparatorPaths(bool unicode, bool longPath)
    {
        string root = Path.Combine(Path.GetTempPath(), "imm-image-" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, unicode ? "角色 昼雪" : "character");
        if (longPath) for (int i = 0; i < 8; i++) directory = Path.Combine(directory, "synthetic-long-image-segment");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preview.jpg");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            if (OperatingSystem.IsWindows()) path = path.Replace('\\', '/');
            using var file = ImageFileReadPolicy.OpenRead(path, 4);
            Assert.Equal(4, file.Length);
            Assert.Equal(1, file.ReadByte());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void RejectsEmptyAndOversizeWithoutLeakingHandle(int length)
    {
        string path = Path.Combine(Path.GetTempPath(), "imm-image-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, new byte[length]);
        try
        {
            Assert.Throws<InvalidDataException>(() => ImageFileReadPolicy.OpenRead(path, 4));
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidLimitBeforeOpening(long limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ImageFileReadPolicy.OpenRead("missing.jpg", limit));
}
