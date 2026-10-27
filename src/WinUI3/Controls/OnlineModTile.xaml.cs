using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>One online Mod in the grid view, bound to <see cref="OnlineModTileViewModel"/>.</summary>
public sealed partial class OnlineModTile : UserControl
{
    public OnlineModTile(OnlineModTileViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public OnlineModTileViewModel ViewModel { get; }

    // The window fills these: the preview goes through the shared image cache, and the
    // author link and installed badge keep their existing behaviour and menus.
    public Image Preview => PreviewImage;

    public ContentControl Author => AuthorHost;

    public ContentControl Installed => InstalledHost;

    public Border Card => CardBorder;

    public static Visibility CollapsedWhen(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
