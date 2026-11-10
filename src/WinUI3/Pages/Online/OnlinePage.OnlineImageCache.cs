using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ModFolderCopier.WinUI;

public sealed partial class OnlinePage
{
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

            BitmapImage? bitmap = await _services.Images.LoadOnlineBitmapAsync(imageUrl, cancellationToken, 128);
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
            _host.LogApplicationIssue("Online character avatar", exception);
        }
        finally
        {
            if (enteredGate)
            {
                _onlineCharacterAvatarDownloadGate.Release();
            }
        }
    }
}
