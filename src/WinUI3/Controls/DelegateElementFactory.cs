using Microsoft.UI.Xaml;

namespace ModFolderCopier.WinUI;

/// <summary>
/// Builds each ItemsRepeater element in code, so long lists only create the rows on screen.
/// Elements are not recycled: a row scrolled back into view is built again.
/// </summary>
internal sealed partial class DelegateElementFactory(Func<object, UIElement> build) : IElementFactory
{
    public UIElement GetElement(ElementFactoryGetArgs args) => build(args.Data);

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
    }
}
