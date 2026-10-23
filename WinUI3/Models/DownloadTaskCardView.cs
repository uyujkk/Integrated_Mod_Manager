using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

public sealed class DownloadTaskCardView
{
    public DownloadTaskCardView(TextBlock statusTextBlock, ProgressBar progressBar, Button actionButton)
    {
        StatusTextBlock = statusTextBlock;
        ProgressBar = progressBar;
        ActionButton = actionButton;
    }

    public TextBlock StatusTextBlock { get; }

    public ProgressBar ProgressBar { get; }

    public Button ActionButton { get; }
}
