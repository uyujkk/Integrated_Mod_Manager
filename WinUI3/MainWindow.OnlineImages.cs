using IntegratedModManager.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ModFolderCopier.WinUI;

public sealed partial class MainWindow
{
    private Task<BitmapImage?> LoadOnlineBitmapAsync(
        string imageUrl, CancellationToken cancellationToken = default, int decodePixelWidth = 0)
        => OnlineImageLoadAdapter.LoadAsync(imageUrl,
            (url, refresh, token) => GetCachedOnlineImageUriAsync(url, token, refresh),
            async (source, token) =>
            {
                token.ThrowIfCancellationRequested();
                // .NET streams handle long/non-ASCII cache paths. SetSourceAsync
                // reports decoding errors before the UI declares load success.
                BitmapImage bitmap = await LoadLocalBitmapAsync(source.LocalPath, decodePixelWidth);
                token.ThrowIfCancellationRequested();
                return bitmap;
            },
            exception => LogApplicationIssue("Online image decode", exception), cancellationToken);

    private static async Task<BitmapImage> LoadLocalBitmapAsync(string path, int decodePixelWidth = 0)
    {
        using var file = ImageFileReadPolicy.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var bitmap = new BitmapImage();
        if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
