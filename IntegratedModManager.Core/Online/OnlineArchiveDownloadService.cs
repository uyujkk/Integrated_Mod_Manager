using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace IntegratedModManager.Core;

public sealed record OnlineArchiveDownloadRequest(string DownloadUrl, string ProfileUrl,
    string PreferredFileName, string Title, int ItemId, long ExpectedBytes);
public sealed record OnlineArchiveDownloadProgress(long BytesRead, long TotalBytes)
{
    public double Percent => TotalBytes > 0 ? Math.Clamp(BytesRead * 100d / TotalBytes, 0, 100) : 0;
}
public sealed record OnlineArchiveDownloadResult(string ArchivePath, long BytesRead);
public sealed class OnlineArchiveSizeException(long expected, long actual)
    : IOException($"Downloaded file size is incorrect. Expected approximately {expected} bytes, received {actual} bytes.")
{
    public long ExpectedBytes { get; } = expected;
    public long ActualBytes { get; } = actual;
}

/// <summary>Transport only. Caller owns destination choice, extraction, metadata and UI.
/// The borrowed HttpClient is not disposed. Only the newly created partial file is cleaned up.
/// Publication never overwrites an existing destination entry.</summary>
public sealed class OnlineArchiveDownloadService(HttpClient client)
{
    public async Task<OnlineArchiveDownloadResult> DownloadAsync(OnlineArchiveDownloadRequest download,
        string destinationFolder, Action<OnlineArchiveDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(download);
        if (!Uri.TryCreate(download.DownloadUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("An HTTP(S) download URL is required.", nameof(download));
        string destination = Path.GetFullPath(destinationFolder);
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("The download destination no longer exists.");
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = Uri.TryCreate(download.ProfileUrl, UriKind.Absolute, out Uri? referer)
            && referer.Scheme is "http" or "https" ? referer : new Uri("https://gamebanana.com/");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string name = OnlineDownloadFilePolicy.ResolveFileName(
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName,
            download.PreferredFileName, response.RequestMessage?.RequestUri, download.Title, download.ItemId);
        string partial = Path.Combine(destination, $".imm-{Guid.NewGuid():N}.download");
        bool ownsPartial = false;
        long read = 0;
        long total = download.ExpectedBytes > 0 ? download.ExpectedBytes : response.Content.Headers.ContentLength ?? 0;
        var watch = Stopwatch.StartNew();
        TimeSpan lastDispatch = TimeSpan.Zero;
        void Report() => progress?.Invoke(new(read, total));
        try
        {
            await using (var local = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                ownsPartial = true;
                Report();
                await using Stream remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                byte[] buffer = new byte[81920];
                int received;
                while ((received = await remote.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await local.WriteAsync(buffer.AsMemory(0, received), cancellationToken).ConfigureAwait(false);
                    read += received;
                    if (watch.Elapsed - lastDispatch >= TimeSpan.FromMilliseconds(125))
                    {
                        lastDispatch = watch.Elapsed;
                        Report();
                    }
                }
                await local.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (download.ExpectedBytes > 0 && Math.Abs(read - download.ExpectedBytes) > 1024)
                throw new OnlineArchiveSizeException(download.ExpectedBytes, read);
            ValidateResponse(partial, response.Content.Headers.ContentType?.MediaType);
            Report();
            cancellationToken.ThrowIfCancellationRequested();
            string published = OnlineDownloadFilePolicy.MoveWithoutOverwrite(partial, Path.Combine(destination, name));
            ownsPartial = false;
            return new(published, read);
        }
        finally
        {
            if (ownsPartial)
            {
                try { File.Delete(partial); }
                catch { /* Best effort: never delete an existing user's file. */ }
            }
        }
    }

    private static void ValidateResponse(string file, string? mediaType)
    {
        if (!string.IsNullOrEmpty(OnlineDownloadFilePolicy.DetectArchiveExtension(file))) return;
        // A bounded read, not File.ReadAllBytes on a potentially large response.
        using var stream = File.OpenRead(file);
        byte[] bytes = new byte[512];
        int count = stream.Read(bytes);
        if (count == 0) throw new InvalidDataException("The download returned an empty file.");
        string preview = Encoding.UTF8.GetString(bytes, 0, count);
        string type = mediaType?.ToLowerInvariant() ?? string.Empty;
        if (type.Contains("text/") || type.Contains("html") || type.Contains("json")
            || preview.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || preview.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
            || preview.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || preview.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The download returned a web page or text response instead of an archive.");
    }
}

public static class OnlineDownloadFilePolicy
{
    public static string ResolveFileName(string? serverName, string? preferredName, Uri? responseUri, string title, int itemId)
    {
        foreach (string? value in new[] { serverName, preferredName,
            responseUri is not null && Path.HasExtension(responseUri.LocalPath) ? responseUri.LocalPath : null })
        {
            string safe = SafeLeafName(value);
            if (safe.Length > 0) return safe;
        }
        string safeTitle = SafeLeafName(title);
        return SafeLeafName($"{(safeTitle.Length > 0 ? safeTitle : "mod")}-{itemId}.zip");
    }

    private static string SafeLeafName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        // Apply Windows rules even in portable Core tests. Never trust HTTP filenames as paths.
        string leaf = name.Trim().Trim('"').Replace('\\', '/').Split('/').Last().Trim();
        leaf = string.Concat(leaf.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim().TrimEnd('.', ' ');
        if (leaf is "" or "." or "..") return string.Empty;
        string stem = leaf.Split('.')[0].TrimEnd(' ', '.');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
            leaf = "_" + leaf;
        if (leaf.Length > 200)
        {
            string extension = Path.GetExtension(leaf);
            leaf = extension.Length <= 20 ? leaf[..(200 - extension.Length)] + extension : leaf[..200];
        }
        return leaf;
    }

    public static string MoveWithoutOverwrite(string source, string desired)
    {
        for (int suffix = 1; suffix <= 10000; suffix++)
        {
            string candidate = suffix == 1 ? desired : Path.Combine(Path.GetDirectoryName(desired)!,
                $"{Path.GetFileNameWithoutExtension(desired)}-{suffix}{Path.GetExtension(desired)}");
            try { File.Move(source, candidate, overwrite: false); return candidate; }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
        }
        throw new IOException("No unused download file name could be reserved.");
    }

    public static string DetectArchiveExtension(string file)
    {
        using var stream = File.OpenRead(file);
        Span<byte> header = stackalloc byte[8];
        int count = stream.Read(header);
        ReadOnlySpan<byte> bytes = header[..count];
        if (count >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] is 0x03 or 0x05 or 0x07 && bytes[3] is 0x04 or 0x06 or 0x08) return ".zip";
        if (bytes.StartsWith(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C })) return ".7z";
        if (bytes.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 })) return ".rar";
        if (bytes.StartsWith(new byte[] { 0x1F, 0x8B })) return ".gz";
        if (bytes.StartsWith(new byte[] { 0x42, 0x5A, 0x68 })) return ".bz2";
        if (bytes.StartsWith(new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 })) return ".xz";
        if (bytes.StartsWith(new byte[] { 0x28, 0xB5, 0x2F, 0xFD })) return ".zst";
        return string.Empty;
    }
}
