using System.Security.Cryptography;
using System.Text;
using IntegratedModManager.Core.Infrastructure;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Online preview images: downloaded once into the app's image cache (cache\online-images), decoded from there, and
/// trimmed by size (moved from the Online page in stage J6). The window attaches the HTTP client at startup.
/// </summary>
internal sealed class ImageService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    public const long MaxImageBytes = 20L * 1024 * 1024;
    private readonly string _cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-images");
    private Action<string, Exception> _log = (_, _) => { };

    private HttpClient Http { get; set; } = null!;

    /// <summary>Called once the window has its HTTP client and log.</summary>
    public void Attach(HttpClient http, Action<string, Exception> log)
    {
        Http = http;
        _log = log;
    }

    /// <summary>Shows a cached online image in <paramref name="image"/>.</summary>
    public async Task LoadCachedAsync(Image image, string url)
    {
        BitmapImage? bitmap = await LoadOnlineBitmapAsync(url, decodePixelWidth: 640);
        if (bitmap is not null) image.Source = bitmap;
    }

    private readonly OnlineImageLoadCoordinator _loads = new();

    public Task<BitmapImage?> LoadOnlineBitmapAsync(
        string imageUrl, CancellationToken cancellationToken = default, int decodePixelWidth = 0)
        => _loads.RunAsync(imageUrl, () => OnlineImageLoadAdapter.LoadAsync(imageUrl,
            (url, refresh, token) => GetCachedUriAsync(url, token, refresh),
            async (source, token) =>
            {
                token.ThrowIfCancellationRequested();
                // .NET streams handle long/non-ASCII cache paths. SetSourceAsync
                // reports decoding errors before the UI declares load success.
                BitmapImage bitmap = await LoadLocalBitmapAsync(source.LocalPath, decodePixelWidth);
                token.ThrowIfCancellationRequested();
                return bitmap;
            },
            exception => _log("Online image decode", exception), cancellationToken), cancellationToken);

    public static async Task<BitmapImage> LoadLocalBitmapAsync(string path, int decodePixelWidth = 0)
    {
        using var file = ImageFileReadPolicy.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var bitmap = new BitmapImage();
        if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    public Uri? TryGetExistingCachedUri(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? remoteUri)
            || (remoteUri.Scheme != Uri.UriSchemeHttp && remoteUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            string cachePath = GetOnlineImageCacheFilePath(remoteUri, imageUrl);
            if (!File.Exists(cachePath))
            {
                return null;
            }

            File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
            return new Uri(cachePath);
        }
        catch
        {
            return null;
        }
    }

    private string GetOnlineImageCacheFilePath(Uri remoteUri, string imageUrl)
    {
        string extension = Path.GetExtension(remoteUri.AbsolutePath).ToLowerInvariant();
        if (!MainWindow.ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            extension = ".jpg";
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)));
        return Path.Combine(_cachePath, hash + extension);
    }

    public async Task<Uri?> GetCachedUriAsync(
        string imageUrl,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? remoteUri)
            || (remoteUri.Scheme != Uri.UriSchemeHttp && remoteUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_cachePath);
            string cachePath = GetOnlineImageCacheFilePath(remoteUri, imageUrl);
            if (forceRefresh || !File.Exists(cachePath)
                || new FileInfo(cachePath).Length is <= 0 or > 26214400)
            {
                byte[]? bytes = await DownloadImageBytesWithRetryAsync(remoteUri, cancellationToken);
                if (bytes is null)
                {
                    return null;
                }

                if (bytes.Length == 0 || bytes.Length > 25 * 1024 * 1024)
                {
                    return null;
                }

                string temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
                try
                {
                    File.Move(temporaryPath, cachePath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }

            File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
            return new Uri(cachePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            UiCrashDiagnostics.Write("Image cache failure", exception.ToString());
            _log("Online image cache", exception);
            return null;
        }
    }

    public async Task<byte[]?> DownloadImageBytesWithRetryAsync(
        Uri remoteUri,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        OnlineImageDownload download = await OnlineImageDownloader.DownloadAsync(Http, remoteUri, timeout.Token);
        if (download.FailedStatus is { } status)
        {
            _log("Online image download",
                new HttpRequestException($"Image server returned HTTP {(int)status}.", null, status));
        }
        return download.Bytes;
    }

    public void TrimCache()
    {
        try
        {
            if (!Directory.Exists(_cachePath))
            {
                return;
            }

            FileInfo[] files = new DirectoryInfo(_cachePath)
                .EnumerateFiles()
                .OrderBy(file => file.LastAccessTimeUtc)
                .ToArray();
            long totalBytes = files.Sum(file => file.Length);
            const long trimThreshold = 250L * 1024 * 1024;
            const long targetSize = 200L * 1024 * 1024;
            if (totalBytes <= trimThreshold)
            {
                return;
            }

            foreach (FileInfo file in files)
            {
                file.Delete();
                totalBytes -= file.Length;
                if (totalBytes <= targetSize)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _log("Trim online image cache", ex);
        }
    }
}
