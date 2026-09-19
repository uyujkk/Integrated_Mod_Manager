using System.Security.Cryptography;

namespace IntegratedModManager.Core;

public static class CompleteFileSnapshot
{
    public static string ComputeSha256(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexString(SHA256.HashData(content));
    }

    public static void Validate(byte[] content, long expectedSize, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (content.LongLength != expectedSize)
        {
            throw new InvalidDataException("The snapshot file size does not match its metadata.");
        }

        string actualSha256 = ComputeSha256(content);
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The snapshot failed its SHA-256 integrity check.");
        }
    }

    public static void WriteAtomically(string destinationPath, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException("The snapshot file has no parent directory.");
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, content);
            if (!File.Exists(fullDestinationPath))
            {
                File.Move(tempPath, fullDestinationPath);
                return;
            }

            try
            {
                File.Replace(tempPath, fullDestinationPath, null, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
            {
                File.Move(tempPath, fullDestinationPath, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
