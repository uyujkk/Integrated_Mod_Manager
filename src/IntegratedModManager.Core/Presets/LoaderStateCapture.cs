using System.Diagnostics;

namespace IntegratedModManager.Core.Presets;

/// <summary>Flush loader memory before capture. Never changes file attributes or writes loader state.</summary>
public static class LoaderStateCapture
{
    public static async Task FlushAsync(string path, Func<CancellationToken, Task> sendReload,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(5);
        TimeSpan interval = pollInterval ?? TimeSpan.FromMilliseconds(150);
        if (limit <= TimeSpan.Zero || interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!string.Equals(Path.GetFileName(path), "d3dx_user.ini", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose d3dx_user.ini.", nameof(path));
        Snapshot before = await ReadAsync(path, cancellationToken);
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            throw new UnauthorizedAccessException("The loader state is read-only.");
        var clock = Stopwatch.StartNew();
        await sendReload(cancellationToken);
        Snapshot? previous = null;
        while (clock.Elapsed < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Snapshot current = await ReadAsync(path, cancellationToken);
                bool changed = current.Stamp != before.Stamp || !current.Bytes.AsSpan().SequenceEqual(before.Bytes);
                if (changed && current.Bytes.Length > 0 && previous is not null
                    && current.Stamp == previous.Stamp && current.Bytes.AsSpan().SequenceEqual(previous.Bytes)) return;
                previous = changed ? current : null;
            }
            catch (IOException) { previous = null; } // The loader may replace or exclusively open the file.
            TimeSpan remaining = limit - clock.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining < interval ? remaining : interval, cancellationToken);
        }
        throw new TimeoutException("No stable loader write was detected.");
    }

    private sealed record Snapshot(DateTime Stamp, byte[] Bytes);

    private static async Task<Snapshot> ReadAsync(string path, CancellationToken token)
    {
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Loader state exceeds 16 MB.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token);
        if (stream.Position != stream.Length || File.GetLastWriteTimeUtc(path) != stamp)
            throw new IOException("Loader state is changing.");
        return new Snapshot(stamp, bytes);
    }
}
