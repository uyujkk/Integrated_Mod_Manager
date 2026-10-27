using Microsoft.UI.Xaml.Controls;

namespace ModFolderCopier.WinUI;

/// <summary>One online Mod in the list view, bound to the same <see cref="OnlineModTileViewModel"/> as a grid tile.</summary>
public sealed partial class OnlineModRow : UserControl, IOnlineModView
{
    public OnlineModRow(OnlineModTileViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public OnlineModTileViewModel ViewModel { get; }

    public Image Preview => PreviewImage;

    public ContentControl Author => AuthorHost;

    public ContentControl Installed => InstalledHost;

    public Border Card => CardBorder;
}
