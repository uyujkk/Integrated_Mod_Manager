using System.Text;
using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class CompleteFileSnapshotTests
{
    [Fact]
    public void Validate_AcceptsExactCompleteFile()
    {
        byte[] content = Encoding.UTF8.GetBytes("; comments and arbitrary mod state\r\n$\\Mods\\A\\state = custom-value\r\n");
        string hash = CompleteFileSnapshot.ComputeSha256(content);

        CompleteFileSnapshot.Validate(content, content.LongLength, hash.ToLowerInvariant());
    }

    [Fact]
    public void Validate_RejectsChangedContentWithSameLength()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, 0x31, 0x32, 0x33];
        byte[] changed = [0xEF, 0xBB, 0xBF, 0x31, 0x32, 0x34];
        string hash = CompleteFileSnapshot.ComputeSha256(original);

        Assert.Throws<InvalidDataException>(() =>
            CompleteFileSnapshot.Validate(changed, original.LongLength, hash));
    }

    [Fact]
    public void Validate_RejectsChangedFileSize()
    {
        byte[] content = [0x00, 0xFF, 0x7F];
        string hash = CompleteFileSnapshot.ComputeSha256(content);

        Assert.Throws<InvalidDataException>(() =>
            CompleteFileSnapshot.Validate(content, content.LongLength + 1, hash));
    }

    [Fact]
    public void WriteAtomically_PreservesEveryByteAndReplacesExistingFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IntegratedModManagerTests", Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(directory, "d3dx_user.ini");
        byte[] snapshot = [0xEF, 0xBB, 0xBF, 0x00, 0x0D, 0x0A, 0xFF, 0x5B, 0x5D];

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(destination, "old state");

            CompleteFileSnapshot.WriteAtomically(destination, snapshot);

            Assert.Equal(snapshot, File.ReadAllBytes(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
