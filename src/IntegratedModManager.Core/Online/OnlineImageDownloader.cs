using System.Net;

namespace IntegratedModManager.Core.Online;

/// <summary>The image bytes, or the status of the last failed attempt.</summary>
public sealed record OnlineImageDownload(byte[]? Bytes, HttpStatusCode? FailedStatus);

/// <summary>
/// Preview and avatar images. A busy or failing image server gets two more tries, waiting as it asks
/// or with a growing delay of at most six seconds; images above 25 MB are skipped.
/// </summary>
public static class OnlineImageDownloader
{
    public const long MaxImageBytes = 25 * 1024 * 1024;
    public const int MaxAttempts = 3;

    public static async Task<OnlineImageDownload> DownloadAsync(HttpClient client, Uri imageUri, CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        for (int attempt = 0; ; attempt++)
        {
            using HttpResponseMessage response = await client.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response.Content.Headers.ContentLength is > MaxImageBytes
                    ? new OnlineImageDownload(null, null)
                    : new OnlineImageDownload(await ReadLimitedAsync(response.Content, cancellationToken), null);
            }

            bool retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!retryable || attempt == MaxAttempts - 1)
            {
                return new OnlineImageDownload(null, response.StatusCode);
            }

            await delay(RetryDelay(response.Headers.RetryAfter?.Delta, attempt), cancellationToken);
        }
    }

    // A response may not say how long it is, so the limit is counted while reading; null once it is passed.
    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>The server's Retry-After, else 0.6 s doubling per attempt; never more than six seconds.</summary>
    public static TimeSpan RetryDelay(TimeSpan? retryAfter, int attempt)
    {
        TimeSpan delay = retryAfter ?? TimeSpan.FromMilliseconds(600 * Math.Pow(2, attempt));
        return delay > TimeSpan.FromSeconds(6) ? TimeSpan.FromSeconds(6) : delay;
    }
}
