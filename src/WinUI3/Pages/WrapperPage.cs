using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Stage J1: one page of the window. It holds a page container that MainWindow.xaml still defines,
/// moved here at startup, so the window shows pages by swapping what its page host displays.
/// Stages J2-J7 replace each wrapper with a real page (docs/development/UI-Structure-Plan.md).
/// </summary>
internal sealed partial class WrapperPage : UserControl
{
    public WrapperPage(FrameworkElement container)
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        // Hidden pages are no longer collapsed; they are simply not in the page host.
        container.Visibility = Visibility.Visible;
        Content = container;
    }
}
