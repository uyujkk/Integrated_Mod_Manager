using System.Security.Cryptography;
using System.Text;
using IntegratedModManager.Core.Online;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private async Task SetCachedOnlineImageAsync(Image image, string imageUrl)
    {
        BitmapImage? bitmap = await LoadOnlineBitmapAsync(imageUrl, decodePixelWidth: 640);
        if (bitmap is not null) image.Source = bitmap;
    }

    private async Task SetCachedOnlineCharacterAvatarAsync(
        Image image,
        string imageUrl,
        int requestVersion,
        CancellationToken cancellationToken)
    {
        bool enteredGate = false;
        try
        {
            await _onlineCharacterAvatarDownloadGate.WaitAsync(cancellationToken);
            enteredGate = true;

            BitmapImage? bitmap = await LoadOnlineBitmapAsync(imageUrl, cancellationToken, 128);
            if (bitmap is null
                || cancellationToken.IsCancellationRequested
                || requestVersion != _onlineCharacterAvatarRequestVersion)
            {
                return;
            }

            image.Source = bitmap;
            image.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // The card was recycled or the page changed; its new image is loading.
        }
        catch (Exception exception)
        {
            LogApplicationIssue("Online character avatar", exception);
        }
        finally
        {
            if (enteredGate)
            {
                _onlineCharacterAvatarDownloadGate.Release();
            }
        }
    }

    private Uri? TryGetExistingCachedOnlineImageUri(string imageUrl)
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
        if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            extension = ".jpg";
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)));
        return Path.Combine(_onlineImageCachePath, hash + extension);
    }

    private async Task<Uri?> GetCachedOnlineImageUriAsync(
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
            Directory.CreateDirectory(_onlineImageCachePath);
            string cachePath = GetOnlineImageCacheFilePath(remoteUri, imageUrl);
            if (forceRefresh || !File.Exists(cachePath)
                || new FileInfo(cachePath).Length is <= 0 or > 26214400)
            {
                byte[]? bytes = await DownloadOnlineImageBytesWithRetryAsync(remoteUri, cancellationToken);
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
            LogApplicationIssue("Online image cache", exception);
            return null;
        }
    }

    private async Task<byte[]?> DownloadOnlineImageBytesWithRetryAsync(
        Uri remoteUri,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OnlinePreviewImageRequestTimeout);
        OnlineImageDownload download = await OnlineImageDownloader.DownloadAsync(_httpClient, remoteUri, timeout.Token);
        if (download.FailedStatus is { } status)
        {
            LogApplicationIssue("Online image download",
                new HttpRequestException($"Image server returned HTTP {(int)status}.", null, status));
        }
        return download.Bytes;
    }

    private void TrimOnlineImageCache()
    {
        try
        {
            if (!Directory.Exists(_onlineImageCachePath))
            {
                return;
            }

            FileInfo[] files = new DirectoryInfo(_onlineImageCachePath)
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
            LogApplicationIssue("Trim online image cache", ex);
        }
    }
}
