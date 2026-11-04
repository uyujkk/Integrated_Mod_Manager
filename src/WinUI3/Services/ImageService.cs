using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Online preview images from the app's image cache. The cache still lives with the Online page in
/// MainWindow (stage J6 moves it), so the window sets the loader at startup.
/// </summary>
internal sealed class ImageService
{
    public Func<Image, string, Task>? Loader { get; set; }

    public Task LoadCachedAsync(Image image, string url) => Loader?.Invoke(image, url) ?? Task.CompletedTask;
}
