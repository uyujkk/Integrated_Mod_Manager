using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ModFolderCopier.WinUI;

/// <summary>
/// The XamlRoot dialogs open in. Stage J8 moves the dialogs themselves here and makes sure only one
/// is open at a time.
/// </summary>
internal sealed class DialogService
{
    private Func<XamlRoot?> _root = () => null;

    public XamlRoot? Root => _root();

    /// <summary>Called once the window exists.</summary>
    public void Attach(Func<XamlRoot?> root) => _root = root;

    public bool IsDialogOpen => Root is XamlRoot root
        && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is ContentDialog);
}
