using System.Security.Cryptography;

namespace IntegratedModManager.Core;

public static class UpdatePackageFile
{
    private const int BufferSize = 128 * 1024;

    public static async Task<long> WriteAsync(
        Stream source,
        string destinationPath,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        long written = 0;
        await using (var destination = new FileStream(
                         destinationPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         BufferSize,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            byte[] buffer = new byte[BufferSize];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                progress?.Invoke(written);
            }

            await destination.FlushAsync(cancellationToken);
        }

        return written;
    }

    public static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
    }
}
